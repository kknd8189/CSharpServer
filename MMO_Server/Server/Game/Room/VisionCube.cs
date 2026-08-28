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

		// 100ms 주기 갱신 잡의 핸들.
		// 예전에는 PushAfter 의 반환값을 버려서 체인을 취소할 방법이 없었다.
		// 그 결과 사망 → 리스폰 → EnterGame 이 Vision.Update() 를 다시 호출할 때마다
		// 기존 체인이 살아 있는 채로 새 체인이 하나씩 더 붙었다.
		// 700 CCU 부하에서 초당 7,000회여야 할 GatherObjects 가 17,913회 돌고 있었고
		// (플레이어당 평균 2.6개 체인), 시간이 갈수록 계속 늘어나는 구조였다.
		//
		// 성능보다 정합성이 더 문제다. 중복 체인이 같은 PreviousObjects 를 번갈아
		// 갱신하면 diff 가 어긋나 S_Spawn / S_Despawn 이 누락되거나 중복된다.
		// Monster 는 _job 핸들을 들고 OnDead 에서 Cancel 하는데 여기만 빠져 있었다.
		IJob _job;

		public VisionCube(Player owner)
		{
			Owner = owner;
		}

		// 갱신 체인을 끊는다. EnterGame 재진입(리스폰) 직전과 LeaveGame 에서 호출.
		public void Stop()
		{
			if (_job != null)
			{
				_job.Cancel = true;
				_job = null;
			}
		}

		public HashSet<GameObject> GatherObjects()
		{
			if (Owner == null || Owner.Room == null)
				return null;

			using var _measure = ServerMetrics.Measure("vision");

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

			// 다음 갱신을 예약하고 핸들을 보관한다. 핸들이 없으면 취소할 수 없다.
			_job = Owner.Room.PushAfter(100, Update);
		}
	}
}
