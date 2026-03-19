using Server.Game;

namespace Server.Tests
{
	public class PosTests
	{
		[Fact]
		public void Equality_SameValues_ReturnsTrue()
		{
			var a = new Pos(1, 2, 3);
			var b = new Pos(1, 2, 3);

			Assert.True(a == b);
			Assert.Equal(a, b);
		}

		[Fact]
		public void Equality_DifferentValues_ReturnsFalse()
		{
			var a = new Pos(1, 2, 3);
			var b = new Pos(4, 5, 6);

			Assert.True(a != b);
			Assert.NotEqual(a, b);
		}

		[Fact]
		public void GetHashCode_SameValues_SameHash()
		{
			var a = new Pos(10, 20, 30);
			var b = new Pos(10, 20, 30);

			Assert.Equal(a.GetHashCode(), b.GetHashCode());
		}

		[Fact]
		public void GetHashCode_DifferentValues_DifferentHash()
		{
			var a = new Pos(1, 2, 3);
			var b = new Pos(3, 2, 1);

			// Not guaranteed but very likely for these values
			Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
		}

		[Fact]
		public void Pos_CanBeUsedAsDictionaryKey()
		{
			var dict = new Dictionary<Pos, int>();
			var pos = new Pos(5, 10, 15);

			dict[pos] = 42;

			Assert.Equal(42, dict[new Pos(5, 10, 15)]);
		}

		[Fact]
		public void Pos_CanBeUsedInHashSet()
		{
			var set = new HashSet<Pos>();

			set.Add(new Pos(1, 2, 3));
			set.Add(new Pos(1, 2, 3)); // duplicate

			Assert.Single(set);
		}
	}
}
