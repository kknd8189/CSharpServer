using System;
using System.Linq;
using System.Net;
using System.Threading;
using Serilog;
using Server.Data;
using Server.DB;
using Server.Game;
using ServerCore;
using SharedDB;

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

        static void GameLogicTask()
		{
			while (true)
			{
				GameLogic.Instance.Update();
				Thread.Sleep(0);
			}
		}

		static void DbTask()
		{
			while (true)
			{
				DbTransaction.Instance.Flush();
				Thread.Sleep(0);
			}
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
			System.Timers.Timer t = new System.Timers.Timer();
			t.AutoReset = true;
			t.Elapsed += new System.Timers.ElapsedEventHandler((s, e) =>
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
			t.Interval = 10 * 1000;
			t.Start();
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
			System.Timers.Timer t = new System.Timers.Timer();
			t.AutoReset = true;
			t.Interval = 5000;
			t.Elapsed += (s, e) =>
			{
				long recv = ServerMetrics.ExchangePacketsReceived();
				long sent = ServerMetrics.ExchangePacketsSent();
				long tickMs = ServerMetrics.GetTickDuration();
				int players = SessionManager.Instance.GetPlayerCount();

				Log.Information(
					"[Metrics] PacketsRecv/s={PacketsRecvPerSec:F1} PacketsSent/s={PacketsSentPerSec:F1} TickMs={TickMs} Players={Players}",
					recv / 5.0, sent / 5.0, tickMs, players);
			};
			t.Start();
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

			AppDomain.CurrentDomain.ProcessExit += (s, e) =>
			{
				Log.Information("Server shutting down");
				Log.CloseAndFlush();
			};

			//함수 순서 주의
			ConfigManager.LoadConfig();
			DataManager.LoadData();

			GameLogic.Instance.Push(() => { GameLogic.Instance.Add(1); });

			Name = ConfigManager.Config.worldName;
			Port = ConfigManager.Config.port;

			_listener.Init( SetDNSInfoTask(), () => { return SessionManager.Instance.Generate(); });

			Log.Information("Server started. World={WorldName} Port={Port} IP={IpAddress}", Name, Port, IpAddress);

			StartServerInfoTask();
			StartMetricsLoggingTask();

			// DbTask
			{
				Thread t = new Thread(DbTask);
				t.Name = "DB";
				t.Start();
			}

			// NetworkTask
			//{
			//	Thread t = new Thread(NetworkTask);
			//	t.Name = "Network Send";
			//	t.Start();
			//}

			// GameLogic
			Thread.CurrentThread.Name = "GameLogic";
			GameLogicTask();
		}
	}
}
