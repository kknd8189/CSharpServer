# GracefulShutdown — 종료 시퀀스 / DLQ 집계

> 게임 서버를 갑자기 죽이면 **메모리에 있던 유저 데이터가 영영 사라진다**.
> Ctrl+C 한 번 / docker stop 한 번에 안전하게 모든 인메모리 변경을 DB로 흘려보내고
> 잔여 잡은 DLQ로 덤프한 뒤 깔끔하게 종료하는 절차.

---

## 1. 종료 트리거

```csharp
// Server/Program.cs
Console.CancelKeyPress += (sender, e) =>
{
    Log.Information("서버 종료 시그널 감지! Graceful Shutdown 시작...");

    e.Cancel = true;        // OS야, 프로세스 바로 죽이지 마! 내가 정리하고 끌게!
    _cts.Cancel();          // GameLogicTask while loop 탈출 신호
};
```

`SIGINT` (Ctrl+C) / `SIGTERM` (docker stop) 둘 다 `Console.CancelKeyPress`로 잡힌다.
`e.Cancel = true`로 OS의 즉시 종료를 막고, 직접 정리 후 main을 끝낸다.

---

## 2. 종료 시퀀스 — 6단계

```csharp
// Server/Program.cs : DoGracefulShutdown
static void DoGracefulShutdown()
{
    // ① 새 유저 차단
    _listener.Stop();

    // ② 접속 유저 정리
    foreach (var session in SessionManager.Instance.GetSessions())
        session.Disconnect();
    GameLogic.Instance.FlushAll();                  // 잔여 Room 잡 flush

    // ③ DB 스레드 종료 대기 (최대 5초)
    DbTransaction.Instance.StopAcceptingJobs();
    if (_dbThread.Join(TimeSpan.FromSeconds(5)))
        Log.Information("인메모리 데이터 DB 저장 완료.");
    else
        Log.Warning("DB 스레드 5초 내 종료 실패. 일부 DB 저장 손실 가능.");

    // ④ LogDB 스레드 종료 대기
    LogTransaction.Instance.StopAcceptingJobs();
    if (_logDbThread.Join(TimeSpan.FromSeconds(5)))
        Log.Information("큐에 있던 모든 로그 DB 저장 완료.");

    // ⑤ Redis 종료
    RedisManager.Instance.Close();

    // ⑥ 포스트모템 집계 + 로그 flush
    long dbDropped     = DbTransaction.Instance.DroppedJobCount;
    long logDropped    = LogTransaction.Instance.DroppedLogCount;
    long logDeadLetter = LogTransaction.Instance.DeadLetterCount;

    if (logDeadLetter > 0)
        Log.Warning("LogDB DLQ에 {Count}건 덤프됨. 수동 복구 경로: {Path}",
            logDeadLetter, LogTransaction.DeadLetterDirectory);

    if (dbDropped > 0 || logDropped > 0)
        Log.Warning("셧다운 중 유실된 Job: DB={DbDropped}건, Log={LogDropped}건",
            dbDropped, logDropped);
    else if (logDeadLetter == 0)
        Log.Information("모든 Job이 정상 플러시됨.");

    Log.CloseAndFlush();
}
```

---

## 3. 단계별 의도

### ① 새 유저 차단 (`_listener.Stop()`)

`Listener.Stop()` → `_listenSocket.Close()`. 이후 SYN은 OS가 RST로 응답.
"종료 중" 새 유저가 절반만 접속해 들어와 부분 상태로 남는 걸 차단.

### ② 접속 유저 정리

```csharp
foreach (var session in SessionManager.Instance.GetSessions())
    session.Disconnect();         // (TODO) S_ServerClose 패킷 송신 추가 예정
GameLogic.Instance.FlushAll();    // GameLogic + 모든 Room 큐 비우기
```

