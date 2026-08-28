# 아키텍처 — 스레딩 모델 / Job 시스템 / Zone

> 이 문서는 게임 서버의 핵심 구조 — 스레드 분리 / JobSerializer / Zone 시스템 — 의
> *왜 이렇게 설계했는가*에 초점을 둔다. 패킷/세션은 [networking.md](networking.md) 참고.

---

## 1. 컴포넌트 토폴로지

```
                    Docker Compose 스택
┌──────────────────────────────────────────────────────────────────┐
│                                                                  │
│  ┌──────────────┐  HTTP   ┌─────────────────┐                    │
│  │ DummyClient  │────────▶│ AccountServer   │──┐                 │
│  │              │  :5000  │ (ASP.NET Core)  │  │                 │
│  └───────┬──────┘         └─────────────────┘  │                 │
│          │                                     │                 │
│          │  TCP            ┌─────────────────┐ │  ┌────────────┐ │
│          └────────────────▶│   GameServer    │─┼─▶│  MariaDB   │ │
│            :7777           │   (.NET 10)     │ │  │ Account/   │ │
│                            │                 │ │  │ Shared/    │ │
│                            │  3 Threads:     │ │  │ Game/Log   │ │
│                            │  - GameLogic    │ │  └────────────┘ │
│                            │  - DB           │ │                 │
│                            │  - LogDB        │ │  ┌────────────┐ │
│                            │  + IOCP pool    │ └─▶│  Redis 7   │ │
│                            └─────────────────┘    │ token/log  │ │
│                                                   └────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

---

## 2. 스레딩 모델

| 스레드 | 책임 | 구현 |
|---|---|---|
| **GameLogic** (Main) | 게임 로직 전체 (모든 Room Update) | 30Hz fixed timestep loop |
| **DB** | 인메모리 → MariaDB 영속화 | `DbTransaction.FlushBlocking()` |
| **LogDB** | 게임 로그 배치 → MariaDB | `LogTransaction.FlushBlocking()` |
| **IOCP pool** | 비동기 Socket I/O (Recv/Send) | .NET ThreadPool (`SetMinThreads(200,200)`) |

### 왜 GameLogic 1개?

- **데이터 경합 0** — 모든 게임 상태는 GameLogic 스레드에서만 변경. lock 불필요.
- **단점**: 단일 코어 한계. 12코어 중 1개만 쓰며, 그 1코어가 **700 CCU 부근에서 포화**(틱 p99 33.9ms). [load-test.md](load-test.md) 참고.
- **트레이드오프 판단**: 모바일 RPG 같은 비실시간 도메인이면 충분. 대규모 MMO면 Room/Zone 단위 병렬화 필요.

### 30Hz 게임 루프

```csharp
// Server/Program.cs:107
const int FrameMs = 33;

while (!_cts.Token.IsCancellationRequested)
{
    long frameStart = Stopwatch.GetTimestamp();
    GameLogic.Instance.Update();    // ← 모든 Room.Update() 호출

    long elapsedMs = ...;
    int sleepMs = (int)(FrameMs - elapsedMs);
    if (sleepMs > 0)
        Thread.Sleep(sleepMs);       // budget 남으면 sleep, 넘으면 즉시 다음 frame (catch-up)
}
```

Windows 환경에서는 `timeBeginPeriod(1)` 호출로 timer 정밀도 15.625ms → 1ms로 끌어올림.
부작용 (시스템 timer interrupt ↑)은 서버 환경에선 무의미.

---

## 3. Job 시스템 — 락 없이 안전한 동시성

게임 로직에서 다른 스레드(IOCP, DB)로부터 GameLogic 스레드로 작업을 넘기는 표준 패턴.

### 구조

```csharp
// Server/Game/Job/JobSerializer.cs
public class JobSerializer
{
    ConcurrentQueue<IJob> _jobQueue = new();
    JobTimer _timer = new();

    public void Push(Action action) { _jobQueue.Enqueue(new Job(action)); }
    public IJob PushAfter(int tickAfter, Action action) { ... }   // 지연 실행

    public void Flush()
    {
        _timer.Flush();
        while (_jobQueue.TryDequeue(out IJob job))
        {
            if (job.Cancel) continue;
            job.Execute();
        }
    }
}
```

### 상속 관계

```
JobSerializer
├── GameLogic   (RoomManager — 1개 인스턴스)
├── GameRoom    (방마다 1개)
└── DbTransaction / LogTransaction
```

GameLogic / GameRoom 둘 다 JobSerializer를 상속받지만, **실제로 Flush()가 호출되는 스레드는 GameLogic 스레드 한 곳**이다 (DbTransaction은 DB 스레드).

### 사용 패턴

IOCP 콜백(다른 스레드)에서 게임 상태를 만지고 싶을 때:

```csharp
// PacketHandler.cs - C_Move 핸들러 (IOCP 스레드에서 실행)
public static void C_MoveHandler(PacketSession session, ReadOnlySpan<byte> data)
{
    ClientSession clientSession = session as ClientSession;
    GameRoom room = clientSession.MyPlayer?.Room;

    // ⚠️ 여기서 직접 room._players[...] 만지면 race condition!
    // ⭕ 대신 Job으로 넘김 — GameLogic 스레드에서 안전하게 실행됨
    room.Push(() =>
    {
        clientSession.MyPlayer.HandleMove(...);
    });
}
```

이 패턴 덕분에 게임 컨텐츠 코드 어디에도 `lock`이 없다.

---

## 4. GameLogic — 사실상 RoomManager

```csharp
// Server/Game/Room/GameLogic.cs
public class GameLogic : JobSerializer
{
    public static GameLogic Instance { get; } = new GameLogic();
    Dictionary<int, GameRoom> _rooms = new();

