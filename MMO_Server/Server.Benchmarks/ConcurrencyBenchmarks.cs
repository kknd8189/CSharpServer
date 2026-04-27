using BenchmarkDotNet.Attributes;
using Server.Game;

namespace Server.Benchmarks
{
	[MemoryDiagnoser]
	public class ConcurrencyBenchmarks
	{
		private JobSerializer? _serializer;

		[GlobalSetup]
		public void Setup()
		{
			_serializer = new JobSerializer();
		}

		[Benchmark(Baseline = true)]
		public void JobSerializer_SingleThread_1000Push()
		{
			for (int i = 0; i < 1000; i++)
				_serializer!.Push(() => { });
			_serializer!.Flush();
		}

		[Benchmark]
		public void JobSerializer_4Threads_1000Push()
		{
			var barrier = new Barrier(4);
			var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				for (int i = 0; i < 250; i++)
					_serializer!.Push(() => { });
			})).ToArray();
			Task.WaitAll(tasks);
			_serializer!.Flush();
		}

		[Benchmark]
		public void JobSerializer_8Threads_1000Push()
		{
			var barrier = new Barrier(8);
			var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				for (int i = 0; i < 125; i++)
					_serializer!.Push(() => { });
			})).ToArray();
			Task.WaitAll(tasks);
			_serializer!.Flush();
		}

		[Benchmark]
		public void ServerMetrics_SingleThread_10000Increments()
		{
			for (int i = 0; i < 10000; i++)
			{
				ServerMetrics.IncrementPacketsReceived();
				ServerMetrics.IncrementPacketsSent();
			}
			ServerMetrics.ExchangePacketsReceived();
			ServerMetrics.ExchangePacketsSent();
		}

		[Benchmark]
		public void ServerMetrics_4Threads_10000Increments()
		{
			var barrier = new Barrier(4);
			var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				for (int i = 0; i < 2500; i++)
				{
					ServerMetrics.IncrementPacketsReceived();
					ServerMetrics.IncrementPacketsSent();
				}
			})).ToArray();
			Task.WaitAll(tasks);
			ServerMetrics.ExchangePacketsReceived();
			ServerMetrics.ExchangePacketsSent();
		}

		[Benchmark]
		public void SessionManager_SingleThread_100Generate()
		{
			var manager = new SessionManager();
			for (int i = 0; i < 100; i++)
				manager.Generate();
		}

		[Benchmark]
		public void SessionManager_4Threads_100Generate()
		{
			var manager = new SessionManager();
			var barrier = new Barrier(4);
			var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
			{
				barrier.SignalAndWait();
				for (int i = 0; i < 25; i++)
					manager.Generate();
			})).ToArray();
			Task.WaitAll(tasks);
		}
	}
}
