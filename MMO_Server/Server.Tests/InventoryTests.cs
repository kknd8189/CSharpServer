using Protocol;
using Server.Game;

namespace Server.Tests
{
	public class InventoryTests
	{
		private Item CreateTestItem(int dbId, int slot = 0)
		{
			var item = new Item(ItemType.Weapon);
			item.ItemDbId = dbId;
			item.Slot = slot;
			return item;
		}

		[Fact]
		public void Add_And_Get_ReturnsItem()
		{
			var inventory = new Inventory();
			var item = CreateTestItem(1);

			inventory.Add(item);

			Assert.NotNull(inventory.Get(1));
			Assert.Equal(1, inventory.Get(1).ItemDbId);
		}

		[Fact]
		public void Get_NotFound_ReturnsNull()
		{
			var inventory = new Inventory();

			Assert.Null(inventory.Get(999));
		}

		[Fact]
		public void Find_WithCondition_ReturnsFirstMatch()
		{
			var inventory = new Inventory();
			inventory.Add(CreateTestItem(1, slot: 0));
			inventory.Add(CreateTestItem(2, slot: 1));
			inventory.Add(CreateTestItem(3, slot: 2));

			var found = inventory.Find(i => i.Slot == 1);

			Assert.NotNull(found);
			Assert.Equal(2, found.ItemDbId);
		}

		[Fact]
		public void Find_NoMatch_ReturnsNull()
		{
			var inventory = new Inventory();
			inventory.Add(CreateTestItem(1));

			var found = inventory.Find(i => i.Slot == 99);

			Assert.Null(found);
		}

		[Fact]
		public void GetEmptySlot_EmptyInventory_ReturnsZero()
		{
			var inventory = new Inventory();

			Assert.Equal(0, inventory.GetEmptySlot());
		}

		[Fact]
		public void GetEmptySlot_SomeFilled_ReturnsFirstEmpty()
		{
			var inventory = new Inventory();
			inventory.Add(CreateTestItem(1, slot: 0));
			inventory.Add(CreateTestItem(2, slot: 1));

			Assert.Equal(2, inventory.GetEmptySlot());
		}

		[Fact]
		public void GetEmptySlot_FullInventory_ReturnsNull()
		{
			var inventory = new Inventory();
			for (int i = 0; i < 20; i++)
			{
				inventory.Add(CreateTestItem(i + 1, slot: i));
			}

			Assert.Null(inventory.GetEmptySlot());
		}

		[Fact]
		public void GetEmptySlot_GapInMiddle_ReturnsGap()
		{
			var inventory = new Inventory();
			inventory.Add(CreateTestItem(1, slot: 0));
			// slot 1 비어있음
			inventory.Add(CreateTestItem(3, slot: 2));

			Assert.Equal(1, inventory.GetEmptySlot());
		}

		[Fact]
		public void Items_Count_AfterMultipleAdds()
		{
			var inventory = new Inventory();
			inventory.Add(CreateTestItem(1, slot: 0));
			inventory.Add(CreateTestItem(2, slot: 1));
			inventory.Add(CreateTestItem(3, slot: 2));

			Assert.Equal(3, inventory.Items.Count);
		}
	}
}
