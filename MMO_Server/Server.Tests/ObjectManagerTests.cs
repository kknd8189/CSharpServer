using Protocol;
using Server.Game;

namespace Server.Tests
{
	public class ObjectManagerTests
	{
		[Theory]
		[InlineData(GameObjectType.Player)]
		[InlineData(GameObjectType.Monster)]
		[InlineData(GameObjectType.Projectile)]
		public void GetObjectTypeById_EncodesAndDecodes(GameObjectType type)
		{
			// ID 생성 로직: ((int)type << 24) | counter
			int id = ((int)type << 24) | 42;

			var result = ObjectManager.GetObjectTypeById(id);

			Assert.Equal(type, result);
		}

		[Fact]
		public void GetObjectTypeById_ZeroId_ReturnsNone()
		{
			int id = 0;

			Assert.Equal(GameObjectType.None, ObjectManager.GetObjectTypeById(id));
		}

		[Fact]
		public void GetObjectTypeById_PreservesLow24Bits()
		{
			int counter = 0xFFFFFF; // max 24-bit value
			int id = ((int)GameObjectType.Player << 24) | counter;

			Assert.Equal(GameObjectType.Player, ObjectManager.GetObjectTypeById(id));
		}

		[Fact]
		public void GetObjectTypeById_DifferentCounters_SameType()
		{
			int id1 = ((int)GameObjectType.Monster << 24) | 0;
			int id2 = ((int)GameObjectType.Monster << 24) | 9999;

			Assert.Equal(ObjectManager.GetObjectTypeById(id1), ObjectManager.GetObjectTypeById(id2));
		}
	}
}
