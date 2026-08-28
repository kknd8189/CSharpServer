# 부하 테스트 — 접속 수용 한계와 게임 로직 처리 한계

> **TL;DR** — 서버의 한계는 하나가 아니라 **성격이 다른 두 개의 지표**다.
>
> | 지표 | 무엇이 막히나 | 결과 |
> |---|---|---|
> | **접속 수용** | 신규 접속이 거부됨 | 500 → **1100+** (ThreadPool starvation 해소) |
> | **게임 로직 처리** | 30Hz 틱 예산 초과 | **700 CCU** (틱 p99 33.9ms, 예산 초과 0.9%) |
>
> 교훈이 두 번 나왔다.
>
> 1. **가설(MariaDB `max_connections`)이 틀렸고, 실측으로 진짜 원인(.NET ThreadPool + 동기 EF)에 도달했다** — 2~5장
> 2. **틱 최댓값으로 한계를 판정하면 안 된다.** 히스토그램 p99 로 바꾸자 판정이 달라졌고,
>    그 과정에서 부하 클라이언트의 결함과 시야 컬링 버그를 찾아 **p99 를 61% 개선**했다 — 6~9장

---

## 1. 테스트 환경

| 항목 | 값 |
|---|---|
| OS | Windows 11 |
| 런타임 | .NET 10 Release |
| 컨테이너 | Docker Compose (MariaDB 10.6, Redis 7, AccountServer, GameServer) |
| DB | MariaDB `max_connections=1000` (튜닝 후) |
| 클라이언트 | DummyClient (동일 머신, 50명/0.5초 chunk ramp-up) |
| 메트릭 수집 | Serilog 5초 주기 (파일 sink) |

수집 항목: `PacketsRecv/s`, `PacketsSent/s`, `TickAvg(μs)`, `TickMax(μs)`, `Players`

---

## 2. 초기 결과 (개선 전)

500 CCU 까지는 안정적이었으나 **500 부근에서 신규 접속이 차단**됨.

| Stage | Build | Players | TickAvg | p95 TickMax | Recv/s | Sent/s |
|---|---|---:|---:|---:|---:|---:|
| Stage 1 | Debug   | 200 | 4.39 ms  | 30.75 ms | 693   | 2,608 |
| Stage 1 | Release | 200 | **2.33 ms**  | 10.41 ms | 680   | 2,226 |
| Stage 2 | Debug   | 400 | 7.57 ms  | 39.19 ms | 1,392 | 5,158 |
| Stage 2 | Release | 400 | **4.78 ms**  | 19.53 ms | 1,395 | 4,902 |
| Stage 3 | Debug   | 500 | 11.03 ms | 60.44 ms | 1,743 | 6,238 |
| Stage 3 | Release | 500 | **7.08 ms**  | 34.86 ms | 1,746 | 6,130 |

**Release vs Debug**: JIT 최적화 효과로 평균 틱 **35~47% 감소**, p95 TickMax 42~66% 감소.

---

## 3. 가설과 실측 — 병목 분석

### 3.1 첫 가설: MariaDB `max_connections=151`

부하 클라이언트 1명당 사용하는 DB 연결을 추정해보면:
- AccountServer 측: AccountDB + SharedDB 각 1개
- GameServer 측: GameDB 1개 (Login, EnterGame 핸들러)
- 동시 ramp-up 시 순간 동시 연결 ~3-5개/명

500명 × 3-5 = 1500~2500 conn → MariaDB 기본 한계 151 명백히 초과.
당시엔 이 가설이 가장 강력해 보였다.

### 3.2 실측

`max_connections=1000`으로 올린 뒤 **1000 CCU 부하 재현 후** 확인:

```sql
mysql> SHOW STATUS LIKE 'Max_used_connections';
+----------------------+-------+
| Variable_name        | Value |
+----------------------+-------+
| Max_used_connections | 146   |   -- 1000 한계의 14.6%
+----------------------+-------+
```

**`max_connections`은 진짜 병목이 아니었다.**
EF Core가 DbContext 단위로 connection을 짧게 점유하고 풀로 반환하기 때문에 동시 1000세션이라도 실제 동시 연결은 146개로 수렴.

