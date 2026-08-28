using Protocol;
using ServerCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Server.Game
{
	public struct Pos : IEquatable<Pos>
    {
		public Pos(int x, int y, int z) { X = x; Y = y; Z = z; }
		readonly public int Y;
        readonly public int X;
        readonly public int Z;

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
            return (obj is Pos other) && this == other;
        }

        public bool Equals(Pos other)
        {
            return this == other;
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

		//public static Vector3Int up { get { return new Vector3Int(0, 1 , 0); } }
		//public static Vector3Int down { get { return new Vector3Int(0, -1 , 0); } }
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
		public int sqrMagnitude { get { return (x * x + y * y + z * z); } }
		public int cellDistFromZero { get { return Math.Abs(x) + Math.Abs(y) + Math.Abs(z); } }

		// 기본 ToString 은 타입명("Server.Game.Vector3Int")만 찍혀서
		// 위반 로그에 좌표가 안 남는다. 로그로 추적하려면 값이 보여야 한다.
		public override string ToString() { return $"({x},{y},{z})"; }
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

        bool[,,] _collision;
		GameObject[,,] _objects;

		public bool CanGo(Vector3Int cellPos, bool checkObjects = true)
		{
            if (cellPos.x < MinX || cellPos.x > MaxX)
                return false;
            if (cellPos.y < MinY || cellPos.y > MaxY)
                return false;
            if (cellPos.z < MinZ || cellPos.z > MaxZ)
                return false;

            int x = cellPos.x - MinX;
            int y = cellPos.y - MinY;
            int z = cellPos.z - MinZ;

            return !_collision[x, y , z] && (!checkObjects || _objects[x, y, z] == null);
		}

		public GameObject Find(Vector3Int cellPos)
		{
			if (cellPos.x < MinX || cellPos.x > MaxX)
				return null;

			if (cellPos.y < MinY || cellPos.y > MaxY)
				return null;

            if (cellPos.z < MinZ || cellPos.z > MaxZ)
                return null;

            int x = cellPos.x - MinX;
            int y = cellPos.y - MinY;
            int z = cellPos.z - MinZ;

            return _objects[x, y, z];
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
            if (posInfo.PosZ < MinZ || posInfo.PosZ > MaxZ)
                return false;

            // Zone
            Zone zone = gameObject.Room.GetZone(gameObject.CellPos);
			zone.Remove(gameObject);

			{
				int x = posInfo.PosX - MinX;
				int y = posInfo.PosY - MinY;
                int z = posInfo.PosZ - MinZ;

                if (_objects[x, y , z] == gameObject)
					_objects[x, y, z] = null;
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
					int y = posInfo.PosY - MinY;
                    int z = posInfo.PosZ - MinZ;

                    if (_objects[x, y, z] == gameObject)
						_objects[x, y, z] = null;
				}
				{ 
					int x = dest.x - MinX;
					int y = dest.y - MinY;
                    int z = dest.z - MinZ;

                    _objects[x, y, z] = gameObject;
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
			posInfo.PosZ = dest.z;
			return true;
		}

		public void LoadMap(int mapId, string pathPrefix = "../../../../../Common/MapData")
		{
			string mapName = "Map_" + mapId.ToString("000");

			// Collision 관련 파일
			string text = File.ReadAllText($"{pathPrefix}/{mapName}.txt");
			StringReader reader = new StringReader(text);

            MaxX = int.Parse(reader.ReadLine());
            MinX = int.Parse(reader.ReadLine());
            MaxZ = int.Parse(reader.ReadLine());
            MinZ = int.Parse(reader.ReadLine());

            MinY = int.Parse(reader.ReadLine());
            MaxY = MinY;

            int xCount = MaxX - MinX + 1;
			int yCount = MaxY - MinY + 1;
			int zCount = MaxZ - MinZ + 1;

            _collision = new bool[xCount, yCount , zCount];
			_objects = new GameObject[xCount, yCount, zCount];

			for (int y = 0; y < yCount; y++)
			{
				string line = reader.ReadLine();
				for (int x = 0; x < xCount; x++)
				{
					for (int z = 0; z < zCount; z++)
					{
						_collision[x, y, z] = (line[z] == '1' ? true : false);
					}
				}	
			}
		}

        #region A* PathFinding

        // 6방향 이동 (앞/뒤/좌/우/위/아래)
        // 순서 주의: 앞 4개가 x/z 평면 이동, 뒤 2개가 y 이동이다.
        // 이 맵은 단일 Y 평면이라(로더가 MaxY = MinY) y 이웃은 매번 CanGo 의 경계 검사에서
        // 걸러지기만 했다. 즉 노드마다 확장의 1/3 이 확정적으로 헛일이었다.
        // FindPath 는 추적 중인 몬스터가 200ms 마다 호출하는 핫패스라 그대로 낭비가 된다.
        int[] _deltaX = { 1, -1, 0, 0, 0, 0 };
        int[] _deltaY = { 0, 0, 0, 0, 1, -1 };
        int[] _deltaZ = { 0, 0, 1, -1, 0, 0 };
        int[] _cost = { 10, 10, 10, 10, 10, 10 };

        // 맵이 평면이면 4방향만, 층이 있으면 6방향을 확장한다.
        int DirectionCount { get { return SizeY > 1 ? 6 : 4; } }

        private int Heuristic(Pos a, Pos b)
        {
            return 10 * (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z));
        }

        public List<Vector3Int> FindPath(Vector3Int startCellPos, Vector3Int destCellPos, bool checkObjects = true, int maxDist = 10)
		{
			using var _measure = ServerMetrics.Measure("findpath");

			List<Pos> path = new List<Pos>();

			// 점수 매기기
			// F = G + H
			// F = 최종 점수 (작을 수록 좋음, 경로에 따라 달라짐)
			// G = 시작점에서 해당 좌표까지 이동하는데 드는 비용 (작을 수록 좋음, 경로에 따라 달라짐)
			// H = 목적지에서 얼마나 가까운지 (작을 수록 좋음, 고정)

			// (y, x, z) 이미 방문했는지 여부 (방문 = closed 상태)
			HashSet<Pos> closeList = new HashSet<Pos>(); // CloseList

			// (y, x, z) 가는 길을 한 번이라도 발견했는지
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

			pq.Push(new PQNode() { F = Heuristic(dest, pos), G = 0, X = pos.X, Y = pos.Y, Z = pos.Z});
			parent.Add(pos, pos);

			while (pq.Count > 0)
			{
				// 제일 좋은 후보를 찾는다
				PQNode pqNode = pq.Pop();
				Pos node = new Pos(pqNode.X, pqNode.Y, pqNode.Z);
				// 동일한 좌표를 여러 경로로 찾아서, 더 빠른 경로로 인해서 이미 방문(closed)된 경우 스킵
				if (closeList.Contains(node))
					continue;

				// 방문한다
				closeList.Add(node);

				// 목적지 도착했으면 바로 종료
				if (node.Y == dest.Y && node.X == dest.X && node.Z == dest.Z)
					break;

				// 상하좌우 등 이동할 수 있는 좌표인지 확인해서 예약(open)한다
				int dirCount = DirectionCount;
				for (int i = 0; i < dirCount; i++)
				{
					Pos next = new Pos(node.X + _deltaX[i], node.Y + _deltaY[i] , node.Z + _deltaZ[i]);

					// 너무 멀면 스킵
					if (Math.Abs(pos.Y - next.Y) + Math.Abs(pos.X - next.X) + Math.Abs(pos.Z - next.Z) > maxDist)
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
			return new Pos(cell.x - MinX, cell.y - MinY , cell.z - MinZ);
		}

		Vector3Int Pos2Cell(Pos pos)
		{
			// ArrayPos -> CellPos
			return new Vector3Int(pos.X + MinX, pos.Y + MinY, pos.Z + MinZ);
		}

		#endregion
	}

}
