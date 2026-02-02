using System;
using System.Linq;
using System.Net;
using System.Threading;
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

		static void Main(string[] args)
		{
			//함수 순서 주의
			ConfigManager.LoadConfig();
			DataManager.LoadData();

			GameLogic.Instance.Push(() => { GameLogic.Instance.Add(1); });

			Name = ConfigManager.Config.worldName;
			Port = ConfigManager.Config.port;

			_listener.Init( SetDNSInfoTask(), () => { return SessionManager.Instance.Generate(); });

			Console.WriteLine( $"ServerInfo\n" 
				+ $"WorldName : {Program.Name}\n" 
				+ $"Port : {Program.Port}\n"
				+ $"IpAddress : {Program.IpAddress}\n"
				+ "Listening...");

			StartServerInfoTask();

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
