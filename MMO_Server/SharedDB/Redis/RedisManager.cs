using System;
using StackExchange.Redis;

namespace SharedDB.Redis
{
    public class RedisManager
    {
        public static RedisManager Instance { get; } = new RedisManager();

        private ConnectionMultiplexer _redis;
        private IDatabase _database;

        public void Init(string connectionString)
        {
            _redis = ConnectionMultiplexer.Connect(connectionString);
            _database = _redis.GetDatabase();
            Console.WriteLine("Redis 연결 성공!");
        }

        public IDatabase GetDatabase() { return _database; }  

        public void Close()
        {
            if(_redis != null)
            {
                _redis.Close();
                _redis.Dispose();
                Console.WriteLine("Redis 연결 종료.");
            }
        }

    }
}