### 3.3 진짜 원인

코드 베이스를 다시 훑어 두 가지 문제를 발견:

**(1) ThreadPool 최소 워커 미설정** — `Program.cs` / `Startup.cs` 모두 `ThreadPool.SetMinThreads()` 호출 없음. 기본값은 CPU 코어 수(예: 8). 동시 spawn 시 워커 풀이 천천히 (~500ms / 1개) 증가하면서 패킷 처리 콜백이 지연 → **신규 SYN 처리 누락**.

**(2) `HandleLoginAsync`의 EF Core 동기 호출** — `ClientSession_PreGame.cs`에서 `.FirstOrDefault()`가 동기로 사용됨. `await ...Async()` 없이 IOCP 워커가 DB I/O 대기 동안 블로킹 → ThreadPool starvation 가속화.

```csharp
// Before
AccountDb findAccount = db.Accounts
    .Include(a => a.Players)
    .Where(a => a.AccountDbId == loginPacket.AccountID).FirstOrDefault();   // 동기 ❌

// After
AccountDb findAccount = await db.Accounts
    .Include(a => a.Players)
    .Where(a => a.AccountDbId == loginPacket.AccountID).FirstOrDefaultAsync();  // 비동기 ✅
```

---

## 4. 조치 사항

| # | 파일 | 변경 |
|---|---|---|
| 1 | `CICD/docker-compose.yml` | MariaDB `--max-connections=1000` (안전 마진) |
| 2 | `MMO_Server/Server/Program.cs` | `ThreadPool.SetMinThreads(200, 200)` |
| 3 | `MMO_Server/AccountServer/Program.cs` | `ThreadPool.SetMinThreads(200, 200)` |
| 4 | `MMO_Server/Server/Session/ClientSession_PreGame.cs` | `FirstOrDefault()` → `await FirstOrDefaultAsync()` |

핵심 변화는 #2~#4 (.NET 측). #1은 안전 마진일 뿐 실측상 결정타가 아님.

---

## 5. 개선 후 결과 — 접속 수용 한계 해소

> ⚠️ **아래 틱·처리량 수치는 이후 무효로 판명됐다.** 당시 DummyClient 가 이동의 절반을
> 맵에 없는 축(Y)으로 보내 서버가 전부 폐기하고 있었다(6.2 참고). 즉 **절반짜리 부하**였다.
> 이 장에서 유효한 결론은 **"접속이 더 이상 거부되지 않는다"** 뿐이다.
> 틱 관점의 한계는 6장 이후에서 다시 측정했다.

```
[04:25:51] Players=1000  TickAvg=12.4ms  TickMax=44.3ms  Recv/s=3,134  Sent/s=19,084
[04:25:56] Players=1000  TickAvg=11.5ms  TickMax=47.4ms  Recv/s=3,463  Sent/s=17,837
[04:26:01] Players=1000  TickAvg=10.3ms  TickMax=54.3ms  Recv/s=3,454  Sent/s=15,194
[04:26:06] Players=1000  TickAvg=9.93ms  TickMax=35.4ms  Recv/s=3,470  Sent/s=14,716
[04:26:11] Players=1000  TickAvg=10.3ms  TickMax=54.3ms  Recv/s=3,453  Sent/s=14,550
[04:26:16] Players=1000  TickAvg=9.89ms  TickMax=33.6ms  Recv/s=3,455  Sent/s=14,115
[04:26:21] Players=1000  TickAvg=10.5ms  TickMax=45.3ms  Recv/s=3,452  Sent/s=14,833
[04:26:26] Players=1000  TickAvg=9.96ms  TickMax=40.3ms  Recv/s=3,466  Sent/s=13,807
[04:26:31] Players=1000  TickAvg=9.97ms  TickMax=37.8ms  Recv/s=3,457  Sent/s=13,804
[04:26:36] Players=1000  TickAvg=10.8ms  TickMax=50.9ms  Recv/s=3,460  Sent/s=14,625
```

### Before / After

