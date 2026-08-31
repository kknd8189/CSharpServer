using Protocol;
using ServerCore;
using System;

class PacketHandler
{
	public static void S_EnterGameHandler(PacketSession session, IPacket packet)
	{
		S_EnterGame enterGamePacket = packet as S_EnterGame;
		ServerSession serverSession = (ServerSession)session;

		if (enterGamePacket?.Player == null)
			return;

		// 서버가 진입을 거부하면 Player 가 비어 있는 S_EnterGame 이 온다.
		// 이걸 안 보면 더미는 이동 타이머를 켜지 않은 채 조용히 붙어만 있게 된다.
		if (enterGamePacket.Player == null)
		{
			Console.WriteLine($"[EnterGame 거부] Dummy={serverSession.DummyId} Account={serverSession.AccountId} — 이 더미는 부하를 만들지 않습니다");
			return;
		}

		serverSession.MyPlayerId = enterGamePacket.Player.ObjectId;
		if (enterGamePacket.Player.PosInfo != null)
		{
			serverSession.PosX = enterGamePacket.Player.PosInfo.PosX;
			serverSession.PosY = enterGamePacket.Player.PosInfo.PosY;
			serverSession.PosZ = enterGamePacket.Player.PosInfo.PosZ;
		}

		serverSession.StartSimulation();
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
		ServerSession serverSession = (ServerSession)session;

		// 내 캐릭터에 대한 S_Move 는 서버의 권위 좌표다.
		// 이걸 무시하면 서버가 이동을 거부했을 때(벽/점유/검증 실패) 클라만 계속
		// 자기 좌표를 밀고 나가 서버와 갈라지고, 벌어진 격차가 그대로 다음 이동의
		// 거리로 잡혀 결국 텔레포트 위반으로 오인된다.
		// 실제 클라이언트가 반드시 해야 하는 처리이고, 더미도 같아야 부하 테스트가 유효하다.
		if (movePacket?.PosInfo == null)
			return;
		if (movePacket.ObjectId != serverSession.MyPlayerId)
			return;

		serverSession.PosX = movePacket.PosInfo.PosX;
		serverSession.PosY = movePacket.PosInfo.PosY;
		serverSession.PosZ = movePacket.PosInfo.PosZ;
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
			// 이름을 DummyId(프로세스 로컬 일련번호)로 지으면 안 된다.
			// DummyClient 를 두 대 띄우면 양쪽 다 1 번부터 시작해서 같은 이름을 요청하고,
			// 두 번째 대는 "이름 중복"으로 캐릭터 생성에 실패한다. 그 더미는 접속만 된 채
			// 게임에 못 들어가는 유령 세션이 되어 부하 테스트 결과를 조용히 오염시킨다.
			// AccountId 는 서버가 발급한 전역 고유값이라 프로세스가 몇 대든 겹치지 않는다.
			C_CreatePlayer createPacket = new C_CreatePlayer();
			createPacket.Name = $"Player_{serverSession.AccountId.ToString("00000")}";
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
			// 생성 실패. 여기를 비워두면 그 더미는 아무 일도 하지 않는 채 접속만 유지된다.
			// 부하 테스트에서는 "접속 수는 맞는데 부하가 안 걸리는" 형태로 나타나
			// 측정값을 조용히 낮춘다. 반드시 눈에 띄게 만든다.
			Console.WriteLine($"[CreatePlayer 실패] Dummy={serverSession.DummyId} Account={serverSession.AccountId} — 이 더미는 부하를 만들지 않습니다");
			return;
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
