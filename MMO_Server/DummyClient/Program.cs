using DummyClient.Auth;
using DummyClient.Session;
using ServerCore;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DummyClient
{
	class Program
	{
		static int DummyClientCount { get; } = 500;

		static async Task Main(string[] args)
		{
			Thread.Sleep(3000);

			// DNS
			string host = Dns.GetHostName();
			IPHostEntry ipHost = Dns.GetHostEntry(host);
			IPAddress ipAddr = ipHost.AddressList[1];
			IPEndPoint endPoint = new IPEndPoint(ipAddr, 7777);

			// 각 더미 클라마다 (계정 생성 시도) → 로그인 → 게임서버 연결을 병렬 진행.
			// TODO (B2): ramp-up 도입해 한 번에 N개씩 단계적으로 띄우기
			var tasks = Enumerable.Range(1, DummyClientCount).Select(async id =>
			{
				string accountName = $"DummyClient_{id:D4}";
				string password = "1234";

				await AccountServerClient.CreateAccountAsync(accountName, password);

				var login = await AccountServerClient.LoginAsync(accountName, password);
				if (login == null)
				{
					Console.WriteLine($"[Login Failed] {accountName}");
					return;
				}

				var connector = new Connector();
				connector.Connect(endPoint,
					() => SessionManager.Instance.Generate(login.AccountId, login.Token),
					count: 1);
			});

			await Task.WhenAll(tasks);

			Console.WriteLine("[System] All dummy clients started.");

			while (true)
			{
				Thread.Sleep(10000);
			}
		}
	}
}