| 항목 | 500 CCU (Before) | **1000 CCU (After)** | 변화 |
|---|---:|---:|---:|
| Players | 500 (캡) | **1000 (안정)** | **2.0×** |
| TickAvg | 7.08 ms | 10.6 ms | 1.5× ↑ |
| p95 TickMax | 34.86 ms | ~50 ms | 1.4× ↑ |
| Recv/s | 1,746 | 3,460 | **2.0×** |
| Sent/s | 6,130 | 14,500 | **2.4×** |
| DB conn 피크 | (미측정) | 146 / 1000 | — |

당시엔 "CCU 2.0× 인데 TickAvg 1.5× 증가 = 선형 이하 스케일링" 으로 읽었다.
**지금 보면 이 해석은 성립하지 않는다.** 부하 클라이언트가 이동 패킷의 절반을 무효한 축으로
보내고 있어 서버가 브로드캐스트를 하지 않았고, 그만큼 일을 덜 하고 있었다.
고친 뒤 같은 CCU 에서 송신이 최대 3.9× 늘었다(6.2).

---

## 6. 게임 로직 처리 한계 — 판정 기준을 바꾸다

접속이 뚫린 뒤에도 "그래서 몇 명까지 **제대로 돌아가나**" 는 답하지 못하고 있었다.
5장까지의 근거는 `TickMax`(5초 윈도우의 최댓값) 하나였는데, 이 지표로는 판정이 흔들린다.

### 6.1 세 가지 기준이 서로 다른 답을 냈다

| 판정 기준 | 답 | 문제 |
|---|---|---|
| 틱 **최댓값** > 33ms | 400 CCU | **과도하게 보수적** — 5초에 149틱 중 1틱만 튀어도 걸린다 |
| **지속 Hz** < 29 | 700 CCU | **과도하게 관대** — 그 구간에서 이미 틱의 19% 가 예산 초과인데도 Hz 는 멀쩡해 보인다 |
| **p99 · 예산 초과율** | **500 CCU** | 분포를 보므로 둘 다 아니다 |

Hz 가 관대한 이유가 흥미롭다. 30Hz 루프는 예산을 넘기면 sleep 을 생략하고 따라잡는다(catch-up).
그래서 **틱의 19% 가 예산을 넘겨도 실측 Hz 는 27.9 로 유지**된다. 겉보기엔 멀쩡한데
다섯 틱 중 하나가 프레임을 밀고 있는 상태다. 히스토그램이 없으면 볼 수 없다.

→ 이 시점에 메트릭을 Prometheus 히스토그램으로 옮겼다. [monitoring.md](monitoring.md) 참고.

```promql
# 예산을 넘긴 틱의 비율 — 이 값이 유의미해지는 CCU 가 실질적 수용 한계
1 - (
  sum(rate(game_tick_duration_seconds_bucket{le="0.0333"}[1m]))
  / sum(rate(game_tick_duration_seconds_count[1m]))
)
```

### 6.2 측정 도구 자체가 고장나 있었다

기준을 바꾸고 재보니 숫자가 이상해서 파고들었더니 **DummyClient 에 버그가 두 개** 있었다.

| 버그 | 증상 |
|---|---|
| **이동 축이 틀렸다** — `Up/Down` 으로 `PosY` 를 움직였는데 이 맵은 단일 Y 평면(`MaxY = MinY`) | 서버 `CanGo` 가 **100% 거부**. 방향 4개 중 2개가 y 였으니 **이동 패킷의 절반이 폐기** |
| **서버 보정을 무시** — `S_MoveHandler` 가 빈 껍데기 | 거부될 때마다 클라 좌표만 앞서 나가 격차 누적 → 정상 더미 292 세션이 텔레포트 어뷰저로 킥 |

고친 뒤 **같은 CCU 에서 송신 처리량이 최대 3.9× 증가**했다.
즉 그 이전의 모든 측정은 절반도 안 되는 부하로 잰 것이었다.

---

## 7. 병목 특정 — 브로드캐스트 팬아웃

정상화된 부하로 재보니 병목이 선명해졌다.

