# 영속성 — DbTransaction 3-step / Dapper 로그 배치 / DLQ

> 게임 서버에서 DB는 두 가지 패턴으로 다룬다.
> - **운영 데이터(GameDB)**: EF Core, GameRoom ↔ DB 스레드 **3-step 동기화** 패턴
> - **로그(LogDB)**: Dapper + 배치 commit + **DLQ로 유실 방지**
> 두 경로 모두 GameLogic 스레드를 절대 블로킹하지 않는다.

---

## 1. DB 4개 — 책임 분리

| DB | 책임 | 접근 주체 |
|---|---|---|
| **AccountDB** | 계정 이름/비밀번호 | AccountServer 전용 |
| **SharedDB** | 서버 레지스트리 (이름/IP/Port/BusyScore), 토큰 메타 | AccountServer + GameServer |
| **GameDB** | 캐릭터, 인벤토리, 스탯 | GameServer 전용 |
| **LogDB** | 로그 (login, reward, ...) | GameServer 전용 (Dapper batch) |

운영/로그 분리의 의미:
- LogDB가 느려져도 게임 데이터 저장에 영향 없음
- 운영 DB의 트랜잭션 부담을 로그가 끌어내리지 않음

---

## 2. DbTransaction — 3-step 패턴

### 문제

GameLogic 스레드에서 `db.SaveChanges()`를 호출하면 → DB I/O 대기 동안 33ms tick budget 다 소진 → 전 유저 lag.

### 해결 — GameRoom 스레드 ↔ DB 스레드 핸드오프

```
┌──────────────────────┐   ┌──────────────┐   ┌──────────────────────┐
│ Step1 (GameLogic)    │   │ Step2 (DB)   │   │ Step3 (GameLogic)    │
│ 메모리 데이터 → DTO │──▶│ EF SaveChanges │──▶│ 결과를 메모리에 반영 │
│ DbTransaction.PushJob│   │ FlushBlocking│   │ room.Push(...)       │
└──────────────────────┘   └──────────────┘   └──────────────────────┘
```

각 단계는 *항상 올바른 스레드*에서 실행된다.

### 코드 — `RewardPlayer` (가장 복잡한 예시)

```csharp
// Server/DB/DbTransaction.cs
public static void RewardPlayer(Player player, RewardData rewardData, GameRoom room)
{
    // [Step 1: GameLogic 스레드] 인벤 빈 슬롯 찾기 + DTO 생성
    int? slot = player.Inven.GetEmptySlot();
    if (slot == null) return;

    var itemDb = new ItemDb {
        TemplateId = rewardData.itemId,
        Count      = rewardData.count,
        Slot       = slot.Value,
        OwnerDbId  = player.PlayerDbId
    };

    // [Step 2: DB 스레드] EF로 저장
    Instance.PushJob(() =>
    {
        using var db = new AppDbContext();
        db.Items.Add(itemDb);
        if (!db.SaveChangesEx()) return;

        // [Step 3: GameLogic 스레드] 메모리 적용 + Stale 재검증
        room.Push(() =>
        {
            // DB 왕복 사이 player가 disconnect 됐을 수도
            if (player.Room == null) return;

            // 다른 핸들러(EquipItem 등)가 slot을 점유했을 수 있음 → 재검증
            int? slotNow = player.Inven.GetEmptySlot();
            if (slotNow == null)
            {
                // 인벤 가득 참 → 방금 저장한 DB 행을 삭제 (보상 ghost 방지)
                Instance.PushJob(() => { /* db.Items.Remove(itemDb); */ });
                return;
            }

            if (slotNow.Value != itemDb.Slot)
            {
                // 슬롯이 바뀌었으면 DB Slot 컬럼 업데이트
                itemDb.Slot = slotNow.Value;
                Instance.PushJob(() => { /* UPDATE Items SET Slot=... */ });
            }

            player.Inven.Add(Item.MakeItem(itemDb));
            player.Session.Send(new S_AddItem(...));     // 클라 알림
        });
    });
}
```

### 핵심 원칙

1. **메모리 접근은 항상 GameLogic 스레드** — Step 1, 3
2. **DB 접근은 항상 DB 스레드** — Step 2 (`DbTransaction.PushJob`)
3. **DB 왕복 중에 상태가 바뀔 수 있음** — Step 3에서 stale 재검증 필수
4. **재검증 실패 시 DB 보상 로직** — Step 3에서 다시 `PushJob`으로 DB 정정

---

## 3. DbTransaction — Poison Pill + 드롭 카운터

```csharp
private readonly BlockingCollection<Action> _jobQueue = new();
private long _droppedJobCount;
public long DroppedJobCount => Interlocked.Read(ref _droppedJobCount);

public bool PushJob(Action job)
{
    if (_jobQueue.IsAddingCompleted)
    {
        Interlocked.Increment(ref _droppedJobCount);   // ⭐ 셧다운 후 드롭은 카운트
        return false;
    }
    try { _jobQueue.Add(job); return true; }
    catch (InvalidOperationException)
    {
        // TOCTOU: IsAddingCompleted 체크와 Add 사이에 StopAcceptingJobs 끼어듦
        Interlocked.Increment(ref _droppedJobCount);
        return false;
    }
}

public void StopAcceptingJobs() => _jobQueue.CompleteAdding();
```

```csharp
public void FlushBlocking()
{
    // 큐가 비면 스레드 sleep(CPU 0%), CompleteAdding 후 비면 자동 탈출
    foreach (Action job in _jobQueue.GetConsumingEnumerable())
    {
        try { job.Invoke(); }
        catch (Exception e) { /* 개별 쿼리 실패가 스레드 전체를 죽이지 않게 */ }
    }
}
```

`BlockingCollection.GetConsumingEnumerable`을 쓰면 sleep/wakeup 직접 관리 안 해도 됨.

