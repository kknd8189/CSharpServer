using Server.Game;

namespace Server.Tests
{
	public class Vector3IntTests
	{
		[Fact]
		public void Addition_ReturnsCorrectResult()
		{
			var a = new Vector3Int(1, 2, 3);
			var b = new Vector3Int(4, 5, 6);

			var result = a + b;

			Assert.Equal(5, result.x);
			Assert.Equal(7, result.y);
			Assert.Equal(9, result.z);
		}

		[Fact]
		public void Subtraction_ReturnsCorrectResult()
		{
			var a = new Vector3Int(5, 10, 15);
			var b = new Vector3Int(3, 4, 5);

			var result = a - b;

			Assert.Equal(2, result.x);
			Assert.Equal(6, result.y);
			Assert.Equal(10, result.z);
		}

		[Fact]
		public void Subtraction_WithNegativeResult()
		{
			var a = new Vector3Int(1, 1, 1);
			var b = new Vector3Int(5, 5, 5);

			var result = a - b;

			Assert.Equal(-4, result.x);
			Assert.Equal(-4, result.y);
			Assert.Equal(-4, result.z);
		}

		[Fact]
		public void SqrMagnitude_ReturnsCorrectValue()
		{
			var v = new Vector3Int(3, 4, 0);

			Assert.Equal(25, v.sqrMagnitude);
		}

		[Fact]
		public void Magnitude_ReturnsCorrectValue()
		{
			var v = new Vector3Int(3, 4, 0);

			Assert.Equal(5.0f, v.magnitude);
		}

		[Fact]
		public void CellDistFromZero_ReturnsManhattanDistance()
		{
			var v = new Vector3Int(-3, 4, -2);

			Assert.Equal(9, v.cellDistFromZero);
		}

		[Fact]
		public void CellDistFromZero_ZeroVector_ReturnsZero()
		{
			var v = new Vector3Int(0, 0, 0);

			Assert.Equal(0, v.cellDistFromZero);
		}

		[Theory]
		[InlineData(1, 0, 0, 1)]
		[InlineData(0, 0, 1, 1)]
		[InlineData(1, 1, 1, 3)]
		[InlineData(-2, -3, -4, 9)]
		public void CellDistFromZero_VariousCases(int x, int y, int z, int expected)
		{
			var v = new Vector3Int(x, y, z);

			Assert.Equal(expected, v.cellDistFromZero);
		}

		[Fact]
		public void DirectionConstants_AreCorrect()
		{
			Assert.Equal(new Vector3Int(-1, 0, 0).x, Vector3Int.left.x);
			Assert.Equal(new Vector3Int(1, 0, 0).x, Vector3Int.right.x);
			Assert.Equal(new Vector3Int(0, 0, 1).z, Vector3Int.forward.z);
			Assert.Equal(new Vector3Int(0, 0, -1).z, Vector3Int.backward.z);
		}
	}
}
