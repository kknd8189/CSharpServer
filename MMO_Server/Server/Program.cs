using System;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
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

        // Windows 의 기본 timer 정밀도(15.625ms) 를 1ms 로 끌어올림.
        // Thread.Sleep / PeriodicTimer 등 모든 시간 대기가 ms 단위로 정확해짐 → 30Hz fixed timestep 정밀도 확보.
        // 부작용: 시스템 전체 timer interrupt 빈도 ↑, 노트북 배터리 미세 소모 (서버용 무관).
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint period);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint period);

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

            // 5 셧다운 중 드롭/DLQ 집계 (포스트모템용, 정상 셧다운이면 모두 0)
            long dbDropped = DbTransaction.Instance.DroppedJobCount;
            long logDropped = LogTransaction.Instance.DroppedLogCount;
            long logDeadLetter = LogTransaction.Instance.DeadLetterCount;

            if (logDeadLetter > 0)
                Log.Warning("LogDB DLQ에 {Count}건 덤프됨. 수동 복구 경로: {Path}",
                    logDeadLetter, LogTransaction.DeadLetterDirectory);

            if (dbDropped > 0 || logDropped > 0)
                Log.Warning("셧다운 중 유실된 Job: DB={DbDropped}건, Log={LogDropped}건",
                    dbDropped, logDropped);
            else if (logDeadLetter == 0)
                Log.Information("모든 Job이 정상 플러시됨.");

            // 6  로그 플러시 및 메인 스레드 놔주기
            Log.CloseAndFlush(); // 비동기로 남은 로그들을 파일에 확실히 다 씀
        }


        // 30Hz fixed timestep (~33ms 주기). frame 처리 후 남은 시간만 sleep,
        // budget 초과 시 곧장 다음 frame (catch-up). tight loop 회피로 1 core 100% 점유 해소.
        const int FrameMs = 33;

        static void GameLogicTask()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                long frameStart = System.Diagnostics.Stopwatch.GetTimestamp();
                GameLogic.Instance.Update();

                long elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - frameStart)
                                 * 1000L / System.Diagnostics.Stopwatch.Frequency;
                int sleepMs = (int)(FrameMs - elapsedMs);
                if (sleepMs > 0)
                    Thread.Sleep(sleepMs);
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
            // 외부에 광고할 주소(ServerInfo 등록값): 컨테이너/운영마다 다르므로 config 로 명시.
            // 과거엔 Dns.GetHostEntry(...).AddressList[1] 로 자동탐지했으나, 컨테이너에서
            // ::1(루프백)을 잡아 잘못 등록되는 문제가 있었다.
            IpAddress = ConfigManager.Config.publicIp;

            // 바인딩은 모든 인터페이스(0.0.0.0)에 — 컨테이너 포트매핑/외부 NIC 모두 수신.
            return new IPEndPoint(IPAddress.Any, Port);
        }

        static void StartMetricsLoggingTask()
        {
            _metricsLogTimer.AutoReset = true;
            _metricsLogTimer.Interval = 5000;
            _metricsLogTimer.Elapsed += (s, e) =>
            {
                long recv = ServerMetrics.ExchangePacketsReceived();
                long sent = ServerMetrics.ExchangePacketsSent();
                var (tickAvgUs, tickMaxUs, workTicks, idleTicks) = ServerMetrics.ExchangeTickStats();
                int players = SessionManager.Instance.GetPlayerCount();

                Log.Information(
                    "[Metrics] PacketsRecv/s={PacketsRecvPerSec:F1} PacketsSent/s={PacketsSentPerSec:F1} TickAvg={TickAvgUs}us TickMax={TickMaxUs}us WorkTicks={WorkTicks} IdleTicks={IdleTicks} Players={Players}",
                    recv / 5.0, sent / 5.0, tickAvgUs, tickMaxUs, workTicks, idleTicks, players);
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

            // Windows timer 정밀도 1ms 로 상향 (Linux/macOS 는 기본 1ms 라 호출 불필요)
            if (OperatingSystem.IsWindows())
                TimeBeginPeriod(1);

            // 부하 테스트 ramp-up 시 IOCP/Worker 스레드가 천천히 증가하면서 (~500ms 페널티)
            // 패킷 처리 콜백이 지연되는 현상 방지. 기본값(코어수)은 200/300 CCU 부근에서 starvation.
            ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);

            //함수 순서 주의
            ConfigManager.LoadConfig();
            // SharedDbContext(파라미터 없는 생성자)는 ConfigManager 가 아닌 자체 static
            // 필드를 쓰므로, config 로드 직후 여기서 명시적으로 주입한다.
            SharedDbContext.ConnectionString = ConfigManager.Config.sharedConnectionString;
            DataManager.LoadData();

            GameLogic.Instance.Push(() => { GameLogic.Instance.Add(1); });

            Name = ConfigManager.Config.worldName;
            Port = ConfigManager.Config.port;

            _listener.Init(SetDNSInfoTask(), () => { return SessionManager.Instance.Generate(); },
                register: 50, backlog: 1024);

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

            // 시스템 timer 정밀도 원복
            if (OperatingSystem.IsWindows())
                TimeEndPeriod(1);
        }
    }
}
