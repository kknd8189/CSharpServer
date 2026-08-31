using DummyClient;
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


	private readonly Random _random = new Random();
	private readonly object _randomLock = new object();

	// 밀집 모드에서 벽에 갇히는 걸 감지한다.
	// 서버가 이동을 거부하면 S_Move 로 좌표가 되돌려지는데, 그대로 두면 같은 방향으로
	// 계속 벽을 밀기만 해서 그 더미는 사실상 부하에서 빠져버린다.
	// 틱 시작 시점의 좌표를 직전 틱과 비교해 "진전이 없으면" 잠시 랜덤으로 우회한다.
	// (낙관적으로 갱신한 값이 아니라 틱 시작 값을 봐야 서버의 거부가 잡힌다)
	private int _lastPosX = int.MinValue;
	private int _lastPosZ = int.MinValue;
	private int _stuckCount;
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
		// 틱 시작 좌표가 직전 틱과 같으면 서버가 이동을 거부한 것이다.
		if (PosX == _lastPosX && PosZ == _lastPosZ)
			_stuckCount++;
		else
			_stuckCount = 0;

		_lastPosX = PosX;
		_lastPosZ = PosZ;

		MoveDir dir = PickDir();

		switch (dir)
		{
			case MoveDir.Left: PosX -= 1; break;
			case MoveDir.Right: PosX += 1; break;
			case MoveDir.Forward: PosZ += 1; break;
			case MoveDir.Backward: PosZ -= 1; break;
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
		// 서버 HandleSkill 은 State == Idle 일 때만 시전을 허용한다.
		// 그런데 OnMoveTick 이 매번 State=Moving 을 보내므로, 이걸 안 하면
		// 더미의 스킬이 100% 상태 게이트에서 거부된다 —
		// 실제로 그 상태에서는 몬스터가 한 마리도 죽지 않아 드랍 경로가 통째로 잠들어 있었다.
		// 진짜 클라이언트도 공격하려면 멈춘다. 같은 순서로 보낸다.
		Send(new C_Move
		{
			PosInfo = new PositionInfo
			{
				State = CreatureState.Idle,
				MoveDir = MoveDir.Right,
				PosX = PosX,
				PosY = PosY,
				PosZ = PosZ,
			}
		});

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

	// 이 맵은 단일 Y 평면이다 (Map 로더가 MaxY = MinY 로 잡는다).
	// 예전엔 Up/Down 으로 PosY 를 움직였는데, 서버 Map.CanGo 가 y 경계 밖이라
	// 100% 거부했다. 방향 4개 중 2개가 y 였으니 이동 패킷의 절반이 버려진 셈.
	// 서버가 실제로 쓰는 축은 x(Left/Right) 와 z(Forward/Backward) 다.
	private MoveDir RandomDir()
	{
		lock (_randomLock)
			return _random.Next(0, 2) == 0
				? (_random.Next(0, 2) == 0 ? MoveDir.Left : MoveDir.Right)
				: (_random.Next(0, 2) == 0 ? MoveDir.Forward : MoveDir.Backward);
	}

	// 밀집 모드가 꺼져 있으면 기존과 완전히 동일하게 동작한다.
	// 켜져 있으면 목표 반경 안으로 걸어 들어가고, 들어간 뒤에는 그 안에서 랜덤 워크한다.
	//
	// 중요한 건 "이동 패킷 수는 그대로"라는 점이다. 전송률·간격·검증 통과 여부를 건드리지
	// 않고 방향만 바꾼다. 그래야 흩어짐 대비 밀집의 차이를 팬아웃 하나로 설명할 수 있다.
	private MoveDir PickDir()
	{
		if (LoadProfile.Cluster == false)
			return RandomDir();

		int dx = LoadProfile.CenterX - PosX;
		int dz = LoadProfile.CenterZ - PosZ;
		int r = LoadProfile.Radius;

		// 이미 반경 안이면 그 안에서 랜덤 워크.
		// 멈춰 세우지 않는 이유: 이동 패킷이 끊기면 브로드캐스트가 사라져
		// 재려던 팬아웃 부하 자체가 없어진다. 뭉친 채로 계속 움직여야 한다.
		if (Math.Abs(dx) <= r && Math.Abs(dz) <= r)
			return RandomDir();

		// 벽에 막혀 제자리면 잠시 랜덤으로 우회한다.
		if (_stuckCount >= 3)
			return RandomDir();

		// 남은 거리가 큰 축을 우선하되, 가끔 다른 축으로 틀어 벽을 돌아간다.
		bool useX = Math.Abs(dx) > Math.Abs(dz);
		lock (_randomLock)
		{
			if (_random.Next(0, 5) == 0)
				useX = !useX;
		}

		if (useX && dx != 0)
			return dx > 0 ? MoveDir.Right : MoveDir.Left;
		if (dz != 0)
			return dz > 0 ? MoveDir.Forward : MoveDir.Backward;
		return dx > 0 ? MoveDir.Right : MoveDir.Left;
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