`DroppedJobCount`는 GracefulShutdown에서 포스트모템 로깅에 사용 → [graceful-shutdown.md](graceful-shutdown.md)

---

## 4. LogTransaction — Dapper + 배치 커밋 + DLQ

운영 데이터와 달리 로그는 **개별 row 정확성보다 throughput**이 중요. 게다가 EF Core의 변경 추적 오버헤드도 불필요.

```csharp
// Server/DB/LogDB/LogManager.cs
private const int MAX_BATCH_SIZE = 500;

public void FlushBlocking()
{
    var batch = new List<LogJob>(MAX_BATCH_SIZE);

    foreach (LogJob first in _logQueue.GetConsumingEnumerable())
    {
        batch.Add(first);
        // 이미 쌓여있는 잡을 non-blocking으로 최대한 긁어와 한 배치로 묶음
        while (batch.Count < MAX_BATCH_SIZE && _logQueue.TryTake(out var more))
            batch.Add(more);

        FlushBatch(batch);
        batch.Clear();
    }
}
```

### FlushBatch — SQL별 그룹 → 단일 커넥션/트랜잭션

```csharp
using var conn = new MySqlConnection(_connectionString);
conn.Open();
using var tx = conn.BeginTransaction();
try
{
    // 같은 SQL끼리 묶어서 Dapper에 IEnumerable로 → command 재사용 (prepared)
    foreach (var group in batch.GroupBy(j => j.Sql))
    {
        var rows = group.Select(j => j.Data).ToList();
        conn.Execute(group.Key, rows, transaction: tx);
    }
    tx.Commit();
}
catch
{
    tx.Rollback();
    throw;
}
```

#### 왜 EF Core가 아니라 Dapper?

| 항목 | EF Core | Dapper |
|---|---|---|
| 변경 추적 | 있음 (오버헤드) | 없음 |
| 로그처럼 INSERT 전용 워크로드 | 과함 | 딱 맞음 |
| Bulk insert 효율 | 별도 라이브러리 필요 | `IEnumerable` 그대로 |
| 트랜잭션 제어 | 추상화 | 직접 |

---

## 5. DLQ (Dead Letter Queue) — 배치 실패 시 유실 방지

배치 전체가 실패하면 (DB 다운, 네트워크 문제 등) **파일로 덤프**한다:

```csharp
catch (Exception e)
{
    bool dlqSaved = WriteToDeadLetter(batch, e);

    if (dlqSaved)
    {
        Interlocked.Add(ref _deadLetterCount, batch.Count);
        Console.WriteLine($"LogDB Batch Error ({batch.Count} rows → DLQ): {e.Message}");
    }
    else
    {
        // DLQ 쓰기도 실패 — 진짜 유실 (디스크 풀 / 권한 등)
        Interlocked.Add(ref _droppedLogCount, batch.Count);
    }
}
```

### DLQ 파일 포맷 — JSON Lines

```jsonl
{"ts":"2026-05-27T04:25:11Z","sql":"INSERT INTO log_login ...","data":{"PlayerDbId":42,"IsLogin":true,...},"error":"Connection timeout"}
{"ts":"2026-05-27T04:25:11Z","sql":"INSERT INTO log_login ...","data":{"PlayerDbId":43,"IsLogin":true,...},"error":"Connection timeout"}
```

- 한 줄 = 한 row → grep / jq 친화
- `append: true` → 크래시 루프에서도 누적
- 위치: `logs/deadletter/logdb-YYYYMMDD.jsonl`
- 운영자가 jsonl을 읽어 수동 복구 (재실행 스크립트로 INSERT)

이 패턴이 없으면 LogDB가 일시적으로 죽었을 때 **그 시간대 로그가 전부 사라짐** → 부정/사기 분석/감사 시 빈 구멍.

---

## 6. 사용 패턴 (LogHelper)

람다 없이 호출 가능한 헬퍼 제공:

```csharp
// Server/DB/LogDB/LogManager.cs
public static void LogLogin(int playerDbId, bool isLogin, string ipAddress)
{
    var log = new Log_LoginDb {
        PlayerDbId = playerDbId,
        IsLogin    = isLogin,
        IpAddress  = ipAddress,
        Timestamp  = DateTime.Now
    };

    string sql = @"INSERT INTO log_login (PlayerDbId, IsLogin, IpAddress, Timestamp)
                   VALUES (@PlayerDbId, @IsLogin, @IpAddress, @Timestamp)";

    LogTransaction.Instance.Push(sql, log);   // 비동기 큐로 push, 즉시 반환
}
```

호출자(게임 핸들러)는 **즉시 반환** — 실제 DB 쓰기는 배치 단위로 LogDB 스레드에서 일어남.

---

## 7. 정리 — 두 패턴 비교

| 항목 | DbTransaction (운영 DB) | LogTransaction (로그 DB) |
|---|---|---|
| ORM | EF Core | Dapper |
| 처리 단위 | 개별 잡 | 배치 (최대 500) |
| 일관성 | 강한 일관성 (Step 3 재검증) | 최선 노력 (실패 시 DLQ) |
| 유실 시 영향 | 게임 데이터 손실 (큼) | 분석 데이터 손실 (DLQ로 복구 가능) |
| 큐 자료구조 | `BlockingCollection<Action>` | `BlockingCollection<LogJob>` |
| 셧다운 방어 | dropped count | dropped count + dead-letter count |

---

## 8. 관련 문서

- [architecture.md](architecture.md) — JobSerializer / 스레드 분리
- [graceful-shutdown.md](graceful-shutdown.md) — 종료 시 두 큐의 flush 순서 + DLQ 집계
- [auth.md](auth.md) — AccountDB / SharedDB / GameDB 분리 설계
