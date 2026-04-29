using Protocol;
using ServerCore;
using System;
using System.Net;
using System.Timers;

public class ServerSession : PacketSession
{
	public int DummyId { get; set; }
	public int AccountId { get; set; }
	public string Token { get; set; }

	public int MyPlayerId { get; set; }
	public int PosX;
	public int PosY;
	public int PosZ;

	private const int MaxPacketSize = 10 * 1024;

	private readonly Random _random = new Random();
	private readonly object _randomLock = new object();
	private System.Timers.Timer _moveTimer;
	private System.Timers.Timer _skillTimer;

	public void Send(IPacket packet)
	{
		Span<byte> span = SendBufferSpanHelper.Open(MaxPacketSize);
		if (span.IsEmpty) return;

		packet.Write(span, out ushort size);
		ArraySegment<byte> pendingBuffer = SendBufferSpanHelper.Close(size);
		Send(pendingBuffer);
	}

	public override void OnConnected(EndPoint endPoint)
	{
	}

	public override void OnDisconnected(EndPoint endPoint)
	{
		StopSimulation();
	}

	public override void OnRecvPacketSpan(ReadOnlySpan<byte> buffer)
	{
		PacketManager.Instance.OnRecvPacketSpan(this, buffer);
	}

	public override void OnSend(int numOfBytes)
	{
	}

	// 게임 진입 직후 PacketHandler.S_EnterGameHandler 에서 호출.
	// 더미마다 독립된 Timer 두 개로 이동/스킬 부하 발생.
	public void StartSimulation()
	{
		if (_moveTimer != null) return; // 중복 진입 방지

		_moveTimer = new System.Timers.Timer { AutoReset = false };
		_moveTimer.Elapsed += OnMoveTick;
		_moveTimer.Interval = NextMoveInterval();
		_moveTimer.Start();

		_skillTimer = new System.Timers.Timer { AutoReset = false };
		_skillTimer.Elapsed += OnSkillTick;
		_skillTimer.Interval = NextSkillInterval();
		_skillTimer.Start();
	}

	private void StopSimulation()
	{
		var m = _moveTimer; _moveTimer = null;
		m?.Stop();
		m?.Dispose();

		var s = _skillTimer; _skillTimer = null;
		s?.Stop();
		s?.Dispose();
	}

	private void OnMoveTick(object sender, ElapsedEventArgs e)
	{
		// 4방향(Up/Down/Left/Right). Forward/Backward 는 Z축이라 제외.
		MoveDir dir;
		lock (_randomLock) dir = (MoveDir)_random.Next(0, 4);

		switch (dir)
		{
			case MoveDir.Up: PosY += 1; break;
			case MoveDir.Down: PosY -= 1; break;
			case MoveDir.Left: PosX -= 1; break;
			case MoveDir.Right: PosX += 1; break;
		}

		C_Move movePacket = new C_Move
		{
			PosInfo = new PositionInfo
			{
				State = CreatureState.Moving,
				MoveDir = dir,
				PosX = PosX,
				PosY = PosY,
				PosZ = PosZ,
			}
		};
		Send(movePacket);

		// 종료된 후 늦게 들어온 콜백 보호. 살아있으면 다음 tick 예약.
		var t = _moveTimer;
		if (t == null) return;
		try
		{
			t.Interval = NextMoveInterval();
			t.Start();
		}
		catch (ObjectDisposedException) { }
	}

	private void OnSkillTick(object sender, ElapsedEventArgs e)
	{
		C_Skill skillPacket = new C_Skill
		{
			Info = new SkillInfo { SkillId = 1 }
		};
		Send(skillPacket);

		var t = _skillTimer;
		if (t == null) return;
		try
		{
			t.Interval = NextSkillInterval();
			t.Start();
		}
		catch (ObjectDisposedException) { }
	}

	private double NextMoveInterval()
	{
		lock (_randomLock) return _random.Next(200, 501);
	}

	private double NextSkillInterval()
	{
		lock (_randomLock) return _random.Next(1000, 3001);
	}
}
