using Dapper;
using MySqlConnector;
using Serilog;
using Server.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Server.DB.LogDB
{
    public class LogJob
    {
        public string Sql { get; set; }
        public object Data { get; set; }
    }

    public class LogTransaction
    {
        // 한 배치에 최대 몇 건까지 모아서 커밋할지 (큐가 더 많으면 여러 배치로 나뉨)
        private const int MAX_BATCH_SIZE = 500;

        // DLQ(Dead Letter Queue) 파일 저장 위치 - DB 플러시 실패 건을 여기에 jsonl로 덤프
        private static readonly string DeadLetterDir =
            Path.Combine(AppContext.BaseDirectory, "logs", "deadletter");

        public static LogTransaction Instance { get; } = new LogTransaction();
        private readonly BlockingCollection<LogJob> _logQueue = new();

        private readonly string _connectionString = ConfigManager.Config.logConnectionString;

        private long _droppedLogCount;
        public long DroppedLogCount => Interlocked.Read(ref _droppedLogCount);

        private long _deadLetterCount;
        public long DeadLetterCount => Interlocked.Read(ref _deadLetterCount);

        public static string DeadLetterDirectory => DeadLetterDir;

        // 셧다운 race 방어: CompleteAdding 이후 Add가 호출되어도 예외 대신 드롭
        public bool Push(string sql, object data)
        {
            if (_logQueue.IsAddingCompleted)
            {
                Interlocked.Increment(ref _droppedLogCount);
                return false;
            }

            try
            {
                // 택배 상자에 담아서 큐에 휙 던짐
                _logQueue.Add(new LogJob { Sql = sql, Data = data });
                return true;
            }
            catch (InvalidOperationException)
            {
                // TOCTOU: IsAddingCompleted 체크와 Add 사이에 StopAcceptingJobs가 끼어든 경우
                Interlocked.Increment(ref _droppedLogCount);
                return false;
            }
        }

        public void FlushBlocking()
        {
            var batch = new List<LogJob>(MAX_BATCH_SIZE);

            // GetConsumingEnumerable: 큐가 비면 대기, CompleteAdding 후 비면 자동 탈출
            foreach (LogJob first in _logQueue.GetConsumingEnumerable())
            {
                batch.Add(first);

                // 이미 쌓여있는 잡을 non-blocking으로 최대한 긁어와 한 배치로 묶음
                while (batch.Count < MAX_BATCH_SIZE && _logQueue.TryTake(out LogJob more))
                    batch.Add(more);

                FlushBatch(batch);
                batch.Clear();
            }
        }

        private void FlushBatch(List<LogJob> batch)
        {
            if (batch.Count == 0) return;

            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        try
                        {
                            // 같은 SQL끼리 묶어서 Dapper에 IEnumerable로 넘기면
                            // 커맨드 재사용(prepared) + 단일 커넥션/트랜잭션으로 처리됨
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
                    }
                }
            }
            catch (Exception e)
            {
                // 배치 전체 실패: DLQ 파일로 덤프 → 수동 복구 가능하게
                // (DLQ 쓰기까지 실패해야 완전 유실)
                bool dlqSaved = WriteToDeadLetter(batch, e);

                if (dlqSaved)
                {
                    Interlocked.Add(ref _deadLetterCount, batch.Count);
                    Console.WriteLine(
                        $"LogDB Batch Error ({batch.Count} rows → DLQ): {e.Message}");
                }
                else
                {
                    Interlocked.Add(ref _droppedLogCount, batch.Count);
                    Console.WriteLine(
                        $"LogDB Batch Error ({batch.Count} rows LOST - DLQ write failed): {e.Message}");
                }
            }
        }

        // 실패한 배치를 JSON Lines 형식으로 logs/deadletter/logdb-YYYYMMDD.jsonl 에 append
        // 각 라인 구조: { "ts", "sql", "data": <runtime type 기반 직렬화>, "error" }
        private static bool WriteToDeadLetter(List<LogJob> batch, Exception failure)
        {
            try
            {
                Directory.CreateDirectory(DeadLetterDir);
                string path = Path.Combine(
                    DeadLetterDir, $"logdb-{DateTime.Now:yyyyMMdd}.jsonl");

                string isoTs = DateTime.UtcNow.ToString("O");
                string errJson = JsonSerializer.Serialize(failure.Message);

                // append: true → 크래시 루프에서도 누적, 한 줄 한 Job
                using (var writer = new StreamWriter(path, append: true))
                {
                    foreach (var job in batch)
                    {
                        // data가 object로 선언돼 있어서 런타임 타입을 명시해야
                        // Log_LoginDb / Log_RewardDb 같은 POCO 프로퍼티가 빠짐없이 직렬화됨
                        string dataJson = job.Data == null
                            ? "null"
                            : JsonSerializer.Serialize(job.Data, job.Data.GetType());
                        string sqlJson = JsonSerializer.Serialize(job.Sql ?? "");

                        writer.WriteLine(
                            $"{{\"ts\":\"{isoTs}\",\"sql\":{sqlJson},\"data\":{dataJson},\"error\":{errJson}}}");
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                // 디스크가 꽉 찼거나 권한 문제 → 진짜 유실. Console에라도 남김
                Console.WriteLine($"[CRITICAL] Dead-letter write failed: {e.Message}");
                return false;
            }
        }

        public void StopAcceptingJobs() => _logQueue.CompleteAdding();
    }
    // 게임 플레이 로그는 두 곳으로 나간다.
    //
    //  - MariaDB LogDb  : 감사/정산 원본. 정합성 기준은 항상 여기다.
    //                     DLQ 까지 붙여 유실을 최소화한다.
    //  - Serilog(jsonl) → Filebeat → ES : 분석/조회용 복제본.
    //                     유실돼도 원본이 DB 에 남으므로 치명적이지 않다.
    //
    // 왜 굳이 둘 다 쓰나: DB 는 "이 유저에게 무엇을 줬는가"를 정확히 보관하는 데 강하고,
    // ES 는 "지난 한 시간 동안 아이템 3번이 몇 번 나왔나" 같은 집계/탐색에 강하다.
    // 정산 근거를 ES 에 두면 안 되고, 분석할 때마다 운영 DB 를 긁어도 안 된다.
    //
    // 순서는 DB 먼저다. 로그 싱크가 느리거나 막혀도 감사 원본이 밀리지 않게 한다.
    public static class LogHelper
    {
        const string EventTypePlay = "Play";

        // 로그인 시점에는 아직 캐릭터를 고르기 전이라 계정 단위로만 남길 수 있다.
        // (DB 컬럼명이 PlayerDbId 인데 실제로 들어가는 값은 AccountDbId 다.
        //  스키마를 바꾸려면 마이그레이션이 필요해 일단 두고, ES 쪽은 올바른 이름으로 남긴다.)
        public static void LogLogin(int accountDbId, bool isLogin, string ipAddress)
        {
            var log = new Log_LoginDb
            {
                PlayerDbId = accountDbId,
                IsLogin = isLogin,
                IpAddress = ipAddress,
                Timestamp = DateTime.Now
            };

            string sql = @"
                INSERT INTO log_login (PlayerDbId, IsLogin, IpAddress, Timestamp)
                VALUES (@PlayerDbId, @IsLogin, @IpAddress, @Timestamp)";

            // 3. 람다 없이 아주 깔끔하게 큐에 밀어 넣기! (비동기 처리)
            LogTransaction.Instance.Push(sql, log);

            Log.ForContext("EventType", EventTypePlay)
               .ForContext("PlayKind", "Login")
               .Information("Login. AccountDbId={AccountDbId} Success={Success} Ip={Ip}",
                   accountDbId, isLogin, ipAddress);
        }

        public static void LogReward(int playerDbId, int itemId, int count, string reason)
        {
            var log = new Log_RewardDb
            {
                PlayerDbId = playerDbId,
                ItemId = itemId,
                Count = count,
                Reason = reason,
                Timestamp = DateTime.Now
            };

            string sql = "INSERT INTO log_reward (PlayerDbId, ItemId, Count, Reason, Timestamp)" +
                " VALUES (@PlayerDbId, @ItemId, @Count, @Reason, @Timestamp)";

            LogTransaction.Instance.Push(sql, log);

            Log.ForContext("EventType", EventTypePlay)
               .ForContext("PlayKind", "ItemGain")
               .Information("Item gained. PlayerDbId={PlayerDbId} ItemId={ItemId} Count={Count} Reason={Reason}",
                   playerDbId, itemId, count, reason);
        }

        // 확률 추첨 결과. 당첨/미당첨을 모두 남기는 게 핵심이다.
        // 당첨만 남기면 "실제 확률이 설정값과 맞는가"를 검증할 수 없다 —
        // 분모(시행 횟수)를 모르기 때문. 가챠/드랍 확률 논란은 이 로그로만 답할 수 있다.
        //
        // DB 에는 넣지 않는다. 시행 횟수가 당첨보다 훨씬 많고, 정산 근거는
        // 실제 지급 기록(log_reward)이지 추첨 시행 자체가 아니기 때문.
        public static void LogItemRoll(int playerDbId, int sourceId, int roll,
                                       int? itemId, int probability, string reason)
        {
            Log.ForContext("EventType", EventTypePlay)
               .ForContext("PlayKind", "ItemRoll")
               .ForContext("Hit", itemId.HasValue)
               .Information(
                   "Item roll. PlayerDbId={PlayerDbId} SourceId={SourceId} Roll={Roll} ItemId={RolledItemId} Probability={Probability} Reason={Reason}",
                   playerDbId, sourceId, roll, itemId ?? 0, probability, reason);
        }
    }

}

