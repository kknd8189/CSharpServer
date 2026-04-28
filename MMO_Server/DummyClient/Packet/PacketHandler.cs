using Protocol;
using ServerCore;

class PacketHandler
{
	public static void S_EnterGameHandler(PacketSession session, IPacket packet)
	{
		S_EnterGame enterGamePacket = packet as S_EnterGame;
	}

	public static void S_LeaveGameHandler(PacketSession session, IPacket packet)
	{
		S_LeaveGame leaveGamePacket = packet as S_LeaveGame;
	}

	public static void S_SpawnHandler(PacketSession session, IPacket packet)
	{
		S_Spawn spawnPacket = packet as S_Spawn;
	}

	public static void S_DespawnHandler(PacketSession session, IPacket packet)
	{
		S_Despawn despawnPacket = packet as S_Despawn;
	}

	public static void S_MoveHandler(PacketSession session, IPacket packet)
	{
		S_Move movePacket = packet as S_Move;
	}

	public static void S_SkillHandler(PacketSession session, IPacket packet)
	{
		S_Skill skillPacket = packet as S_Skill;
	}

	public static void S_ChangeHpHandler(PacketSession session, IPacket packet)
	{
		S_ChangeHp changePacket = packet as S_ChangeHp;
	}

	public static void S_DieHandler(PacketSession session, IPacket packet)
	{
		S_Die diePacket = packet as S_Die;
	}

	// Step1: 서버 접속 직후. AccountServer에서 받아둔 AccountId/Token으로 C_Login 송신
	public static void S_ConnectedHandler(PacketSession session, IPacket packet)
	{
		ServerSession serverSession = (ServerSession)session;
		C_Login loginPacket = new C_Login
		{
			AccountID = serverSession.AccountId,
			Token = serverSession.Token,
		};
		serverSession.Send(loginPacket);
	}

	// Step2: 로그인 OK + 캐릭터 목록
	public static void S_LoginHandler(PacketSession session, IPacket packet)
	{
		S_Login loginPacket = (S_Login)packet;
		ServerSession serverSession = (ServerSession)session;

		if (loginPacket.Players == null || loginPacket.Players.Count == 0)
		{
			C_CreatePlayer createPacket = new C_CreatePlayer();
			createPacket.Name = $"Player_{serverSession.DummyId.ToString("0000")}";
			serverSession.Send(createPacket);
		}
		else
		{
			LobbyPlayerInfo info = loginPacket.Players[0];
			C_EnterGame enterGamePacket = new C_EnterGame();
			enterGamePacket.Name = info.Name;
			serverSession.Send(enterGamePacket);
		}
	}

	// Step3
	public static void S_CreatePlayerHandler(PacketSession session, IPacket packet)
	{
		S_CreatePlayer createOkPacket = (S_CreatePlayer)packet;
		ServerSession serverSession = (ServerSession)session;

		if (createOkPacket.Player == null)
		{
			// 생성 실패
		}
		else
		{
			C_EnterGame enterGamePacket = new C_EnterGame();
			enterGamePacket.Name = createOkPacket.Player.Name;
			serverSession.Send(enterGamePacket);
		}
	}

	public static void S_ItemListHandler(PacketSession session, IPacket packet)
	{
		S_ItemList itemList = (S_ItemList)packet;
	}

	public static void S_AddItemHandler(PacketSession session, IPacket packet)
	{
	}

	public static void S_EquipItemHandler(PacketSession session, IPacket packet)
	{
	}

	public static void S_ChangeStatHandler(PacketSession session, IPacket packet)
	{
		S_ChangeStat statPacket = (S_ChangeStat)packet;
	}

	public static void S_PingHandler(PacketSession session, IPacket packet)
	{
		ServerSession serverSession = (ServerSession)session;
		C_Pong pongPacket = new C_Pong();
		serverSession.Send(pongPacket);
	}
}
