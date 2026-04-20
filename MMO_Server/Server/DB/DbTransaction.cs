using Google.Protobuf.Protocol;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Game;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Server.DB
{
	public partial class DbTransaction /*: JobSerializer*/
	{
		public static DbTransaction Instance { get; } = new DbTransaction();

        //Graceful Shutdown을 위해 JobSerializer를 상속받는 대신, DbTransaction이 JobSerializer를 포함하도록 변경
        //Poison Pill 패턴을 위해 BlockingCollection 사용
        private readonly BlockingCollection<Action> _jobQueue = new();
		public void StopAcceptingJobs()
		{
			_jobQueue.CompleteAdding();
        }

		public void FlushBlocking()
		{
            // GetConsumingEnumerable()은 큐가 비면 스레드를 재우고(CPU 0%), 
            // CompleteAdding()이 호출된 상태에서 큐가 완전히 비워지면 자동으로 foreach를 탈출합니다.
            foreach (Action job in _jobQueue.GetConsumingEnumerable())
            {
                try
                {
                    job.Invoke(); // 람다식으로 넘긴 DB 저장 로직 실행!
                }
                catch (Exception e)
                {
                    // 특정 DB 쿼리 하나가 에러 났다고 DB 스레드 전체가 죽는 것을 방지
                    Console.WriteLine($"DB Transaction Error: {e.Message}");
                }
            }
            Console.WriteLine("DB 스레드: 큐에 남은 모든 작업을 안전하게 DB에 플러시하고 퇴근합니다.");
        }


        // Me (GameRoom) -> You (Db) -> Me (GameRoom)
        public static void SavePlayerStatus_AllInOne(Player player, GameRoom room)
		{
			if (player == null || room == null)
				return;

			// Me (GameRoom)
			PlayerDb playerDb = new PlayerDb();
			playerDb.PlayerDbId = player.PlayerDbId;
			playerDb.Hp = player.Stat.Hp;

            // You
            Instance._jobQueue.Add(() =>
			{
				using (AppDbContext db = new AppDbContext())
				{
					db.Entry(playerDb).State = EntityState.Unchanged;
					db.Entry(playerDb).Property(nameof(PlayerDb.Hp)).IsModified = true;
					bool success = db.SaveChangesEx();
					if (success)
					{
						// Me
					}
				}
			});			
		}

		// Me (GameRoom)
		public static void SavePlayerStatus_Step1(Player player, GameRoom room)
		{
			if (player == null || room == null)
				return;

			// Me (GameRoom)
			PlayerDb playerDb = new PlayerDb();
			playerDb.PlayerDbId = player.PlayerDbId;
			playerDb.Hp = player.Stat.Hp;
			//Instance._jobQueue.Push<PlayerDb, GameRoom>(SavePlayerStatus_Step2, playerDb, room);
            Instance._jobQueue.Add(() => SavePlayerStatus_Step2(playerDb, room));
        }

		// You (Db)
		public static void SavePlayerStatus_Step2(PlayerDb playerDb, GameRoom room)
		{
			using (AppDbContext db = new AppDbContext())
			{
				db.Entry(playerDb).State = EntityState.Unchanged;
				db.Entry(playerDb).Property(nameof(PlayerDb.Hp)).IsModified = true;
				bool success = db.SaveChangesEx();
				if (success)
				{
					room.Push(SavePlayerStatus_Step3, playerDb.Hp);
				}
			}
		}

		// Me
		public static void SavePlayerStatus_Step3(int hp)
		{

		}

		public static void RewardPlayer(Player player, RewardData rewardData, GameRoom room)
		{
			if (player == null || rewardData == null || room == null)
				return;

			// TODO : 살짝 문제가 있긴 하다...
			// 1) DB에다가 저장 요청
			// 2) DB 저장 OK
			// 3) 메모리에 적용
			int? slot = player.Inven.GetEmptySlot();
			if (slot == null)
				return;

			ItemDb itemDb = new ItemDb()
			{
				TemplateId = rewardData.itemId,
				Count = rewardData.count,
				Slot = slot.Value,
				OwnerDbId = player.PlayerDbId
			};

			// You
			Instance._jobQueue.Add(() =>
			{
				using (AppDbContext db = new AppDbContext())
				{
					db.Items.Add(itemDb);
					bool success = db.SaveChangesEx();
					if (success)
					{
						// Me
						room.Push(() =>
						{
							Item newItem = Item.MakeItem(itemDb);
							player.Inven.Add(newItem);

							// Client Noti
							{
								S_AddItem itemPacket = new S_AddItem();
								ItemInfo itemInfo = new ItemInfo();
								itemInfo.MergeFrom(newItem.Info);
								itemPacket.Items.Add(itemInfo);

								player.Session.Send(itemPacket);
							}
						});
					}
				}
			});
		}
	}
}