```
900 CCU:  수신 3,062/s  →  송신 42,606/s     (14.6배)
          명당 3.4 패킷      명당 47 패킷
```

수신은 CCU 에 선형인데 송신만 가파르게 증가한다.
비용이 접속자 **수**가 아니라 **시야 안 접속자 쌍**에 비례한다는 신호 — 브로드캐스트 팬아웃이다.

파고들었더니 성능 튜닝 이전에 **버그**였다.

### 7.1 시야 컬링 축이 틀렸다

```csharp
int dx = player.CellPos.x - cellPos.x;
int dy = player.CellPos.y - cellPos.y;   // 단일 Y 평면 → 항상 0, 아무것도 못 거른다
if (Math.Abs(dx) > VisionCells) continue;
if (Math.Abs(dy) > VisionCells) continue;
// z 는 아예 검사하지 않는다
```

존이 `ZoneCells`(10) 단위라 인접 존을 훑으면 z 로 최대 20 셀이 포함된다.

```
의도한 시야:  11 × 11 = 121 셀
실제 전송:    11 × 20 ≈ 220 셀      → 약 1.8배 과다 전송
```

같은 버그가 `Broadcast` 와 `VisionCube` 양쪽에 복사돼 있었다. `GameRoom.IsInVision` 으로 통일.

### 7.2 핫패스 할당

`Broadcast` 는 이동 1건마다 도는 최핫패스인데 호출마다
`GetAdjacentZones` 의 `HashSet`+`List`, `SelectMany` 의 이터레이터/클로저를 할당했다.
존 인덱스를 직접 순회하도록 펼쳐 **할당 0** 으로 만들었다.

### 7.3 A\* — 확장 여지의 비용

```csharp
int[] _deltaY = { 0, 0, 1, -1, 0, 0 };   // 6방향 중 2개가 y → 매번 CanGo 에서 탈락
```

이건 버그가 아니라 **3D 맵을 대비해 의도적으로 넣어둔 확장 여지**였다.
다만 현재 데이터가 단일 평면이라 **노드당 확장의 1/3 이 매번 버려지고 있었고**,
`FindPath` 는 추적 중인 몬스터가 200ms 마다 호출하는 핫패스다.

지우는 대신 `SizeY > 1` 일 때만 6방향을 쓰도록 조건을 달았다.
맵 데이터에 층이 생기면 코드 수정 없이 자동으로 6방향으로 돌아간다 —
**확장 여지는 남기고 현재 비용만 제거**.

> 실제로 다층 맵을 넣을 때는 `±1 y 이웃` 자체를 다시 봐야 한다.
> "위로 한 칸 날아오른다" 는 대부분의 게임에서 유효한 이동이 아니다.
> 층 사이는 계단·사다리 같은 **명시적 연결 노드**로만 이어지는 게 맞다.
> 시야도 y 범위 검사가 아니라 "같은 층인가" 검사가 맞다.

한편 몬스터의 축 정렬 판정(`dir.x == 0 || dir.y == 0`)은 **진짜 버그**였다.
`dir.y` 가 항상 0 이라 조건이 무조건 참이 되어 정렬 검사가 무력화돼 있었다.

---

## 8. 개선 결과

동일 환경(다른 앱 종료), 200 → 1100 CCU 램프업, 75초 간격.

| CCU | p99 (before) | **p99 (after)** | 개선 | 예산 초과율 (after) |
|---:|---:|---:|---:|---:|
| 500 | 33.0 ms | **20.4 ms** | −38% | 0.00% |
| 600 | 47.9 ms | **24.3 ms** | −49% | 0.10% |
| **700** | 86.4 ms | **33.9 ms** | **−61%** | **0.90%** |
| 800 | 176.1 ms | **47.6 ms** | −73% | 6.58% |
| 900 | 288.2 ms | **72.0 ms** | **−75%** | 18.58% |
| 1000 | (붕괴) | **98.0 ms** | — | 40.27% |
| 1100 | — | **225.9 ms** | — | 62.50% |

