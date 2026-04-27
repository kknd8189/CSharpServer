using BenchmarkDotNet.Attributes;
using Protocol;
using Server.Game;

namespace Server.Benchmarks
{
	[MemoryDiagnoser]
	public class GameObjectBenchmarks
	{
		private GameObject? _obj;

		[GlobalSetup]
		public void Setup()
		{
			_obj = new GameObject();
			_obj.Stat.MaxHp = 1000;
			_obj.Hp = 1000;
			_obj.CellPos = new Vector3Int(50, 0, 50);
			_obj.Dir = MoveDir.Forward;
		}

		[Benchmark]
		public int HpClamp_Normal()
		{
			_obj!.Hp = 500;
			return _obj.Hp;
		}

		[Benchmark]
		public int HpClamp_Overflow()
		{
			_obj!.Hp = 9999;
			return _obj.Hp;
		}

		[Benchmark]
		public int HpClamp_Underflow()
		{
			_obj!.Hp = -100;
			return _obj.Hp;
		}

		[Benchmark]
		public Vector3Int GetFrontCellPos_Forward() => _obj!.GetFrontCellPos(MoveDir.Forward);

		[Benchmark]
		public Vector3Int GetFrontCellPos_Left() => _obj!.GetFrontCellPos(MoveDir.Left);

		[Benchmark]
		public MoveDir GetDirFromVec() => GameObject.GetDirFromVec(new Vector3Int(1, -1, 0));

		[Benchmark]
		public Vector3Int CellPos_SetAndGet()
		{
			_obj!.CellPos = new Vector3Int(100, 0, 100);
			return _obj.CellPos;
		}
	}
}
