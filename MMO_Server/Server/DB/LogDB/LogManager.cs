using Dapper;
using MySqlConnector;
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
    public static class LogHelper
    {
        public static void LogLogin(int playerDbId, bool isLogin, string ipAddress)
        {
            var log = new Log_LoginDb
            {
                PlayerDbId = playerDbId,
                IsLogin = isLogin,
                IpAddress = ipAddress,
                Timestamp = DateTime.Now 
            };

            string sql = @"
                INSERT INTO log_login (PlayerDbId, IsLogin, IpAddress, Timestamp)
                VALUES (@PlayerDbId, @IsLogin, @IpAddress, @Timestamp)";

            // 3. 람다 없이 아주 깔끔하게 큐에 밀어 넣기! (비동기 처리)
            LogTransaction.Instance.Push(sql, log);
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
        }
    }

}

