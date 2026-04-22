using System;
using System.Linq;
using System.Net;
using System.Threading;
using Serilog;
using Server.Data;
using Server.DB;
using Server.DB.LogDB;
using Server.Game;
using ServerCore;
using SharedDB;
using SharedDB.Redis;

namespace Server
{
    // 1. GameRoom 방식의 간단한 동기화 <- OK
    // 2. 더 넓은 영역 관리
    // 3. 심리스 MMO
    class Program
    {
        static Listener _listener = new Listener();

        public static string Name { get; set; }
        public static int Port { get; set; }
        public static string IpAddress { get; set; }

        private static CancellationTokenSource _cts = new CancellationTokenSource();

        private static Thread _dbThread;
        private static Thread _logDbThread;

        private static System.Timers.Timer _busyCheckTimer = new System.Timers.Timer();
        private static System.Timers.Timer _metricsLogTimer = new System.Timers.Timer();

        static void DoGracefulShutdown()
        {
            // 1.새로운 유저 차단
            _listener.Stop();
            Log.Information("Listener 중지.");

            //타이머 종료
            _busyCheckTimer.Stop();
            _metricsLogTimer.Stop();


            //2. 유저 안내 및 쫓아내기
            //TODO : 서버 종료 패킷 구현
            foreach (var session in SessionManager.Instance.GetSessions())
            {
                //session.Send(new S_ServerClose()); // "서버가 종료됩니다" 패킷
                session.Disconnect();
            }
            Log.Information("접속 중인 유저 안전 종료.");
            //마지막으로 GameLogic에 잡 전부 Flush 시켜서 남은 데이터들 DB에 저장하게 하기
            //(GameLogic 자기 큐 + 모든 Room 큐까지 전부 비움)
            GameLogic.Instance.FlushAll();

            // 3. 메모리 데이터를 DB에 저장 (가장 중요!)
            DbTransaction.Instance.StopAcceptingJobs();
            if (_dbThread.Join(TimeSpan.FromSeconds(5)))
                Log.Information("인메모리 데이터 DB 저장 완료.");
            else
                Log.Warning("DB 스레드 5초 내 종료 실패. 일부 DB 저장 손실 가능.");

            LogTransaction.Instance.StopAcceptingJobs();
            if (_logDbThread.Join(TimeSpan.FromSeconds(5)))
                Log.Information("큐에 있던 모든 로그 DB 저장 완료.");
            else
                Log.Warning("LogDB 스레드 5초 내 종료 실패. 일부 로그 손실 가능.");

            //4 Redis 연결 종료
            RedisManager.Instance.Close();
            Log.Information("Redis 연결 종료.");

            // 5 셧다운 중 드롭된 Job 집계 (포스트모템용, 정상 셧다운이면 0)
            long dbDropped = DbTransaction.Instance.DroppedJobCount;
            long logDropped = LogTransaction.Instance.DroppedLogCount;
            if (dbDropped > 0 || logDropped > 0)
                Log.Warning("셧다운 중 드롭된 Job: DB={DbDropped}건, Log={LogDropped}건",
                    dbDropped, logDropped);
            else
                Log.Information("모든 Job이 정상 플러시됨.");

            // 6  로그 플러시 및 메인 스레드 놔주기
            Log.CloseAndFlush(); // 비동기로 남은 로그들을 파일에 확실히 다 씀
        }


