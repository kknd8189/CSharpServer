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
		static int RampUpChunkSize { get; } = 50;
		static int RampUpDelayMs { get; } = 500;

		static async Task Main(string[] args)
		{
			Thread.Sleep(3000);

			// DNS
			string host = Dns.GetHostName();
			IPHostEntry ipHost = Dns.GetHostEntry(host);
			IPAddress ipAddr = ipHost.AddressList[1];
			IPEndPoint endPoint = new IPEndPoint(ipAddr, 7777);

			// 각 더미 클라마다 (계정 생성 시도) → 로그인 → 게임서버 연결을 병렬 진행.
			// ramp-up: chunkSize 단위로 끊어서 띄움. 동시 SYN 폭주로 인한 ConnectionRefused 회피.
			for (int start = 1; start <= DummyClientCount; start += RampUpChunkSize)
			{
				int end = Math.Min(start + RampUpChunkSize - 1, DummyClientCount);
				var chunkTasks = Enumerable.Range(start, end - start + 1).Select(async id =>
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

				await Task.WhenAll(chunkTasks);

				if (end < DummyClientCount)
					await Task.Delay(RampUpDelayMs);
			}

			Console.WriteLine("[System] All dummy clients started.");

			while (true)
			{
				Thread.Sleep(10000);
			}
		}
	}
}
