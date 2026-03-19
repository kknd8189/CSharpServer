using BenchmarkDotNet.Attributes;
using Server.Game;

namespace Server.Benchmarks
{
	[MemoryDiagnoser]
	public class JobSerializerBenchmarks
	{
		private JobSerializer _serializer;

		[GlobalSetup]
		public void Setup()
		{
			_serializer = new JobSerializer();
		}

		[Benchmark]
		public void PushAndFlush_1Job()
		{
			_serializer.Push(() => { });
			_serializer.Flush();
		}

		[Benchmark]
		public void PushAndFlush_100Jobs()
		{
			for (int i = 0; i < 100; i++)
				_serializer.Push(() => { });
			_serializer.Flush();
		}

		[Benchmark]
		public void PushAndFlush_1000Jobs()
		{
			for (int i = 0; i < 1000; i++)
				_serializer.Push(() => { });
			_serializer.Flush();
		}

		[Benchmark]
		public void PushWithParams_AndFlush()
		{
			for (int i = 0; i < 100; i++)
				_serializer.Push((int a, int b) => { var _ = a + b; }, 10, 20);
			_serializer.Flush();
		}
	}
}
