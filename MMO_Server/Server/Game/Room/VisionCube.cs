using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Server.Game.Room
{
	public class VisionCube
	{
		public Player Owner { get; private set; }
		public HashSet<GameObject> PreviousObjects { get; private set; } = new HashSet<GameObject>();

		public VisionCube(Player owner)
		{
			Owner = owner;
		}

		public HashSet<GameObject> GatherObjects()
		{
			if (Owner == null || Owner.Room == null)
				return null;

			HashSet<GameObject> objects = new HashSet<GameObject>();
			Vector3Int cellPos = Owner.CellPos;

			// 시야 판정은 GameRoom.IsInVision 으로 통일했다.
			// 예전엔 x/y 로 컬링했는데 이 맵은 x/z 평면이라, y 검사는 아무것도 걸러내지 못하고
			// z 는 아예 검사하지 않아 시야 밖 오브젝트까지 전부 담고 있었다.
			// Broadcast 와 같은 버그가 양쪽에 복사돼 있어 한 곳으로 모았다.
			Owner.Room.ForEachAdjacentZone(cellPos, GameRoom.VisionCells, zone =>
			{
				foreach (Player player in zone.Players)
				{
					if (GameRoom.IsInVision(cellPos, player.CellPos))
						objects.Add(player);
				}

				foreach (Monster monster in zone.Monsters)
				{
					if (GameRoom.IsInVision(cellPos, monster.CellPos))
						objects.Add(monster);
				}

				foreach (Projectile projectile in zone.Projectiles)
				{
					if (GameRoom.IsInVision(cellPos, projectile.CellPos))
						objects.Add(projectile);
				}
			});

			return objects;
		}

		public void Update()
		{
			if (Owner == null || Owner.Room == null)
				return;

			HashSet<GameObject> currentObjects = GatherObjects();

			// 기존엔 없었는데 새로 생긴 애들 Spawn 처리
			List<GameObject> added = currentObjects.Except(PreviousObjects).ToList();
			if (added.Count > 0)
			{
				S_Spawn spawnPacket = new S_Spawn();

				foreach (GameObject gameObject in added)
				{
					ObjectInfo info = new ObjectInfo();
					info.MergeFrom(gameObject.Info);
					spawnPacket.Objects.Add(info);
				}

				Owner.Session.Send(spawnPacket);
			}

			// 기존엔 있었는데 사라진 애들 Despawn 처리
			List<GameObject> removed = PreviousObjects.Except(currentObjects).ToList();
			if (removed.Count > 0)
			{
				S_Despawn despawnPacket = new S_Despawn();

				foreach (GameObject gameObject in removed)
				{
					despawnPacket.ObjectIds.Add(gameObject.Id);
				}

				Owner.Session.Send(despawnPacket);
			}

			// 교체
			PreviousObjects = currentObjects;

			Owner.Room.PushAfter(100, Update);
		}
	}
}
