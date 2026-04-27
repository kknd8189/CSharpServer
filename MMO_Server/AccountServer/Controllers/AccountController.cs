using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccountServer.DB;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharedDB;
using SharedDB.Redis;

namespace AccountServer.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	public class AccountController : ControllerBase
	{
		AppDbContext _context;
		SharedDbContext _shared;

		public AccountController(AppDbContext context, SharedDbContext shared)
		{
			_context = context;
			_shared = shared;
		}

		[HttpPost]
		[Route("create")]
		public CreateAccountPacketRes CreateAccount([FromBody] CreateAccountPacketReq req)
		{
			CreateAccountPacketRes res = new CreateAccountPacketRes();

			AccountDb account = _context.Accounts
									.AsNoTracking()
									.Where(a => a.AccountName == req.AccountName)
									.FirstOrDefault();

			if (account == null)
			{
				_context.Accounts.Add(new AccountDb()
				{
					AccountName = req.AccountName,
					Password = req.Password
				});

				bool success = _context.SaveChangesEx();
				res.CreateOk = success;
			}
			else
			{
				res.CreateOk = false;
			}

			return res;
		}

		[HttpPost]
		[Route("login")]
		public LoginAccountPacketRes LoginAccount([FromBody] LoginAccountPacketReq req)
		{
			LoginAccountPacketRes res = new LoginAccountPacketRes();

			AccountDb account = _context.Accounts
				.AsNoTracking()
				.Where(a => a.AccountName == req.AccountName && a.Password == req.Password)
				.FirstOrDefault();

			if (account == null)
			{
				res.LoginOk = false;
			}
			else
			{
				res.LoginOk = true;

				// 세션 토큰 생성 + Redis 저장 (TTL 300s)
				string sessionToken = Guid.NewGuid().ToString("N");
				bool ok = RedisAuth.SaveSessionToken(account.AccountDbId, sessionToken);
				if (!ok)
				{
					res.LoginOk = false;
					return res;
				}

				res.AccountId = account.AccountDbId;
				res.Token = sessionToken;
				res.ServerList = new List<ServerInfo>();

				foreach (ServerDb serverDb in _shared.Servers)
				{
					res.ServerList.Add(new ServerInfo()
					{
						Name = serverDb.Name,
						IpAddress = serverDb.IpAddress,
						Port = serverDb.Port,
						BusyScore = serverDb.BusyScore
					});
				}
			}

			return res;
		}
	}
}
