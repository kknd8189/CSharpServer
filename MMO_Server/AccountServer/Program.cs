using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedDB.Redis;

namespace AccountServer
{
	public class Program
	{
		public static void Main(string[] args)
		{
			// 부하 테스트 ramp-up 시 동시 spawn 으로 ThreadPool 워커가 천천히 증가하면서 (~500ms 페널티)
			// 로그인/계정생성 요청이 큐잉되는 현상 방지. 기본값(코어수)은 200/300 CCU 부근에서 starvation.
			ThreadPool.SetMinThreads(200, 200);

			var host = CreateHostBuilder(args).Build();

			// Redis 초기화
			var config = host.Services.GetRequiredService<IConfiguration>();
			string redisConn = config.GetConnectionString("RedisConnection");
			RedisManager.Instance.Init(redisConn);

			host.Run();
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.UseStartup<Startup>();
				});
	}
}
