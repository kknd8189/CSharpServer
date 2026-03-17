using System.Threading;

namespace Server
{
	public static class ServerMetrics
	{
		private static long _packetsReceived;
		private static long _packetsSent;
		private static long _lastTickDurationMs;

		public static void IncrementPacketsReceived() => Interlocked.Increment(ref _packetsReceived);
		public static void IncrementPacketsSent() => Interlocked.Increment(ref _packetsSent);
		public static void SetTickDuration(long ms) => Volatile.Write(ref _lastTickDurationMs, ms);

		public static long ExchangePacketsReceived() => Interlocked.Exchange(ref _packetsReceived, 0);
		public static long ExchangePacketsSent() => Interlocked.Exchange(ref _packetsSent, 0);
		public static long GetTickDuration() => Volatile.Read(ref _lastTickDurationMs);
	}
}