        static void GameLogicTask()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                GameLogic.Instance.Update();
                Thread.Sleep(0);
            }
        }

        static void DbTask()
        {
            DbTransaction.Instance.FlushBlocking();
        }

        static void LogDbTask()
        {
            LogTransaction.Instance.FlushBlocking();
        }

        //static void NetworkTask()
        //{
        //	while (true)
        //	{
        //		List<ClientSession> sessions = SessionManager.Instance.GetSessions();
        //		foreach (ClientSession session in sessions)
        //		{
        //			session.FlushSend();
        //		}
        //		Thread.Sleep(0);
        //	}
        //}

        static void StartServerInfoTask()
        {
            _busyCheckTimer.AutoReset = true;
            _busyCheckTimer.Elapsed += new System.Timers.ElapsedEventHandler((s, e) =>
            {
                using (SharedDbContext shared = new SharedDbContext())
                {
                    ServerDb serverDb = shared.Servers.Where(s => s.Name == Name).FirstOrDefault();
                    if (serverDb != null)
                    {
                        serverDb.IpAddress = IpAddress;
                        serverDb.Port = Port;
                        serverDb.BusyScore = SessionManager.Instance.GetBusyScore();
                        shared.SaveChangesEx();
                    }
                    else
                    {
                        serverDb = new ServerDb()
                        {
                            Name = Program.Name,
                            IpAddress = Program.IpAddress,
                            Port = Program.Port,
                            BusyScore = SessionManager.Instance.GetBusyScore()
                        };
                        shared.Servers.Add(serverDb);
                        shared.SaveChangesEx();
                    }
                }
            });
            _busyCheckTimer.Interval = 10 * 1000;
            _busyCheckTimer.Start();
        }

        static IPEndPoint SetDNSInfoTask()
        {
            // DNS
            string host = Dns.GetHostName();
            IPHostEntry ipHost = Dns.GetHostEntry(host);
            IPAddress ipAddr = ipHost.AddressList[1];
            IPEndPoint endPoint = new IPEndPoint(ipAddr, Port);
            IpAddress = ipAddr.ToString();

            return endPoint;
        }

        static void StartMetricsLoggingTask()
        {
            _metricsLogTimer.AutoReset = true;
            _metricsLogTimer.Interval = 5000;
            _metricsLogTimer.Elapsed += (s, e) =>
            {
                long recv = ServerMetrics.ExchangePacketsReceived();
                long sent = ServerMetrics.ExchangePacketsSent();
                long tickMs = ServerMetrics.GetTickDuration();
                int players = SessionManager.Instance.GetPlayerCount();

                Log.Information(
                    "[Metrics] PacketsRecv/s={PacketsRecvPerSec:F1} PacketsSent/s={PacketsSentPerSec:F1} TickMs={TickMs} Players={Players}",
                    recv / 5.0, sent / 5.0, tickMs, players);
            };
            _metricsLogTimer.Start();
        }

        static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("logs/server-.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Console.CancelKeyPress += (sender, e) =>
            {
                Log.Information("서버 종료 시그널 감지! Graceful Shutdown 시작...");

                // OS야, 프로세스 바로 죽이지 마! 내가 알아서 정리하고 끌게!
                e.Cancel = true;

                //Thread Loop 종료
                _cts.Cancel();
            };

            //함수 순서 주의
            ConfigManager.LoadConfig();
            DataManager.LoadData();

            GameLogic.Instance.Push(() => { GameLogic.Instance.Add(1); });

            Name = ConfigManager.Config.worldName;
            Port = ConfigManager.Config.port;

            _listener.Init(SetDNSInfoTask(), () => { return SessionManager.Instance.Generate(); });

            Log.Information("Server started. World={WorldName} Port={Port} IP={IpAddress}", Name, Port, IpAddress);

            StartServerInfoTask();
            StartMetricsLoggingTask();

            // DbTask
            {
                _dbThread = new Thread(DbTask) { Name = "DB" };
                _dbThread.Start();
            }
            // LogDbTask
            {
                _logDbThread = new Thread(LogDbTask) { Name = "LogDB" };
                _logDbThread.Start();
            }
            // Redis 초기화
            string redisConnectionString = ConfigManager.Config.redisConnectionString;
            RedisManager.Instance.Init(redisConnectionString);

            // NetworkTask
            //{
            //	Thread t = new Thread(NetworkTask);
            //	t.Name = "Network Send";
            //	t.Start();
            //}

            // GameLogic -> Main Thread
            Thread.CurrentThread.Name = "GameLogic";
            GameLogicTask();

            //Thread Loop 종료후 Graceful Shutdown
            DoGracefulShutdown();
        }
    }
}
