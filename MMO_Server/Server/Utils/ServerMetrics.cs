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
