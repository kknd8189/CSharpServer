using BenchmarkDotNet.Attributes;
using Google.Protobuf.Protocol;
using Server.Game;

namespace Server.Benchmarks
{
	[MemoryDiagnoser]
	public class InventoryBenchmarks
	{
		private Inventory _emptyInventory;
		private Inventory _halfFullInventory;
		private Inventory _fullInventory;

		[GlobalSetup]
		public void Setup()
		{
			_emptyInventory = new Inventory();

			_halfFullInventory = new Inventory();
			for (int i = 0; i < 10; i++)
			{
				var item = new Item(ItemType.Weapon) { ItemDbId = i + 1, Slot = i };
				_halfFullInventory.Add(item);
			}

			_fullInventory = new Inventory();
			for (int i = 0; i < 20; i++)
			{
				var item = new Item(ItemType.Weapon) { ItemDbId = i + 1, Slot = i };
				_fullInventory.Add(item);
			}
		}

		[Benchmark]
		public int? GetEmptySlot_Empty() => _emptyInventory.GetEmptySlot();

		[Benchmark]
		public int? GetEmptySlot_HalfFull() => _halfFullInventory.GetEmptySlot();

		[Benchmark]
		public int? GetEmptySlot_Full() => _fullInventory.GetEmptySlot();

		[Benchmark]
		public Item Get_Existing() => _fullInventory.Get(10);

		[Benchmark]
		public Item Get_NonExisting() => _fullInventory.Get(999);

		[Benchmark]
		public Item Find_BySlot() => _fullInventory.Find(i => i.Slot == 15);
	}
}
