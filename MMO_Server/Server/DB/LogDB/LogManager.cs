using Dapper;
using MySqlConnector;
using Server.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
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

        private readonly string _connectionString = ConfigManager.Config.connectionString;

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
                // 배치 전체 실패: 배치 크기만큼 드롭으로 기록 (포스트모템용)
                Interlocked.Add(ref _droppedLogCount, batch.Count);
                Console.WriteLine($"LogDB Batch Error ({batch.Count} rows): {e.Message}");
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
                INSERT INTO Log_Login (PlayerDbId, IsLogin, IpAddress, Timestamp) 
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

            string sql = "INSERT INTO Log_Reward (PlayerDbId, ItemId, Count, Reason, Timestamp)" +
                " VALUES (@PlayerDbId, @ItemId, @Count, @Reason, @Timestamp)";

            LogTransaction.Instance.Push(sql, log);
        }
    }

}

