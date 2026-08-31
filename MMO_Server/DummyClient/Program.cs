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

			// 컨테이너 게임서버는 호스트에 7777 포트가 퍼블리시됨 → 루프백으로 접속.
			_endPoint = new IPEndPoint(IPAddress.Loopback, 7777);

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
				else if (line.StartsWith("cluster"))
				{
					HandleClusterCommand(line);
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
			Console.WriteLine("  cluster            show current load profile");
			Console.WriteLine("  cluster on         gather all dummies (default 45,45 r=15)");
			Console.WriteLine("  cluster on X Z R   gather at (X,Z) with radius R");
			Console.WriteLine("  cluster off        scatter (back to map-wide random walk)");
			Console.WriteLine("  quit    disconnect all and exit");
		}

		// 밀집 시나리오 토글. 재접속 없이 같은 세션 집합으로 A/B 를 재기 위한 것이다.
		// 세션을 다시 만들면 스폰 위치·계정·접속 순서가 전부 달라져서
		// 결과 차이가 밀집도 때문인지 세션 구성 때문인지 구분할 수 없다.
		//
		// 사용법: cluster on -> 30초 관찰 -> cluster off -> 30초 관찰.
		// 반경을 30 / 20 / 15 / 10 으로 훑으면 밀도-틱시간 곡선이 나온다.
		static void HandleClusterCommand(string line)
		{
			string[] tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

			if (tok.Length == 1)
			{
				Console.WriteLine($"[System] {LoadProfile.Describe()}");
				return;
			}

			if (tok[1] == "off")
			{
				LoadProfile.Cluster = false;
				Console.WriteLine($"[System] {LoadProfile.Describe()}");
				Console.WriteLine("[System] 흩어지는 데 시간이 걸립니다. 30초쯤 뒤부터 측정하세요.");
				return;
			}

			if (tok[1] == "on")
			{
				if (tok.Length >= 5)
				{
					if (int.TryParse(tok[2], out int cx) &&
						int.TryParse(tok[3], out int cz) &&
						int.TryParse(tok[4], out int r) && r > 0)
					{
						LoadProfile.CenterX = cx;
						LoadProfile.CenterZ = cz;
						LoadProfile.Radius = r;
					}
					else
					{
						Console.WriteLine("[System] Usage: cluster on X Z R  (R > 0)");
						return;
					}
				}

				LoadProfile.Cluster = true;
				Console.WriteLine($"[System] {LoadProfile.Describe()}");
				WarnIfTooTight();
				Console.WriteLine("[System] 모이는 데 시간이 걸립니다(최대 거리 ~110칸, 칸당 0.2~0.5초).");
				Console.WriteLine("[System] game_broadcast_recipients 가 평평해진 뒤부터 측정하세요.");
				return;
			}

			Console.WriteLine("[System] Usage: cluster [on [X Z R] | off]");
		}

		// 서버 Map.CanGo 는 한 칸에 한 오브젝트만 허용한다(_objects[x,y,z] == null 검사).
		// 그래서 반경이 인원 대비 너무 작으면 물리적으로 다 못 들어가고, 못 들어간 더미들이
		// 외곽에서 정체한다. 그 상태의 측정은 "밀집 부하"가 아니라 "정체 부하"라서
		// 결과 해석이 통째로 어긋난다. 돌리기 전에 잡아주는 게 낫다.
		//
		// 수용 가능 칸 = (2R+1)^2, 벽/몬스터가 차지하는 몫이 있으므로 여유를 둔다.
		static void WarnIfTooTight()
		{
			int n = SessionManager.Instance.Count;
			if (n == 0)
				return;

			int r = LoadProfile.Radius;
			long cells = (2L * r + 1) * (2L * r + 1);
			double density = (double)n / cells;

			// 정사각형 한 변이 sqrt(N) 은 되어야 하므로 R >= (sqrt(N)-1)/2.
			// 벽/몬스터 여유로 1.3 배 넉넉하게 권장한다.
			int minR = (int)Math.Ceiling((Math.Sqrt(n * 1.3) - 1) / 2);

			if (density >= 1.0)
			{
				Console.WriteLine($"[Warn] 반경 {r} 은 {n} 명을 담을 수 없습니다 ({cells} 칸 < {n} 명).");
				Console.WriteLine($"[Warn] 외곽에서 정체합니다. 권장 최소 반경: {minR}");
			}
			else if (density >= 0.75)
			{
				Console.WriteLine($"[Warn] 밀도 {density:F2}/칸 — 포화에 가깝습니다 ({cells} 칸 / {n} 명).");
				Console.WriteLine($"[Warn] 이동 거부가 늘어 브로드캐스트가 오히려 줄 수 있습니다.");
				Console.WriteLine($"[Warn] game_move_blocked_total 을 같이 보세요. 여유를 두려면 반경 {minR} 이상.");
			}
			else
			{
				Console.WriteLine($"[System] 밀도 {density:F2}/칸 ({cells} 칸 / {n} 명). 시야 121칸 기준 예상 팬아웃 ~{(int)(density * 121)} 명.");
			}
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
