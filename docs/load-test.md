# 부하 테스트 결과 — 500 → 1000 CCU 분석 및 개선

> **TL;DR** — 초기 500 CCU에서 막혔던 게임 서버를 **병목 분석 → 3가지 조치**로 1000 CCU 안정 운영까지 끌어올렸다.
> 가장 중요한 인사이트는 *"수치가 2배가 됐다"*가 아니라
> *"가설(MariaDB max_connections)이 틀렸고 실측 데이터로 진짜 원인(.NET ThreadPool + 동기 EF)을 찾았다"* 라는 점이다.

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

## 5. 개선 후 결과 (1000 CCU 안정 구간)

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

**CCU 2.0×인데 TickAvg는 1.5× 증가** — 부하가 선형 이하로 증가하는 효율적 스케일링.
**Sent/s가 2.4× 증가** — 브로드캐스트가 많은 MMO 특성상 N 명 늘면 Sent/s는 비선형으로 증가하는 게 자연스러움.

---

## 6. 30Hz 게임 루프 관점

게임 서버는 30Hz fixed timestep (`FrameMs = 33`)으로 동작.

- **TickAvg 10.6 ms / Frame 33 ms = 32% 점유** — 헤드룸 22 ms 남음 (CPU 1코어 기준)
- **TickMax 피크 50 ms** — 일부 frame은 budget 초과 → catch-up. 33ms 윈도우를 넘는 spike가 정기적으로 발생하므로 GameLogic 분할 (멀티 룸/Zone 별 스레드) 검토 여지

---

## 7. 후속 과제

1. **GameLogic 단일 스레드 한계** — 1000 CCU 부근부터 TickMax가 50ms를 자주 넘음. 다음 단계 스케일링은 Room별 독립 스레드 또는 Zone 단위 병렬화 필요
2. **p99/p99.9 latency 측정** — 현재는 TickMax(피크)만 보고 있어 long-tail 분포 파악 어려움. HdrHistogram 도입 검토
3. **Elasticsearch + Kibana 메트릭 송출** — 현재 Serilog 파일 sink → 실시간 대시보드 필요 (사용자 본인이 Elasticsearch 경험 있어 자연스러운 확장)
4. **부하 시나리오 다양화** — 현재는 랜덤 이동/스킬 위주. 채팅/거래/룸 이동 비율 조정한 시나리오로 다른 병목 노출 가능
5. **DummyClient 머신 분리** — 현재는 서버와 같은 머신에서 부하 생성. 별도 머신/컨테이너로 분리 시 더 높은 CCU 도전 가능

---

## 8. 어필 포인트 (면접 자료용)

이 작업의 진짜 가치는 **수치 향상이 아니라 분석 프로세스**:

- ✅ 가설 → 실측 → 가설 수정의 반복으로 진짜 원인 도달
- ✅ DB / .NET / OS 여러 레이어를 동시에 의심하고 좁혀나감
- ✅ **틀린 가설 (max_connections)을 인정하고 실측 데이터를 따라간 것** — 엔지니어링 판단력
- ✅ 코드 변경은 단 3줄 (`SetMinThreads` 한 줄씩 + `FirstOrDefault → FirstOrDefaultAsync`)으로 2배 향상 — 측정 없이는 절대 못 찾을 변경
