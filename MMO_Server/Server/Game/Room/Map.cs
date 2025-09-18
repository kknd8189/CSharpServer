using Google.Protobuf.Protocol;
using ServerCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Server.Game
{
	public struct Pos
	{
		public Pos(int y, int x, int z) { Y = y; X = x; Z = z; }
		public int Y;
		public int X;
        public int Z;

        public static bool operator==(Pos lhs, Pos rhs)
		{
			return lhs.Y == rhs.Y && lhs.X == rhs.X && lhs.Z == rhs.Z;
		}

		public static bool operator!=(Pos lhs, Pos rhs)
		{
			return !(lhs == rhs);
		}

		public override bool Equals(object obj)
		{
			return (Pos)obj == this;
		}

		public override int GetHashCode()
		{
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Y;
                hash = hash * 31 + X;
                hash = hash * 31 + Z;
                return hash;
            }
        }

        public override string ToString()
		{
			return base.ToString();
		}
	}

	public struct PQNode : IComparable<PQNode>
	{
		public int F;
		public int G;
		public int Y;
		public int X;
		public int Z;

		public int CompareTo(PQNode other)
		{
			if (F == other.F)
				return 0;
			return F < other.F ? 1 : -1;
		}
	}

	public struct Vector3Int
	{
		public int x;
		public int y;
		public int z;

		public Vector3Int(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }

		public static Vector3Int up { get { return new Vector3Int(0, 1 , 0); } }
		public static Vector3Int down { get { return new Vector3Int(0, -1 , 0); } }
		public static Vector3Int left { get { return new Vector3Int(-1, 0, 0); } }
		public static Vector3Int right { get { return new Vector3Int(1, 0, 0 ); } }
        public static Vector3Int forward { get { return new Vector3Int(0, 0, 1); } }
        public static Vector3Int backward { get { return new Vector3Int(0, 0, -1); } }

        public static Vector3Int operator +(Vector3Int a, Vector3Int b)
		{
			return new Vector3Int(a.x + b.x, a.y + b.y, a.z + b.z);
		}

		public static Vector3Int operator -(Vector3Int a, Vector3Int b)
		{
			return new Vector3Int(a.x - b.x, a.y - b.y, a.z - b.z);
		}

		public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
		public int sqrMagnitude { get { return (x * x + y * y); } }
		public int cellDistFromZero { get { return Math.Abs(x) + Math.Abs(y); } }
	}

	public class Map
	{
		public int MinX { get; set; }
		public int MaxX { get; set; }
		public int MinY { get; set; }
		public int MaxY { get; set; }
        public int MinZ { get; set; }
        public int MaxZ { get; set; }

        public int SizeX { get { return MaxX - MinX + 1; } }
		public int SizeY { get { return MaxY - MinY + 1; } }
        public int SizeZ { get { return MaxZ - MinZ + 1; } }

        bool[,] _collision;
		GameObject[,] _objects;

		public bool CanGo(Vector3Int cellPos, bool checkObjects = true)
		{
			if (cellPos.x < MinX || cellPos.x > MaxX)
				return false;
			if (cellPos.y < MinY || cellPos.y > MaxY)
				return false;

			int x = cellPos.x - MinX;
			int y = MaxY - cellPos.y;
			return !_collision[y, x] && (!checkObjects || _objects[y, x] == null);
		}

		public GameObject Find(Vector3Int cellPos)
		{
			if (cellPos.x < MinX || cellPos.x > MaxX)
				return null;
			if (cellPos.y < MinY || cellPos.y > MaxY)
				return null;

			int x = cellPos.x - MinX;
			int y = MaxY - cellPos.y;
			return _objects[y, x];
		}

		public bool ApplyLeave(GameObject gameObject)
		{
			if (gameObject.Room == null)
				return false;
			if (gameObject.Room.Map != this)
				return false;

			PositionInfo posInfo = gameObject.PosInfo;
			if (posInfo.PosX < MinX || posInfo.PosX > MaxX)
				return false;
			if (posInfo.PosY < MinY || posInfo.PosY > MaxY)
				return false;

			// Zone
			Zone zone = gameObject.Room.GetZone(gameObject.CellPos);
			zone.Remove(gameObject);

			{
				int x = posInfo.PosX - MinX;
				int y = MaxY - posInfo.PosY;
				if (_objects[y, x] == gameObject)
					_objects[y, x] = null;
			}

			return true;
		}

		public bool ApplyMove(GameObject gameObject, Vector3Int dest, bool checkObjects = true, bool collision = true)
		{
			if (gameObject.Room == null)
				return false;
			if (gameObject.Room.Map != this)
				return false;

			PositionInfo posInfo = gameObject.PosInfo;
			if (CanGo(dest, checkObjects) == false)
				return false;

			if (collision)
			{
				{
					int x = posInfo.PosX - MinX;
					int y = MaxY - posInfo.PosY;
					if (_objects[y, x] == gameObject)
						_objects[y, x] = null;
				}
				{ 
					int x = dest.x - MinX;
					int y = MaxY - dest.y;
					_objects[y, x] = gameObject;
				}
			}

			// Zone
			GameObjectType type = ObjectManager.GetObjectTypeById(gameObject.Id);
			if (type == GameObjectType.Player)
			{
				Player player = (Player)gameObject;
				Zone now = gameObject.Room.GetZone(gameObject.CellPos);
				Zone after = gameObject.Room.GetZone(dest);
				if (now != after)
				{
					now.Players.Remove(player);
					after.Players.Add(player);
				}
			}
			else if (type == GameObjectType.Monster)
			{
				Monster monster = (Monster)gameObject;
				Zone now = gameObject.Room.GetZone(gameObject.CellPos);
				Zone after = gameObject.Room.GetZone(dest);
				if (now != after)
				{
					now.Monsters.Remove(monster);
					after.Monsters.Add(monster);
				}
			}
			else if (type == GameObjectType.Projectile)
			{
				Projectile projectile = (Projectile)gameObject;
				Zone now = gameObject.Room.GetZone(gameObject.CellPos);
				Zone after = gameObject.Room.GetZone(dest);
				if (now != after)
				{
					now.Projectiles.Remove(projectile);
					after.Projectiles.Add(projectile);
				}
			}

			// 실제 좌표 이동
			posInfo.PosX = dest.x;
			posInfo.PosY = dest.y;
			return true;
		}

		public void LoadMap(int mapId, string pathPrefix = "../../../../../Common/MapData")
		{
			string mapName = "Map_" + mapId.ToString("000");

			// Collision 관련 파일
			string text = File.ReadAllText($"{pathPrefix}/{mapName}.txt");
			StringReader reader = new StringReader(text);

			MinX = int.Parse(reader.ReadLine());
			MaxX = int.Parse(reader.ReadLine());
			MinY = int.Parse(reader.ReadLine());
			MaxY = int.Parse(reader.ReadLine());

			int xCount = MaxX - MinX + 1;
			int yCount = MaxY - MinY + 1;
			_collision = new bool[yCount, xCount];
			_objects = new GameObject[yCount, xCount];

			for (int y = 0; y < yCount; y++)
			{
				string line = reader.ReadLine();
				for (int x = 0; x < xCount; x++)
				{
					_collision[y, x] = (line[x] == '1' ? true : false);
				}
			}
		}

        #region A* PathFinding

        // 6방향 이동 (앞/뒤/좌/우/위/아래)
        int[] _deltaY = { 1, -1, 0, 0, 0, 0 };
        int[] _deltaX = { 0, 0, -1, 1, 0, 0 };
        int[] _deltaZ = { 0, 0, 0, 0, 1, -1 };
        int[] _cost = { 10, 10, 10, 10, 10, 10 };

        private int Heuristic(Pos a, Pos b)
        {
            return 10 * (
                (a.Y - b.Y) * (a.Y - b.Y) +
                (a.X - b.X) * (a.X - b.X) +
                (a.Z - b.Z) * (a.Z - b.Z)
            );
        }


        public List<Vector3Int> FindPath(Vector3Int startCellPos, Vector3Int destCellPos, bool checkObjects = true, int maxDist = 10)
		{
			List<Pos> path = new List<Pos>();

			// 점수 매기기
			// F = G + H
			// F = 최종 점수 (작을 수록 좋음, 경로에 따라 달라짐)
			// G = 시작점에서 해당 좌표까지 이동하는데 드는 비용 (작을 수록 좋음, 경로에 따라 달라짐)
			// H = 목적지에서 얼마나 가까운지 (작을 수록 좋음, 고정)

			// (y, x) 이미 방문했는지 여부 (방문 = closed 상태)
			HashSet<Pos> closeList = new HashSet<Pos>(); // CloseList

			// (y, x) 가는 길을 한 번이라도 발견했는지
			// 발견X => MaxValue
			// 발견O => F = G + H
			Dictionary<Pos /*발견된 노드*/, int /*F 값*/ > openList = new Dictionary<Pos, int>(); // OpenList 와 F값
            Dictionary<Pos, Pos> parent = new Dictionary<Pos, Pos>();  // 경로 추적용 부모

            // 오픈리스트에 있는 정보들 중에서, 가장 좋은 후보를 빠르게 뽑아오기 위한 도구
            PriorityQueue<PQNode> pq = new PriorityQueue<PQNode>();

			// CellPos -> ArrayPos
			Pos pos = Cell2Pos(startCellPos);
			Pos dest = Cell2Pos(destCellPos);

			// 시작점 발견 (예약 진행)
			openList.Add(pos, Heuristic(dest, pos));

			pq.Push(new PQNode() { F = Heuristic(dest, pos), G = 0, Y = pos.Y, X = pos.X ,Z = pos.Z});
			parent.Add(pos, pos);

			while (pq.Count > 0)
			{
				// 제일 좋은 후보를 찾는다
				PQNode pqNode = pq.Pop();
				Pos node = new Pos(pqNode.Y, pqNode.X, pqNode.Z);
				// 동일한 좌표를 여러 경로로 찾아서, 더 빠른 경로로 인해서 이미 방문(closed)된 경우 스킵
				if (closeList.Contains(node))
					continue;

				// 방문한다
				closeList.Add(node);

				// 목적지 도착했으면 바로 종료
				if (node.Y == dest.Y && node.X == dest.X && node.Z == dest.Z)
					break;

				// 상하좌우 등 이동할 수 있는 좌표인지 확인해서 예약(open)한다
				for (int i = 0; i < _deltaY.Length; i++)
				{
					Pos next = new Pos(node.Y + _deltaY[i], node.X + _deltaX[i] , node.Z + _deltaZ[i]);

					// 너무 멀면 스킵
					if ( Math.Abs(pos.Y - next.Y) + Math.Abs(pos.X - next.X) + Math.Abs(pos.Z - next.Z) > maxDist)
						continue;

					// 유효 범위를 벗어났으면 스킵
					// 벽으로 막혀서 갈 수 없으면 스킵
					if (next.Y != dest.Y || next.X != dest.X || next.Z != dest.Z)
					{
						if (CanGo(Pos2Cell(next), checkObjects) == false) // CellPos
							continue;
					}

					// 이미 방문한 곳이면 스킵
					if (closeList.Contains(next))
						continue;

					// 비용 계산
					//int g = 0;
					int g = pqNode.G + _cost[i];
					int h = Heuristic(dest, next);
					//int h = 10 * ((dest.Y - next.Y) * (dest.Y - next.Y) + (dest.X - next.X) * (dest.X - next.X));

					// 다른 경로에서 더 빠른 길 이미 찾았으면 스킵

					int value = 0;
					if (openList.TryGetValue(next, out value) == false)
						value = Int32.MaxValue;

					if (value < g + h)
						continue;

					// 예약 진행
					if (openList.TryAdd(next, g + h) == false)
						openList[next] = g + h;

					pq.Push(new PQNode() { F = g + h, G = g, Y = next.Y, X = next.X , Z = next.Z});

					if (parent.TryAdd(next, node) == false)
						parent[next] = node;
				}
			}

			return CalcCellPathFromParent(parent, dest);
		}

		List<Vector3Int> CalcCellPathFromParent(Dictionary<Pos, Pos> parent, Pos dest)
		{
			List<Vector3Int> cells = new List<Vector3Int>();

			if (parent.ContainsKey(dest) == false)
			{
				Pos best = new Pos();
				int bestDist = Int32.MaxValue;

				foreach (Pos pos in parent.Keys)
				{
					int dist = Math.Abs(dest.X - pos.X) + Math.Abs(dest.Y - pos.Y) + Math.Abs(dest.Z - pos.Z);
					// 제일 우수한 후보를 뽑는다
					if (dist < bestDist)
					{
						best = pos;
						bestDist = dist;
					}
				}

				dest = best;
			}

			{
				Pos pos = dest;
				while (parent[pos] != pos)
				{
					cells.Add(Pos2Cell(pos));
					pos = parent[pos];
				}
				cells.Add(Pos2Cell(pos));
				cells.Reverse();
			}

			return cells;
		}

		Pos Cell2Pos(Vector3Int cell)
		{
			// CellPos -> ArrayPos
			return new Pos(MaxY - cell.y, cell.x - MinX, cell.z - MinZ);
		}

		Vector3Int Pos2Cell(Pos pos)
		{
			// ArrayPos -> CellPos
			return new Vector3Int(pos.X + MinX, MaxY - pos.Y, pos.Z + MinZ);
		}

		#endregion
	}

}
