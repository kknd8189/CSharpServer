using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace DummyClient.Auth
{
	public class LoginResult
	{
		public int AccountId;
		public string Token;
	}

	public static class AccountServerClient
	{
		// TODO (B2): BaseAddress / Timeout을 CLI args로 빼기
		// 컨테이너 스택 대상: http 5000 포트만 호스트에 퍼블리시됨.
		private const string BaseAddress = "http://localhost:5000/";

		private static readonly HttpClient _http = CreateClient();

		private static HttpClient CreateClient()
		{
			// 개발용 자체 서명 인증서 무시. 프로덕션 사용 금지.
			var handler = new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
			};
			return new HttpClient(handler)
			{
				BaseAddress = new Uri(BaseAddress),
				Timeout = TimeSpan.FromSeconds(10),
			};
		}

		public static async Task<bool> CreateAccountAsync(string accountName, string password)
		{
			try
			{
				var req = new CreateAccountPacketReq { AccountName = accountName, Password = password };
				var resp = await _http.PostAsJsonAsync("api/account/create", req);
				if (!resp.IsSuccessStatusCode) return false;

				var result = await resp.Content.ReadFromJsonAsync<CreateAccountPacketRes>();
				return result?.CreateOk ?? false;
			}
			catch
			{
				return false;
			}
		}

		public static async Task<LoginResult> LoginAsync(string accountName, string password)
		{
			try
			{
				var req = new LoginAccountPacketReq { AccountName = accountName, Password = password };
				var resp = await _http.PostAsJsonAsync("api/account/login", req);
				if (!resp.IsSuccessStatusCode) return null;

				var result = await resp.Content.ReadFromJsonAsync<LoginAccountPacketRes>();
				if (result == null || !result.LoginOk) return null;

				return new LoginResult
				{
					AccountId = result.AccountId,
					Token = result.Token,
				};
			}
			catch
			{
				return null;
			}
		}

		// AccountServer/DB/WebPacket.cs 미러
		private class CreateAccountPacketReq
		{
			public string AccountName { get; set; }
			public string Password { get; set; }
		}

		private class CreateAccountPacketRes
		{
			public bool CreateOk { get; set; }
		}

		private class LoginAccountPacketReq
		{
			public string AccountName { get; set; }
			public string Password { get; set; }
		}

		private class LoginAccountPacketRes
		{
			public bool LoginOk { get; set; }
			public int AccountId { get; set; }
			public string Token { get; set; }
			// ServerList는 일단 단일 게임서버 가정으로 생략
		}
	}
}
