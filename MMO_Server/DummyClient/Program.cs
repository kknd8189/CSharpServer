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
		const int RampUpChunkSize = 50;
		const int RampUpDelayMs = 500;

		static IPEndPoint _endPoint;
		static int _nextAccountId = 1;
		static readonly object _idLock = new object();

		// 동시 spawn 요청 시 ramp-up이 겹쳐 SYN 폭주가 나지 않도록 직렬화
		static readonly SemaphoreSlim _spawnGate = new SemaphoreSlim(1, 1);

		static async Task Main(string[] args)
		{
			Thread.Sleep(3000);

			string host = Dns.GetHostName();
			IPHostEntry ipHost = Dns.GetHostEntry(host);
			IPAddress ipAddr = ipHost.AddressList[1];
			_endPoint = new IPEndPoint(ipAddr, 7777);

			// 초기 N (선택). `dotnet run -- 50` 처럼 인자로 주면 시작 시 즉시 spawn.
			int initialCount = 0;
			if (args.Length > 0 && int.TryParse(args[0], out int parsed) && parsed > 0)
				initialCount = parsed;

			if (initialCount > 0)
			{
				Console.WriteLine($"[System] Spawning initial {initialCount} dummies...");
				await SpawnDummies(initialCount);
				Console.WriteLine($"[System] Initial spawn complete. Active = {SessionManager.Instance.Count}");
			}

			PrintHelp();

			while (true)
			{
				Console.Write("> ");
				string line = Console.ReadLine();
				if (line == null) break; // stdin EOF
				line = line.Trim();
				if (line.Length == 0) continue;

				if (line == "quit" || line == "exit")
				{
					int all = SessionManager.Instance.Count;
					Console.WriteLine($"[System] Disconnecting all {all} dummies...");
					SessionManager.Instance.DisconnectN(all);
					break;
				}
				else if (line == "?" || line == "count" || line == "help")
				{
					Console.WriteLine($"[System] Active dummies: {SessionManager.Instance.Count}");
					if (line == "help") PrintHelp();
				}
				else if (line.StartsWith("+"))
				{
					if (int.TryParse(line.Substring(1), out int add) && add > 0)
					{
						Console.WriteLine($"[System] Spawning +{add} (current: {SessionManager.Instance.Count})...");
						_ = Task.Run(async () =>
						{
							try
							{
								await SpawnDummies(add);
								Console.WriteLine($"[System] +{add} done. Active = {SessionManager.Instance.Count}");
							}
							catch (Exception ex)
							{
								Console.WriteLine($"[Error] Spawn failed: {ex.Message}");
							}
						});
					}
					else
					{
						Console.WriteLine("[System] Usage: +N  (N > 0)");
					}
				}
				else if (line.StartsWith("-"))
				{
					if (int.TryParse(line.Substring(1), out int rm) && rm > 0)
					{
						int actual = Math.Min(rm, SessionManager.Instance.Count);
						Console.WriteLine($"[System] Disconnecting {actual} (current: {SessionManager.Instance.Count})...");
						SessionManager.Instance.DisconnectN(actual);
					}
					else
					{
						Console.WriteLine("[System] Usage: -N  (N > 0)");
					}
				}
				else
				{
					Console.WriteLine("[System] Unknown command.");
					PrintHelp();
				}
			}

			Console.WriteLine("[System] Bye.");
		}

		static void PrintHelp()
		{
			Console.WriteLine("[System] Commands:");
			Console.WriteLine("  +N      spawn N more dummies (e.g. +50)");
			Console.WriteLine("  -N      disconnect N dummies (e.g. -100)");
			Console.WriteLine("  ?       show current active count");
			Console.WriteLine("  quit    disconnect all and exit");
		}

		static async Task SpawnDummies(int count)
		{
			await _spawnGate.WaitAsync();
			try
			{
				int startId, endId;
				lock (_idLock)
				{
					startId = _nextAccountId;
					endId = startId + count - 1;
					_nextAccountId = endId + 1;
				}

				for (int s = startId; s <= endId; s += RampUpChunkSize)
				{
					int e = Math.Min(s + RampUpChunkSize - 1, endId);
					var chunkTasks = Enumerable.Range(s, e - s + 1).Select(async id =>
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
						connector.Connect(_endPoint,
							() => SessionManager.Instance.Generate(login.AccountId, login.Token),
							count: 1);
					});

					await Task.WhenAll(chunkTasks);

					if (e < endId)
						await Task.Delay(RampUpDelayMs);
				}
			}
			finally
			{
				_spawnGate.Release();
			}
		}
	}
}
