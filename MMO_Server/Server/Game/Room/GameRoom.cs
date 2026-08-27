using ServerCore;
using Protocol;
using Server.Data;
using Server.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Server.Game
{
	public partial class GameRoom : JobSerializer
	{
		public const int VisionCells = 5;

		public int RoomId { get; set; }

		Dictionary<int, Player> _players = new Dictionary<int, Player>();
		Dictionary<int, Monster> _monsters = new Dictionary<int, Monster>();
		Dictionary<int, Projectile> _projectiles = new Dictionary<int, Projectile>();

		public Zone[,,] Zones { get; private set; }
		public int ZoneCells { get; private set; }

		public Map Map { get; private set; } = new Map();

		// ㅁㅁㅁ
		// ㅁㅁㅁ
		// ㅁㅁㅁ
		public Zone GetZone(Vector3Int cellPos)
		{
			int x = (cellPos.x - Map.MinX) / ZoneCells;
			int y = (cellPos.y - Map.MinY) / ZoneCells;
			int z = (cellPos.z - Map.MinZ) / ZoneCells;

			return GetZone(x, y, z);
		}

		public Zone GetZone(int indexX, int indexY, int indexZ)
		{
			if (indexX < 0 || indexX >= Zones.GetLength(0))
				return null;
			if (indexY < 0 || indexY >= Zones.GetLength(1))
				return null;
            if (indexZ < 0 || indexZ >= Zones.GetLength(2))
                return null;

            return Zones[indexX, indexY, indexZ];
		}

		public void Init(int mapId, int zoneCells)
		{
			Map.LoadMap(mapId);

			// Zone
			ZoneCells = zoneCells; // 10
			// 1~10 칸 = 1존
			// 11~20칸 = 2존
			// 21~30칸 = 3존
			int countX = (Map.SizeX + zoneCells - 1) / zoneCells;
			int countY = (Map.SizeY + zoneCells - 1) / zoneCells;
            int countZ = (Map.SizeZ + zoneCells - 1) / zoneCells;

            Zones = new Zone[countX, countY, countZ];
			for (int y = 0; y < countY; y++)
			{
				for (int x = 0; x < countX; x++)
				{
					for(int z =  0; z < countZ; z++)
					{
                        Zones[x, y, z] = new Zone(x, y, z);
                    }
                }
			}

			// TEMP
			for (int i = 0; i < 200; i++)
			{
				Monster monster = ObjectManager.Instance.Add<Monster>();
				monster.Init(1);
				EnterGame(monster, randomPos: true);
			}

			PushAfter(SaveIntervalMs, SaveTick);
		}

		// 주기 저장 간격 = 크래시 시 감수하는 최대 유실 시간
		public const int SaveIntervalMs = 60_000;

		// dirty 플레이어만 스냅샷 떠서 배치 하나로 DB 큐에 넘긴다.
		// 스냅샷은 게임 스레드에서 복사 후 불변 → DB 스레드에서 락 없이 안전
		void SaveTick()
		{
			PushAfter(SaveIntervalMs, SaveTick);

			List<PlayerDb> snapshots = null;
			foreach (Player player in _players.Values)
			{
				if (player.IsDirty == false)
					continue;
				player.IsDirty = false;

				snapshots ??= new List<PlayerDb>();
				snapshots.Add(new PlayerDb()
				{
					PlayerDbId = player.PlayerDbId,
					Level = player.Stat.Level,
					Hp = player.Stat.Hp,
					MaxHp = player.Stat.MaxHp,
					Attack = player.Stat.Attack,
					Speed = player.Stat.Speed,
					TotalExp = player.Stat.TotalExp
					// TODO: 위치 저장 — PlayerDb에 Pos 컬럼 마이그레이션 후 여기에 추가
				});
			}

			if (snapshots != null)
				DbTransaction.SavePlayersBatch(snapshots);
		}

		// 누군가 주기적으로 호출해줘야 한다
		public void Update()
		{
			Flush();
		}

		Random _rand = new Random();
		public void EnterGame(GameObject gameObject, bool randomPos)
		{
			if (gameObject == null)
				return;

			// 서버가 좌표를 바꾸기 직전의 위치. 사망 리스폰 시 클라의 in-flight 이동은
			// 반드시 이 근처에서 출발하므로, 검증에서 조작과 구분하는 기준이 된다.
			Vector3Int posBeforeRelocate = gameObject.CellPos;

			if (randomPos)
			{
				Vector3Int respawnPos;
				while (true)
				{
					respawnPos.x = _rand.Next(Map.MinX, Map.MaxX + 1);
					respawnPos.y = _rand.Next(Map.MinY, Map.MaxY + 1);
					respawnPos.z = _rand.Next(Map.MinZ, Map.MaxZ + 1);

					if (Map.Find(respawnPos) == null)
					{
						gameObject.CellPos = respawnPos;
						break;
					}
				}
			}

			GameObjectType type = ObjectManager.GetObjectTypeById(gameObject.Id);

			if (type == GameObjectType.Player)
			{
				Player player = gameObject as Player;
				_players.Add(gameObject.Id, player);
				player.Room = this;

				player.RefreshAdditionalStat();

				// 검증 상태 초기화. 예산을 가득 채운 상태로 시작해야
				// 접속 직후 첫 이동들이 오탐으로 걸리지 않는다.
				player.LastMoveTick = System.Environment.TickCount64;
				player.PositionEpochTick = player.LastMoveTick;
				player.PrePosition = posBeforeRelocate;
				player.MoveBudget = player.Speed * 1.0f;
				player.NextSkillTick = 0;
				player.AbuseScore = 0;
				player.LastAbuseTick = 0;

				Map.ApplyMove(player, new Vector3Int(player.CellPos.x, player.CellPos.y, player.CellPos.z));
				GetZone(player.CellPos).Players.Add(player);

				// 본인한테 정보 전송
				{
					S_EnterGame enterPacket = new S_EnterGame();
					enterPacket.Player = player.Info;
					player.Session.Send(enterPacket);

					player.Vision.Update();
				}
			}
			else if (type == GameObjectType.Monster)
			{
				Monster monster = gameObject as Monster;
				_monsters.Add(gameObject.Id, monster);
				monster.Room = this;

				GetZone(monster.CellPos).Monsters.Add(monster);
				Map.ApplyMove(monster, new Vector3Int(monster.CellPos.x, monster.CellPos.y, monster.CellPos.z));

				monster.Update();
			}
			else if (type == GameObjectType.Projectile)
			{
				Projectile projectile = gameObject as Projectile;
				_projectiles.Add(gameObject.Id, projectile);
				projectile.Room = this;

				GetZone(projectile.CellPos).Projectiles.Add(projectile);
				projectile.Update();
			}

			// 타인한테 정보 전송
			{
				S_Spawn spawnPacket = new S_Spawn();
				spawnPacket.Objects.Add(gameObject.Info);
				Broadcast(gameObject.CellPos, spawnPacket);
			}
		}

		public void LeaveGame(int objectId)
		{
			GameObjectType type = ObjectManager.GetObjectTypeById(objectId);

			Vector3Int cellPos;

			if (type == GameObjectType.Player)
			{
				Player player = null;
				if (_players.Remove(objectId, out player) == false)
					return;

				cellPos = player.CellPos;

				player.OnLeaveGame();
				Map.ApplyLeave(player);
				player.Room = null;

				// 본인한테 정보 전송
				{
					S_LeaveGame leavePacket = new S_LeaveGame();
					player.Session.Send(leavePacket);
				}
			}
			else if (type == GameObjectType.Monster)
			{
				Monster monster = null;
				if (_monsters.Remove(objectId, out monster) == false)
					return;

				cellPos = monster.CellPos;
				Map.ApplyLeave(monster);
				monster.Room = null;
			}
			else if (type == GameObjectType.Projectile)
			{
				Projectile projectile = null;
				if (_projectiles.Remove(objectId, out projectile) == false)
					return;

				cellPos = projectile.CellPos;
				Map.ApplyLeave(projectile);
				projectile.Room = null;
			}
			else
			{
				return;
			}

			// 타인한테 정보 전송
			{
				S_Despawn despawnPacket = new S_Despawn();
				despawnPacket.ObjectIds.Add(objectId);
				Broadcast(cellPos, despawnPacket);
			}
		}

		Player FindPlayer(Func<GameObject, bool> condition)
		{
			foreach (Player player in _players.Values)
			{
				if (condition.Invoke(player))
					return player;
			}

			return null;
		}

		// 살짝 부담스러운 함수
		public Player FindClosestPlayer(Vector3Int pos, int range)
		{
			List<Player> players = GetAdjacentPlayers(pos, range);

			players.Sort((left, right) =>
			{
				int leftDist = (left.CellPos - pos).cellDistFromZero;
				int rightDist = (right.CellPos - pos).cellDistFromZero;
				return leftDist - rightDist;
			});

			foreach (Player player in players)
			{
				List<Vector3Int> path = Map.FindPath(pos, player.CellPos, checkObjects: true);
				if (path.Count < 2 || path.Count > range)
					continue;

				return player;
			}

			return null;
		}

		// 시야 판정. 이 맵은 x/z 평면이다 (Map 로더가 MaxY = MinY 로 잡는다).
		//
		// 예전에는 x/y 로 컬링했는데 두 가지가 잘못돼 있었다.
		//  1) y 차이는 항상 0 이라 그 검사는 아무것도 걸러내지 못했고
		//  2) z 는 아예 검사하지 않아 시야 밖 플레이어에게도 패킷이 그대로 나갔다.
		// 존은 ZoneCells(10) 단위라 인접 존을 훑으면 z 로 최대 20 셀까지 포함된다.
		// 즉 의도한 시야(11×11=121셀) 대신 약 11×20=220셀에 뿌리고 있었다.
		public static bool IsInVision(Vector3Int center, Vector3Int target)
		{
			return Math.Abs(target.x - center.x) <= VisionCells
				&& Math.Abs(target.z - center.z) <= VisionCells;
		}

		public void Broadcast(Vector3Int pos, IPacket packet)
		{
			// 브로드캐스트는 항상 GameLogic 스레드에서 실행 → ThreadLocal SendBuffer 안전.
			// 패킷을 1회만 직렬화하고 그 세그먼트를 모든 수신자에게 공유한다.
			ArraySegment<byte> segment = ClientSession.SerializeToSendBuffer(packet);
			if (segment.Array == null)
				return;

			// 존 인덱스를 직접 순회한다. ForEachAdjacentZone 을 쓰지 않고 여기서 펼친 이유는
			// 람다가 segment/pos 를 캡처하면서 클로저와 델리게이트를 매 호출마다 할당하기 때문.
			// 브로드캐스트는 이동 1건마다 도는 최핫패스라 할당을 0으로 만든다.
			// (예전에는 GetAdjacentZones 의 HashSet + List, SelectMany 의 이터레이터까지 있었다.)
			int minIndexX = (pos.x - VisionCells - Map.MinX) / ZoneCells;
			int maxIndexX = (pos.x + VisionCells - Map.MinX) / ZoneCells;
			int minIndexY = (pos.y - VisionCells - Map.MinY) / ZoneCells;
			int maxIndexY = (pos.y + VisionCells - Map.MinY) / ZoneCells;
			int minIndexZ = (pos.z - VisionCells - Map.MinZ) / ZoneCells;
			int maxIndexZ = (pos.z + VisionCells - Map.MinZ) / ZoneCells;

			for (int x = minIndexX; x <= maxIndexX; x++)
			{
				for (int y = minIndexY; y <= maxIndexY; y++)
				{
					for (int z = minIndexZ; z <= maxIndexZ; z++)
					{
						Zone zone = GetZone(x, y, z);
						if (zone == null)
							continue;

						foreach (Player p in zone.Players)
						{
							if (IsInVision(pos, p.CellPos) == false)
								continue;

							p.Session.SendShared(segment);
						}
					}
				}
			}
		}

		// 인접 존을 컬렉션으로 만들지 않고 그대로 순회한다.
		// 게임 스레드 전용이라 재진입 걱정이 없다.
		public void ForEachAdjacentZone(Vector3Int cellPos, int range, Action<Zone> action)
		{
			int minIndexX = (cellPos.x - range - Map.MinX) / ZoneCells;
			int maxIndexX = (cellPos.x + range - Map.MinX) / ZoneCells;
			int minIndexY = (cellPos.y - range - Map.MinY) / ZoneCells;
			int maxIndexY = (cellPos.y + range - Map.MinY) / ZoneCells;
			int minIndexZ = (cellPos.z - range - Map.MinZ) / ZoneCells;
			int maxIndexZ = (cellPos.z + range - Map.MinZ) / ZoneCells;

			for (int x = minIndexX; x <= maxIndexX; x++)
			{
				for (int y = minIndexY; y <= maxIndexY; y++)
				{
					for (int z = minIndexZ; z <= maxIndexZ; z++)
					{
						// 인덱스가 모두 다르므로 같은 존이 두 번 나오지 않는다 (중복 제거 불필요).
						Zone zone = GetZone(x, y, z);
						if (zone == null)
							continue;

						action(zone);
					}
				}
			}
		}

		public List<Player> GetAdjacentPlayers(Vector3Int pos, int range)
		{
			List<Zone> zones = GetAdjacentZones(pos, range);
			return zones.SelectMany(z => z.Players).ToList();
		}

		public List<Zone> GetAdjacentZones(Vector3Int cellPos, int range = GameRoom.VisionCells)
		{
			HashSet<Zone> zones = new HashSet<Zone>();

            int minIndexX = (cellPos.x - range - Map.MinX) / ZoneCells;
            int maxIndexX = (cellPos.x + range - Map.MinX) / ZoneCells;
            int minIndexY = (cellPos.y - range - Map.MinY) / ZoneCells;
            int maxIndexY = (cellPos.y + range - Map.MinY) / ZoneCells;
            int minIndexZ = (cellPos.z - range - Map.MinZ) / ZoneCells;
            int maxIndexZ = (cellPos.z + range - Map.MinZ) / ZoneCells;

            for (int x = minIndexX; x <= maxIndexX; x++)
			{
				for (int y = minIndexY; y <= maxIndexY; y++)
				{
					for(int z = minIndexZ; z <= maxIndexZ; z++)
					{
                        Zone zone = GetZone(x, y, z);
                        if (zone == null)
                            continue;

                        zones.Add(zone);
                    }
				}
			}

			return zones.ToList();
		}
	}
}
