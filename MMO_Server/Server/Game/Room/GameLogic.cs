using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Game
{

	//사실상 RoomManager
	public class GameLogic : JobSerializer
	{
		public static GameLogic Instance { get; } = new GameLogic();

		Dictionary<int, GameRoom> _rooms = new Dictionary<int, GameRoom>();
		int _roomId = 1;

		public void Update()
		{
			long start = System.Diagnostics.Stopwatch.GetTimestamp();

			Flush();

			foreach (GameRoom room in _rooms.Values)
			{
				room.Update();
			}

			ServerMetrics.RecordTick(System.Diagnostics.Stopwatch.GetTimestamp() - start);
		}

		// Graceful Shutdown 전용: GameLogic 자기 큐 + 모든 Room 큐까지 전부 Flush
		public void FlushAll()
		{
			Flush();

			foreach (GameRoom room in _rooms.Values)
			{
				room.Flush();
			}
		}

		public GameRoom Add(int mapId)
		{
			GameRoom gameRoom = new GameRoom();
			gameRoom.Push(gameRoom.Init, mapId, 10);

			gameRoom.RoomId = _roomId;
			_rooms.Add(_roomId, gameRoom);
			_roomId++;

			return gameRoom;
		}

		public bool Remove(int roomId)
		{
			return _rooms.Remove(roomId);
		}

		public GameRoom Find(int roomId)
		{
			GameRoom room = null;
			if (_rooms.TryGetValue(roomId, out room))
				return room;

			return null;
		}
	}
}
