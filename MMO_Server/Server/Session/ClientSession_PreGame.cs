using Protocol;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.DB;
using Server.DB.LogDB;
using Server.Game;
using ServerCore;
using SharedDB.Redis;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Server
{
	public partial class ClientSession : PacketSession
	{
		public int AccountDbId { get; private set; }
		public List<LobbyPlayerInfo> LobbyPlayers { get; set; } = new List<LobbyPlayerInfo>();

		public async Task<bool> HandleLoginAsync(C_Login loginPacket)
		{
			if (ServerState != PlayerServerState.ServerStateLogin)
				return false;

			// Redis 세션 토큰 검증 (1회용, AccountServer가 발급) — async로 IOCP/요청 스레드 블로킹 회피
			if (await RedisAuth.VerifyTokenAsync(loginPacket.AccountID, loginPacket.Token) == false)
			{
				Send(new S_Login() { LoginOk = 0 });
				Disconnect(CloseReason.AuthFailed);
				return false;
			}

			LobbyPlayers.Clear();

			using (AppDbContext db = new AppDbContext())
			{
				AccountDb findAccount = await db.Accounts
						.Include(a => a.Players)
					.Where(a => a.AccountDbId == loginPacket.AccountID).FirstOrDefaultAsync();

				if (findAccount == null)
				{
					// 토큰 검증은 통과했으나 GameDb 에 Account 행이 없는 경우.
					// 계정 인증은 AccountServer(AccountDB), 게임 데이터는 GameDb 가 담당하므로
					// 최초 로그인 시 게임서버가 자신의 Account 행을 직접 만든다(lazy provisioning).
					// AccountDbId 는 EF 가 자동 증가시키지 않도록 raw SQL 로 AccountServer 가
					// 부여한 값을 그대로 넣어 Player FK / 두 DB 간 정합을 맞춘다.
					// INSERT IGNORE: 동일 계정 동시 로그인 시 중복 PK 경합을 안전하게 흡수.
					await db.Database.ExecuteSqlInterpolatedAsync(
						$"INSERT IGNORE INTO Account (AccountDbId) VALUES ({loginPacket.AccountID})");

					findAccount = await db.Accounts
							.Include(a => a.Players)
						.Where(a => a.AccountDbId == loginPacket.AccountID).FirstOrDefaultAsync();

					if (findAccount == null)
					{
						Send(new S_Login() { LoginOk = 0 });
						Disconnect(CloseReason.AuthFailed);
						return false;
					}
				}

				// AccountDbId 메모리에 기억
				AccountDbId = findAccount.AccountDbId;

				S_Login loginOk = new S_Login() { LoginOk = 1 };
				foreach (PlayerDb playerDb in findAccount.Players)
				{
					LobbyPlayerInfo lobbyPlayer = new LobbyPlayerInfo()
					{
						PlayerDbId = playerDb.PlayerDbId,
						Name = playerDb.PlayerName,
						StatInfo = new StatInfo()
						{
							Level = playerDb.Level,
							Hp = playerDb.Hp,
							MaxHp = playerDb.MaxHp,
							Attack = playerDb.Attack,
							Speed = playerDb.Speed,
							TotalExp = playerDb.TotalExp
						}
					};

					// 메모리에도 들고 있다
					LobbyPlayers.Add(lobbyPlayer);

					// 패킷에 넣어준다
					loginOk.Players.Add(lobbyPlayer);
				}

				Send(loginOk);
				// 로비로 이동
				ServerState = PlayerServerState.ServerStateLobby;
				return true;
			}
		}

		public async Task HandleEnterGameAsync(C_EnterGame enterGamePacket)
		{
			if (ServerState != PlayerServerState.ServerStateLobby)
				return;

			LobbyPlayerInfo playerInfo = LobbyPlayers.Find(p => p.Name == enterGamePacket.Name);
			if (playerInfo == null)
				return;

			MyPlayer = ObjectManager.Instance.Add<Player>();
			{
				MyPlayer.PlayerDbId = playerInfo.PlayerDbId;
				MyPlayer.Info.Name = playerInfo.Name;
				MyPlayer.Info.PosInfo.State = CreatureState.Idle;
				MyPlayer.Info.PosInfo.MoveDir = MoveDir.Up;
				MyPlayer.Info.PosInfo.PosX = 0;
				MyPlayer.Info.PosInfo.PosY = 0;
                MyPlayer.Info.PosInfo.PosZ = 0;

                MyPlayer.Stat.MergeFrom(playerInfo.StatInfo);
				MyPlayer.Session = this;

				S_ItemList itemListPacket = new S_ItemList();

				// 아이템 목록을 갖고 온다 (async — IOCP 점유 회피)
				using (AppDbContext db = new AppDbContext())
				{
					List<ItemDb> items = await db.Items
						.Where(i => i.OwnerDbId == playerInfo.PlayerDbId)
						.ToListAsync();

					foreach (ItemDb itemDb in items)
					{
						Item item = Item.MakeItem(itemDb);
						if (item != null)
						{
							MyPlayer.Inven.Add(item);

							ItemInfo info = new ItemInfo();
							info.MergeFrom(item.Info);
							itemListPacket.Items.Add(info);
						}
					}
				}

				Send(itemListPacket);
			}

			ServerState = PlayerServerState.ServerStateGame;

			GameLogic.Instance.Push(() =>
			{
				GameRoom room = GameLogic.Instance.Find(1);
				room.Push(room.EnterGame, MyPlayer, true);
			});
		}

		public async Task HandleCreatePlayerAsync(C_CreatePlayer createPacket)
		{
			// TODO : 이런 저런 보안 체크
			if (ServerState != PlayerServerState.ServerStateLobby)
				return;

			using (AppDbContext db = new AppDbContext())
			{
				PlayerDb findPlayer = await db.Players
					.Where(p => p.PlayerName == createPacket.Name).FirstOrDefaultAsync();

				if (findPlayer != null)
				{
					// 이름이 겹친다
					Send(new S_CreatePlayer());
				}
				else
				{
					// 1레벨 스탯 정보 추출
					StatInfo stat = null;
					DataManager.StatDict.TryGetValue(1, out stat);

					// DB에 플레이어 만들어줘야 함
					PlayerDb newPlayerDb = new PlayerDb()
					{
						PlayerName = createPacket.Name,
						Level = stat.Level,
						Hp = stat.Hp,
						MaxHp = stat.MaxHp,
						Attack = stat.Attack,
						Speed = stat.Speed,
						TotalExp = 0,
						AccountDbId = AccountDbId
					};

					db.Players.Add(newPlayerDb);
					bool success = await db.SaveChangesExAsync();
					if (success == false)
						return;

					// 메모리에 추가
					LobbyPlayerInfo lobbyPlayer = new LobbyPlayerInfo()
					{
						PlayerDbId = newPlayerDb.PlayerDbId,
						Name = createPacket.Name,
						StatInfo = new StatInfo()
						{
							Level = stat.Level,
							Hp = stat.Hp,
							MaxHp = stat.MaxHp,
							Attack = stat.Attack,
							Speed = stat.Speed,
							TotalExp = 0
						}
					};

					// 메모리에도 들고 있다
					LobbyPlayers.Add(lobbyPlayer);

					// 클라에 전송
					S_CreatePlayer newPlayer = new S_CreatePlayer() { Player = new LobbyPlayerInfo() };
					newPlayer.Player.MergeFrom(lobbyPlayer);

					Send(newPlayer);
				}
			}
		}
	}
}
