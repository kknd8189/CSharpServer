using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Server.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Server.DB
{
	public class AppDbContext : DbContext
	{
		public DbSet<AccountDb> Accounts { get; set; }
		public DbSet<PlayerDb> Players { get; set; }
		public DbSet<ItemDb> Items { get; set; }

		// EF Core SQL 로그 — 부하 테스트 시 콘솔이 꽉 차서 비활성화. 디버깅 시 주석 해제.
		// static readonly ILoggerFactory _logger = LoggerFactory.Create(builder => { builder.AddConsole(); });

		string _connectionString = @"Server=localhost;Port=3306;Database=GameDb;User=DY;Password=3306;";


        protected override void OnConfiguring(DbContextOptionsBuilder options)
		{
			base.OnConfiguring(options);

			string connectionString = ConfigManager.Config == null ? _connectionString : ConfigManager.Config.connectionString;

            options
				//.UseLoggerFactory(_logger)
                .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        }

		protected override void OnModelCreating(ModelBuilder builder)
		{
			builder.Entity<AccountDb>()
				.HasIndex(a => a.AccountName)
				.IsUnique();

			builder.Entity<PlayerDb>()
				.HasIndex(p => p.PlayerName)
				.IsUnique();
		}
	}
}
