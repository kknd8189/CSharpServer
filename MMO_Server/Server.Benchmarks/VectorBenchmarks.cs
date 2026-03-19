using BenchmarkDotNet.Attributes;
using Server.Game;

namespace Server.Benchmarks
{
	[MemoryDiagnoser]
	public class VectorBenchmarks
	{
		private Vector3Int _a;
		private Vector3Int _b;

		[GlobalSetup]
		public void Setup()
		{
			_a = new Vector3Int(10, 20, 30);
			_b = new Vector3Int(5, 15, 25);
		}

		[Benchmark]
		public Vector3Int Addition() => _a + _b;

		[Benchmark]
		public Vector3Int Subtraction() => _a - _b;

		[Benchmark]
		public int SqrMagnitude() => _a.sqrMagnitude;

		[Benchmark]
		public float Magnitude() => _a.magnitude;

		[Benchmark]
		public int ManhattanDistance() => (_a - _b).cellDistFromZero;
	}
}