    public void Update()
    {
        Flush();                            // 1. GameLogic 큐 처리 (Room 생성/제거 등)

        foreach (GameRoom room in _rooms.Values)
            room.Update();                   // 2. 각 Room 자기 큐 Flush + 컨텐츠 업데이트

        ServerMetrics.RecordTick(...);       // 3. 메트릭 기록
    }
}
```

매 33ms마다 모든 Room의 Update가 순차 호출된다. **Room별 독립 스레드 분리는 향후 과제**.

### GracefulShutdown 지원

```csharp
public void FlushAll()                       // 종료 시퀀스에서 호출
{
    Flush();                                 // GameLogic 자기 큐
    foreach (var room in _rooms.Values)
        room.Flush();                        // 모든 Room 큐
}
```

남은 잡들을 전부 DB로 흘려보낸 뒤 종료. [graceful-shutdown.md](graceful-shutdown.md) 참고.

---

## 5. Zone 시스템 — 3D 공간 분할

`GameRoom`은 맵을 `ZoneCells × ZoneCells × ZoneCells` 단위 cube로 분할 (`ZoneCells=10`).
플레이어/몬스터/투사체는 자기 위치에 해당하는 Zone에 등록된다.

```csharp
// Server/Game/Room/GameRoom.cs
public const int VisionCells = 5;       // 시야 범위
public int ZoneCells { get; }           // 10 (Init 시 주입)
public Zone[,,] Zones { get; }

public Zone GetZone(Vector3Int cellPos)
{
    int x = (cellPos.x - Map.MinX) / ZoneCells;
    int y = (cellPos.y - Map.MinY) / ZoneCells;
    int z = (cellPos.z - Map.MinZ) / ZoneCells;
    return Zones[x, y, z];
}
```

### 시야 (Vision) 처리

브로드캐스트가 필요한 패킷(이동/스킬/스폰)은 **자기 Zone + 인접 Zone에만** 전송:

```csharp
// 의사 코드
foreach (Zone zone in GetAdjacentZones(myZone, range: VisionCells))
{
    foreach (Player p in zone.Players)
        p.Session.Send(packet);
}
```

이렇게 하지 않으면 1000명 전체에게 broadcast → O(N²) → 즉시 망함.
Zone 분할 덕분에 평균 시야 인구 수십 명 수준으로 유지.

---

## 6. ObjectManager — ID에 타입 인코딩

```csharp
// Server/Game/Object/ObjectManager.cs
// [UNUSED(1)] [TYPE(7)] [ID(24)]
int GenerateId(GameObjectType type)
{
    return ((int)type << 24) | (_counter++);
}

public static GameObjectType GetObjectTypeById(int id)
{
    int type = (id >> 24) & 0x7F;
    return (GameObjectType)type;
}
```

- ID 한 번 보고 Player/Monster/Projectile 구분 가능 → Dictionary 분기 필요 없음
- 24비트 ID 공간 (≈1,600만) — 단일 서버 라이프타임에 충분
- type 7비트 → 최대 128종 GameObject 타입 (현재 4종 사용)

---

## 7. 클래스 계층 요약

```
Session (ServerCore)
└── PacketSession
    └── ClientSession
        + AccountDbId, MyPlayer, ServerState

GameObject
├── Player (+ Stat, Inven, Skills, Session ref)
├── Monster (+ AI 상태 머신)
└── Projectile (+ Owner, Damage)

JobSerializer
├── GameLogic   (RoomManager)
├── GameRoom    (Zones, Players Dict, Monsters Dict, ...)
└── DbTransaction / LogTransaction
```

각 Player는 자기 Zone과 Room을 알고, 자기 Session을 통해 패킷을 보낸다. Session은 ClientSession이고, ClientSession은 자기 MyPlayer와 ServerState를 안다 — 순환 참조이지만 단일 스레드 접근이라 안전.

---

## 8. 관련 문서

- [networking.md](networking.md) — Custom 패킷 / Span 파싱 / Session 락프리 큐
- [auth.md](auth.md) — AccountServer + Redis 토큰 인증 흐름
- [persistence.md](persistence.md) — DbTransaction 3-step / 로그 배치
- [graceful-shutdown.md](graceful-shutdown.md) — 종료 시퀀스
- [load-test.md](load-test.md) — 접속 수용/게임 로직 두 한계 측정 + 병목 분석
- [monitoring.md](monitoring.md) — 메트릭(Prometheus/Grafana) · 로그(ES/Kibana)
