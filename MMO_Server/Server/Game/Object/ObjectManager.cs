using Protocol;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Game
{
	public class ObjectManager
	{
		public static ObjectManager Instance { get; } = new ObjectManager();

		object _lock = new object();
		Dictionary<int, Player> _players = new Dictionary<int, Player>();

		// [UNUSED(1)][TYPE(7)][ID(24)]
		//
		// 예전에는 _counter 를 그대로 OR 했다. 24 비트를 넘는 순간 캐리가 TYPE 필드로 올라가
		// GetObjectTypeById 가 엉뚱한 타입을 돌려주고, GameRoom.EnterGame 의 타입 분기가
		// 통째로 빗나가 플레이어가 룸에 등록되지 않는다(= 접속은 되는데 게임에 못 들어감).
		// 게다가 카운터를 모든 타입이 공유해서, 발사체가 초당 수백 개씩 생기는
		// 700 CCU 부하에서는 약 13 시간이면 소진된다. 라이브였다면 하루도 못 버틴다.
		//
		// 지금은 24 비트로 자르고 순환시킨다. 순환 자체는 재사용 ID 충돌 위험이 있지만
		// 1,677만 개가 동시에 살아있는 상황은 아니므로 실질 안전하다.
		// 대신 한 바퀴 돌 때마다 경고를 남겨서 "돌고 있다"는 사실이 보이게 한다.
		const int IdMask = 0x00FF_FFFF;   // 24비트
		const int IdCapacity = IdMask + 1;

		int _counter = 0;
		int _wrapCount = 0;

		public T Add<T>() where T : GameObject, new()
		{
			T gameObject = new T();

			lock (_lock)
			{
				gameObject.Id = GenerateId(gameObject.ObjectType);

				if (gameObject.ObjectType == GameObjectType.Player)
				{
					_players.Add(gameObject.Id, gameObject as Player);
				}
			}

			return gameObject;
		}

		int GenerateId(GameObjectType type)
		{
			lock (_lock)
			{
				int id = _counter++;

				if (_counter >= IdCapacity)
				{
					_counter = 0;
					_wrapCount++;
					CoreLogger.Warn("ObjectId",
						"Object id counter wrapped. WrapCount={WrapCount} Capacity={Capacity}",
						_wrapCount, IdCapacity);
				}

				// 마스크는 이중 안전장치다. 위에서 순환시키므로 평시엔 아무것도 바꾸지 않지만,
				// 누군가 _counter 를 다른 경로로 건드려도 TYPE 필드는 침범당하지 않는다.
				return ((int)type << 24) | (id & IdMask);
			}
		}

		// 소진 진행도(0~1). 관측용 — 1 에 가까워지면 순환이 임박했다는 뜻이다.
		public double IdUsage
		{
			get { lock (_lock) return (double)_counter / IdCapacity; }
		}

		public static GameObjectType GetObjectTypeById(int id)
		{
			int type = (id >> 24) & 0x7F;
			return (GameObjectType)type;
		}

		public bool Remove(int objectId)
		{
			GameObjectType objectType = GetObjectTypeById(objectId);

			lock (_lock)
			{
				if (objectType == GameObjectType.Player)
					return _players.Remove(objectId);
			}

			return false;
		}

		public Player Find(int objectId)
		{
			GameObjectType objectType = GetObjectTypeById(objectId);

			lock (_lock)
			{
				if (objectType == GameObjectType.Player)
				{
					Player player = null;
					if (_players.TryGetValue(objectId, out player))
						return player;
				}
			}

			return null;
		}
	}
}
