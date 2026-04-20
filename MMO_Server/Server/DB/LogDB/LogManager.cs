using Dapper;
using MySqlConnector;
using Server.Data;
using System;
using System.Collections.Concurrent;
using System.Data;
using System.Net;

namespace Server.DB.LogDB
{
    public class LogJob
    {
        public string Sql { get; set; }
        public object Data { get; set; }
    }

    public class LogTransaction
    {
        public static LogTransaction Instance { get; } = new LogTransaction();
        private readonly BlockingCollection<LogJob> _logQueue = new();

        private readonly string _connectionString = ConfigManager.Config.connectionString;

        public void Push(string sql, object data)
        {
            if (_logQueue.IsAddingCompleted) return;

            // 택배 상자에 담아서 큐에 휙 던짐
            _logQueue.Add(new LogJob { Sql = sql, Data = data });
        }

        public void FlushBlocking()
        {
            foreach (LogJob job in _logQueue.GetConsumingEnumerable())
            {
                try
                {
                    using (IDbConnection db = new MySqlConnection(_connectionString))
                    {
                        db.Execute(job.Sql, job.Data);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"LogDB Error: {e.Message}");
                }
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

