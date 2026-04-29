using System.Threading;

namespace Server
{
	public static class ServerMetrics
	{
		private static long _packetsReceived;
		private static long _packetsSent;

		// Tick 측정: us 단위. Environment.TickCount64 는 ~15.6ms 정밀도라 빠른 tick 들이 0 으로 깔림.
		// Stopwatch 기반 us 누적 + 최대 + 카운트 → 5초 윈도우의 평균/최대 산출.
		private static long _tickTotalUs;
		private static long _tickMaxUs;
		private static long _tickCount;

		public static void IncrementPacketsReceived() => Interlocked.Increment(ref _packetsReceived);
		public static void IncrementPacketsSent() => Interlocked.Increment(ref _packetsSent);

		public static void RecordTick(long us)
		{
			Interlocked.Add(ref _tickTotalUs, us);
			Interlocked.Increment(ref _tickCount);
			InterlockedMax(ref _tickMaxUs, us);
		}

		public static long ExchangePacketsReceived() => Interlocked.Exchange(ref _packetsReceived, 0);
		public static long ExchangePacketsSent() => Interlocked.Exchange(ref _packetsSent, 0);

		// 평균/최대/카운트를 한 번에 스냅 후 리셋. 윈도우 사이 race 는 무시 수준.
		public static (long AvgUs, long MaxUs, long Count) ExchangeTickStats()
		{
			long total = Interlocked.Exchange(ref _tickTotalUs, 0);
			long max = Interlocked.Exchange(ref _tickMaxUs, 0);
			long count = Interlocked.Exchange(ref _tickCount, 0);
			long avg = count > 0 ? total / count : 0;
			return (avg, max, count);
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