`FlushAll()`은 [architecture.md](architecture.md#4-gamelogic--사실상-roommanager)에 정의된 *모든 JobSerializer의 큐를 한 번씩 Flush* 헬퍼.

여기서 다 비워두지 않으면 ③번 단계에서 DB 스레드는 잡이 안 들어와 일찍 종료하고, 게임 룸 큐에 남은 "**저장해줘**" 잡이 영영 실행 안 됨.

### ③ DB 스레드 — Poison Pill 패턴

```csharp
DbTransaction.Instance.StopAcceptingJobs();   // _jobQueue.CompleteAdding()
_dbThread.Join(TimeSpan.FromSeconds(5));
```

- `StopAcceptingJobs()` = `BlockingCollection.CompleteAdding()` — 큐 닫음
- `FlushBlocking()` 안의 `GetConsumingEnumerable()`은 큐가 빌 때까지 계속 돌고, **빈 상태로 CompleteAdding 됐음을 감지하면 자동 종료**
- 5초 타임아웃 — 무한 대기로 종료를 못 끝내는 상황 방지

```csharp
// DbTransaction.cs
public void FlushBlocking()
{
    foreach (Action job in _jobQueue.GetConsumingEnumerable())   // ← 큐 비고 닫히면 탈출
        try { job.Invoke(); } catch (...) { }
}
```

### ④ LogDB 스레드

같은 패턴. `LogTransaction.FlushBlocking()`은 배치 단위로 묶어 마지막 commit까지 마침.

### ⑤ Redis 종료

```csharp
RedisManager.Instance.Close();      // ConnectionMultiplexer.Close() + Dispose()
```

Redis 토큰들은 TTL 300s라 어차피 자동 만료되지만, multiplexer를 명시적으로 닫아야 OS 소켓도 깨끗하게 정리.

### ⑥ 포스트모템 집계

```csharp
long dbDropped     = DbTransaction.Instance.DroppedJobCount;
long logDropped    = LogTransaction.Instance.DroppedLogCount;
long logDeadLetter = LogTransaction.Instance.DeadLetterCount;
```

이 세 숫자가 **정상 종료의 검증 지표**:

| 지표 | 정상값 | 비정상의 의미 |
|---|---|---|
| `DroppedJobCount` | 0 | 셧다운 race로 DB Job 누락 발생 |
| `DroppedLogCount` | 0 | LogDB Job 누락 + DLQ 쓰기도 실패 (진짜 유실) |
| `DeadLetterCount` | 0 | LogDB 배치 실패 → DLQ로 덤프됨 (복구 가능) |

전부 0이면 → `"모든 Job이 정상 플러시됨."` 한 줄로 정상 종료 확인 가능.

---

## 4. 드롭 카운터 — TOCTOU 방어

`BlockingCollection.Add()`는 `CompleteAdding()` 이후 호출되면 `InvalidOperationException`을 던진다.
체크 → Add 사이에 다른 스레드가 `CompleteAdding`을 호출하면 race condition.

```csharp
// DbTransaction.cs
public bool PushJob(Action job)
{
    if (_jobQueue.IsAddingCompleted)
    {
        Interlocked.Increment(ref _droppedJobCount);   // ⭐ 사전 차단
        return false;
    }

    try { _jobQueue.Add(job); return true; }
    catch (InvalidOperationException)
    {
        // ⭐ TOCTOU: 체크와 Add 사이에 StopAcceptingJobs가 끼어든 경우
        Interlocked.Increment(ref _droppedJobCount);
        return false;
    }
}
```

체크 + try/catch 2중 방어로 *예외도 안 나고 손실도 카운트됨*.

---

## 5. 시각화 — 정상 종료 vs 비정상

### 정상 종료 로그 예시

```
[15:02:33 INF] 서버 종료 시그널 감지! Graceful Shutdown 시작...
[15:02:33 INF] Listener 중지.
[15:02:33 INF] 접속 중인 유저 안전 종료.
[15:02:34 INF] 인메모리 데이터 DB 저장 완료.
[15:02:34 INF] 큐에 있던 모든 로그 DB 저장 완료.
[15:02:34 INF] Redis 연결 종료.
[15:02:34 INF] 모든 Job이 정상 플러시됨.
```

### 비정상 종료 로그 예시

```
[15:02:33 INF] 서버 종료 시그널 감지!
[15:02:38 WRN] DB 스레드 5초 내 종료 실패. 일부 DB 저장 손실 가능.
[15:02:38 WRN] LogDB DLQ에 142건 덤프됨. 수동 복구 경로: logs/deadletter/
[15:02:38 WRN] 셧다운 중 유실된 Job: DB=3건, Log=0건
```

→ 운영팀이 DLQ 파일 142건을 수동 INSERT로 복구하면 됨. DB 3건은 영영 유실 (포스트모템 대상).

---

## 6. 채용 어필 포인트

이 영역에서 면접관이 보는 시그널:

- ✅ **신호 처리 분리** — `e.Cancel=true`로 OS 즉시 종료 차단
- ✅ **Poison Pill 패턴** — sleep/wake 직접 관리 안 하고 `BlockingCollection`에 위임
- ✅ **타임아웃 가드** — 무한 대기로 종료 못 하는 상황 방어 (5초)
- ✅ **DLQ + 포스트모템 집계** — *어느 정도 유실됐는지 측정 가능*. 실제 운영에 가장 중요한 시그널
- ✅ **TOCTOU race 인지** — `BlockingCollection.Add`가 `CompleteAdding` 이후 예외 던질 수 있다는 걸 알고 try/catch까지 추가

---

## 7. 관련 문서

- [persistence.md](persistence.md) — DbTransaction / LogTransaction 큐 구조
- [architecture.md](architecture.md) — JobSerializer / `FlushAll()` 정의
