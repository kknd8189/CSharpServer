using Protocol;
using Server.Game;

namespace Server.Tests
{
	public class GameObjectTests
	{
		[Fact]
		public void Hp_ClampedToMaxHp()
		{
			var obj = new GameObject();
			obj.Stat.MaxHp = 100;
			obj.Hp = 200;

			Assert.Equal(100, obj.Hp);
		}

		[Fact]
		public void Hp_ClampedToZero()
		{
			var obj = new GameObject();
			obj.Stat.MaxHp = 100;
			obj.Hp = -50;

			Assert.Equal(0, obj.Hp);
		}

		[Fact]
		public void Hp_SetNormalValue()
		{
			var obj = new GameObject();
			obj.Stat.MaxHp = 100;
			obj.Hp = 50;

			Assert.Equal(50, obj.Hp);
		}

		[Fact]
		public void CellPos_SetAndGet()
		{
			var obj = new GameObject();
			obj.CellPos = new Vector3Int(10, 20, 30);

			Assert.Equal(10, obj.CellPos.x);
			Assert.Equal(20, obj.CellPos.y);
			Assert.Equal(30, obj.CellPos.z);
		}

		[Fact]
		public void GetFrontCellPos_Forward()
		{
			var obj = new GameObject();
			obj.CellPos = new Vector3Int(5, 0, 5);

			var front = obj.GetFrontCellPos(MoveDir.Forward);

			Assert.Equal(5, front.x);
			Assert.Equal(0, front.y);
			Assert.Equal(6, front.z);
		}

		[Fact]
		public void GetFrontCellPos_Backward()
		{
			var obj = new GameObject();
			obj.CellPos = new Vector3Int(5, 0, 5);

			var front = obj.GetFrontCellPos(MoveDir.Backward);

			Assert.Equal(5, front.x);
			Assert.Equal(0, front.y);
			Assert.Equal(4, front.z);
		}

		[Fact]
		public void GetFrontCellPos_Left()
		{
			var obj = new GameObject();
			obj.CellPos = new Vector3Int(5, 0, 5);

			var front = obj.GetFrontCellPos(MoveDir.Left);

			Assert.Equal(4, front.x);
			Assert.Equal(0, front.y);
			Assert.Equal(5, front.z);
		}

		[Fact]
		public void GetFrontCellPos_Right()
		{
			var obj = new GameObject();
			obj.CellPos = new Vector3Int(5, 0, 5);

			var front = obj.GetFrontCellPos(MoveDir.Right);

			Assert.Equal(6, front.x);
			Assert.Equal(0, front.y);
			Assert.Equal(5, front.z);
		}

		[Theory]
		[InlineData(1, 0, 0, MoveDir.Right)]
		[InlineData(-1, 0, 0, MoveDir.Left)]
		[InlineData(0, 1, 0, MoveDir.Up)]
		[InlineData(0, -1, 0, MoveDir.Down)]
		[InlineData(0, 0, 0, MoveDir.Down)]  // zero vector defaults to Down
		public void GetDirFromVec_ReturnsCorrectDirection(int x, int y, int z, MoveDir expected)
		{
			var dir = new Vector3Int(x, y, z);

			Assert.Equal(expected, GameObject.GetDirFromVec(dir));
		}

		[Fact]
		public void GetDirFromVec_XPrioritizedOverY()
		{
			// x > 0 이면 y 값과 관계없이 Right
			var dir = new Vector3Int(1, 1, 0);

			Assert.Equal(MoveDir.Right, GameObject.GetDirFromVec(dir));
		}
	}
}
