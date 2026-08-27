using System.Diagnostics;
using System.Threading;

namespace Server
{
	public static class ServerMetrics
	{
		private static long _packetsReceived;
		private static long _packetsSent;

		// Tick 측정: Stopwatch ticks 단위로 누적 (us 변환은 표출 시점에).
		// 빈 tick(=0) 은 idle 로 분리 카운트해서 평균이 0 으로 깔리는 문제 회피.
		private static long _tickTotalSw;
		private static long _tickMaxSw;
		private static long _tickWorkCount;
		private static long _tickIdleCount;

		public static void IncrementPacketsReceived() => Interlocked.Increment(ref _packetsReceived);
		public static void IncrementPacketsSent() => Interlocked.Increment(ref _packetsSent);

		// 검증 거부 카운터. 개별 위반의 "누가/언제"는 로그가 담당하고,
		// 여기서는 "비율"만 본다. 거부율이 갑자기 튀면 둘 중 하나다 —
		// 핵이 유행했거나, 우리 임계값이 잘못돼 정상 유저를 끊고 있거나.
		// 후자가 훨씬 흔하고 더 치명적이라 배포 직후 반드시 확인해야 한다.
		private static long _rejectedSkillCooldown;
		private static long _rejectedMoveSpeed;
		private static long _rejectedTeleport;

		public static void IncrementValidationRejected(Game.GameRoom.ViolationKind kind)
		{
			switch (kind)
			{
				case Game.GameRoom.ViolationKind.SkillCooldown:
					Interlocked.Increment(ref _rejectedSkillCooldown);
					break;
				case Game.GameRoom.ViolationKind.MoveSpeed:
					Interlocked.Increment(ref _rejectedMoveSpeed);
					break;
				case Game.GameRoom.ViolationKind.Teleport:
					Interlocked.Increment(ref _rejectedTeleport);
					break;
			}
		}

		public static (long SkillCooldown, long MoveSpeed, long Teleport) ExchangeValidationRejected()
		{
			return (Interlocked.Exchange(ref _rejectedSkillCooldown, 0),
					Interlocked.Exchange(ref _rejectedMoveSpeed, 0),
					Interlocked.Exchange(ref _rejectedTeleport, 0));
		}

		public static void RecordTick(long elapsedSwTicks)
		{
			if (elapsedSwTicks <= 0)
			{
				Interlocked.Increment(ref _tickIdleCount);
				return;
			}
			Interlocked.Add(ref _tickTotalSw, elapsedSwTicks);
			Interlocked.Increment(ref _tickWorkCount);
			InterlockedMax(ref _tickMaxSw, elapsedSwTicks);
		}

		public static long ExchangePacketsReceived() => Interlocked.Exchange(ref _packetsReceived, 0);
		public static long ExchangePacketsSent() => Interlocked.Exchange(ref _packetsSent, 0);

		public static (long AvgUs, long MaxUs, long WorkCount, long IdleCount) ExchangeTickStats()
		{
			long total = Interlocked.Exchange(ref _tickTotalSw, 0);
			long max = Interlocked.Exchange(ref _tickMaxSw, 0);
			long work = Interlocked.Exchange(ref _tickWorkCount, 0);
			long idle = Interlocked.Exchange(ref _tickIdleCount, 0);

			long freq = Stopwatch.Frequency;
			long avgUs = work > 0 ? (total * 1_000_000L / freq) / work : 0;
			long maxUs = max * 1_000_000L / freq;
			return (avgUs, maxUs, work, idle);
		}

		// CAS 루프로 atomic max.
		private static void InterlockedMax(ref long location, long value)
		{
			long current;
			do
			{
				current = Volatile.Read(ref location);
				if (value <= current) return;
			} while (Interlocked.CompareExchange(ref location, value, current) != current);
		}
	}
}