**한계: 700 CCU** (p99 33.9ms 로 예산 33ms 를 아슬아슬하게 넘고, 예산 초과 0.90%).
600 CCU 는 완전 안전 구간(p99 24.3ms, 초과 0.10%).

### 연쇄 붕괴가 사라졌다

가장 중요한 변화는 수치가 아니다.

```
before:  999 CCU 에서 80 으로 급락    PingTimeout 715, Kicked 205
after:   1100 CCU 까지 유지          PingTimeout 0,   Kicked 0   (전원 정상 종료)
```

이전에는 게임 스레드가 30초 넘게 막혀 세션 715개가 강제 종료됐다.
지금은 **틱이 예산을 넘어도 연결은 유지된다** — 성능이 떨어져도 우아하게 degrade 하지, 무너지지 않는다.

---

## 9. 측정 환경이 결과를 2~3배 흔든다

같은 코드를 다른 환경에서 재면 이렇게 갈린다.

| CCU | 최적화 전<br>(조용) | 최적화 전<br>(3코어 점유 중) | 최적화 후<br>(3코어 점유 중) | 최적화 후<br>(조용) |
|---:|---:|---:|---:|---:|
| 500 | 33.0 ms | 53.5 ms | 36.4 ms | **20.4 ms** |
| 700 | 86.4 ms | 235.5 ms | 92.2 ms | **33.9 ms** |
| 900 | 288.2 ms | 997.4 ms | 246.2 ms | **72.0 ms** |

**최적화 전 조용한 환경이 최적화 후 시끄러운 환경보다 낫다.**
즉 환경 노이즈가 최적화 효과보다 컸다. 다른 앱(3코어 점유)을 끄기 전에는
개선을 측정할 수조차 없었다.

> **부하 테스트를 인용할 때 반드시 함께 말해야 하는 조건**
> - 클라이언트와 서버가 **같은 PC**에서 구동 (서버 단독이면 한계치가 더 높다 → **하한선**으로 읽어야 한다)
> - 더미가 실제 유저보다 공격적 (200~500ms 이동, 1~3초 스킬을 쉬지 않고)
> - Docker Desktop(WSL2) 오버헤드
> - **단일 존** 기준. 존이 분산되면 결과가 달라진다
> - 게임 로직이 **단일 스레드**라 12코어 중 1개만 사용

---

## 10. 후속 과제

- [x] ~~p99/p99.9 latency 측정~~ → Prometheus 히스토그램 도입 완료
- [x] ~~Elasticsearch + Kibana 메트릭 송출~~ → 메트릭은 Prometheus/Grafana, 로그는 ES/Kibana 로 분리
- [ ] **GameLogic 단일 스레드 한계** — 12코어 중 1개만 쓴다. 그 1코어가 700 CCU 부근에서 포화.
      틱을 페이즈로 분리한 뒤 발송 → 시뮬레이션 순으로 병렬화 예정
- [ ] **A\* 잔여 최적화** — 호출마다 자료구조 5개 할당, 경로 실패 시 `parent` 전체 O(N) 선형 탐색
- [ ] **DummyClient 머신 분리** — 서버 단독 환경에서의 진짜 한계 측정
- [ ] **부하 시나리오 다양화** — 채팅/거래/존 이동 비율을 조정해 다른 병목 노출

---

## 11. 어필 포인트

수치 향상보다 **분석 프로세스**가 이 작업의 값어치다.

- ✅ **틀린 가설(`max_connections`)을 인정하고 실측을 따라간 것** — 엔지니어링 판단력
- ✅ **판정 기준 자체를 의심한 것** — 최댓값·처리량·p99 가 각각 다른 답(400/700/500)을 내는 걸 확인하고, 왜 갈리는지 설명한 뒤 근거 있는 기준을 골랐다
- ✅ **측정 도구를 먼저 검증한 것** — 부하 클라이언트가 절반짜리 부하를 만들고 있었다
- ✅ **병목을 수치로 특정하고(송신/수신 14.6배) 실제로 61% 개선한 것**
- ✅ **환경 노이즈가 개선 효과보다 크다는 걸 인지하고 통제한 것**
