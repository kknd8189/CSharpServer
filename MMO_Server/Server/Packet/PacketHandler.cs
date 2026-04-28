using Server;
using Server.Game;
using ServerCore;
using Server.DB.LogDB;
using Protocol;
using Serilog;
using System;
using System.Threading.Tasks;

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

		// IOCP 스레드 즉시 반환. async 작업은 ThreadPool에서.
		// (sync로 await하면 Redis 호출 동안 IOCP 점유 → AcceptAsync 적체 → ConnectionRefused 캐스케이드)
		_ = ProcessLoginAsync(clientSession, loginPacket);
	}

	private static async Task ProcessLoginAsync(ClientSession clientSession, C_Login loginPacket)
	{
		try
		{
			bool ok = await clientSession.HandleLoginAsync(loginPacket);

			// 성공: 확정된 AccountDbId / 실패: 클라가 보낸 AccountID로 로그 (잘못된 토큰 시도 추적)
			int accountId = ok ? clientSession.AccountDbId : loginPacket.AccountID;
			LogHelper.LogLogin(accountId, ok, clientSession.GetIpAddress());
		}
		catch (Exception ex)
		{
			Log.Error(ex, "ProcessLoginAsync error");
		}
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
