using Server;
using Server.Game;
using ServerCore;
using Server.DB.LogDB;
using Protocol;

class PacketHandler
{
	public static void C_MoveHandler(PacketSession session, IPacket packet)
	{
		C_Move movePacket = packet as C_Move;
		ClientSession clientSession = session as ClientSession;

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleMove, player, movePacket);
	}

	public static void C_SkillHandler(PacketSession session, IPacket packet)
	{
		C_Skill skillPacket = packet as C_Skill;
		ClientSession clientSession = session as ClientSession;

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleSkill, player, skillPacket);
	}

	public static void C_LoginHandler(PacketSession session, IPacket packet)
	{
		C_Login loginPacket = packet as C_Login;
		ClientSession clientSession = session as ClientSession;
		bool ok = clientSession.HandleLogin(loginPacket);

		// 성공: 확정된 AccountDbId / 실패: 클라가 보낸 AccountID로 로그 (잘못된 토큰 시도 추적)
		int accountId = ok ? clientSession.AccountDbId : loginPacket.AccountID;
		LogHelper.LogLogin(accountId, ok, clientSession.GetIpAddress());
	}

	public static void C_EnterGameHandler(PacketSession session, IPacket packet)
	{
		C_EnterGame enterGamePacket = (C_EnterGame)packet;
		ClientSession clientSession = (ClientSession)session;
		clientSession.HandleEnterGame(enterGamePacket);
	}

	public static void C_CreatePlayerHandler(PacketSession session, IPacket packet)
	{
		C_CreatePlayer createPlayerPacket = (C_CreatePlayer)packet;
		ClientSession clientSession = (ClientSession)session;
		clientSession.HandleCreatePlayer(createPlayerPacket);
	}

	public static void C_EquipItemHandler(PacketSession session, IPacket packet)
	{
		C_EquipItem equipPacket = (C_EquipItem)packet;
		ClientSession clientSession = (ClientSession)session;

		Player player = clientSession.MyPlayer;
		if (player == null)
			return;

		GameRoom room = player.Room;
		if (room == null)
			return;

		room.Push(room.HandleEquipItem, player, equipPacket);
	}

	public static void C_PongHandler(PacketSession session, IPacket packet)
	{
		ClientSession clientSession = (ClientSession)session;
		clientSession.HandlePong();
	}
}
