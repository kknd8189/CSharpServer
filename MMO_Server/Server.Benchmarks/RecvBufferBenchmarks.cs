using BenchmarkDotNet.Attributes;
using ServerCore;

namespace Server.Benchmarks
{
	[MemoryDiagnoser]
	public class RecvBufferBenchmarks
	{
		[Params(1024, 4096, 65536)]
		public int BufferSize;

		[Benchmark]
		public void CreateAndDispose()
		{
			using var buffer = new RecvBufferSpan(BufferSize);
		}

		[Benchmark]
		public void WriteAndRead_Small()
		{
			using var buffer = new RecvBufferSpan(BufferSize);
			buffer.OnWrite(64);
			buffer.OnRead(64);
		}

		[Benchmark]
		public void WriteReadClean_Cycle()
		{
			using var buffer = new RecvBufferSpan(BufferSize);
			for (int i = 0; i < 100; i++)
			{
				buffer.OnWrite(64);
				buffer.OnRead(32);
				buffer.Clean();
			}
		}

		[Benchmark]
		public void Clean_WithRemainingData()
		{
			using var buffer = new RecvBufferSpan(BufferSize);
			buffer.OnWrite(512);
			buffer.OnRead(256);
			buffer.Clean();
		}
	}
}
