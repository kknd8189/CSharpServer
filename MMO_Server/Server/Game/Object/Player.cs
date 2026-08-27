using Protocol;
using Server.DB;
using Server.Game.Room;

namespace Server.Game
{
	public class Player : GameObject
	{
		public int PlayerDbId { get; set; }
		public ClientSession Session { get; set; }
		public VisionCube Vision { get; private set; }

		// 주기 저장(GameRoom.SaveTick) 대상 여부. 게임 스레드에서만 접근하므로 동기화 불필요
		public bool IsDirty { get; set; }

		#region 서버 검증 상태
		// 아래 값들은 GameRoom 잡 큐를 통해 게임 스레드에서만 접근하므로 동기화가 필요 없다.

		// 스킬 쿨다운이 풀리는 시각. Monster._coolTick 과 같은 방식.
		public long NextSkillTick;

		// 이동 예산(토큰 버킷)을 마지막으로 적립한 시각.
		public long LastMoveTick;

		// 남은 이동 가능 셀 수. 경과 시간 × Speed 만큼 적립되고 이동할 때마다 차감된다.
		// 매 이동을 독립적으로 "경과시간 × 속도" 와 비교하지 않는 이유:
		// 서버 틱이 밀리면(부하 테스트에서 700 CCU 기준 최대 257ms 관측) 클라가 그 사이
		// 정상적으로 보낸 이동 패킷들이 한꺼번에 처리되면서 "0.001초에 3칸"처럼 보인다.
		// 예산 방식은 밀린 시간만큼 미리 적립돼 있으므로 이 버스트를 흡수한다.
		public float MoveBudget;

		// 누적 어뷰징 점수. 시간이 지나면 감쇠하며, 임계를 넘으면 조치 대상.
		public float AbuseScore;
		public long LastAbuseTick;
		#endregion

		public Inventory Inven { get; private set; } = new Inventory();

		public int WeaponDamage { get; private set; }
		public int ArmorDefence { get; private set; }

		public override int TotalAttack { get { return Stat.Attack + WeaponDamage; } }
		public override int TotalDefence { get { return ArmorDefence; } }

		public Player()
		{
			ObjectType = GameObjectType.Player;
			Vision = new VisionCube(this);
		}

		public override void OnDamaged(GameObject attacker, int damage)
		{
			base.OnDamaged(attacker, damage);
			IsDirty = true;
		}

		public override void OnDead(GameObject attacker)
		{
			base.OnDead(attacker);
		}

		public void OnLeaveGame()
		{
			// TODO
			// DB 연동?
			// -- 피가 깎일 때마다 DB 접근할 필요가 있을까?
			// 1) 서버 다운되면 아직 저장되지 않은 정보 날아감
			// 2) 코드 흐름을 다 막아버린다 !!!!
			// - 비동기(Async) 방법 사용?
			// - 다른 쓰레드로 DB 일감을 던져버리면 되지 않을까?
			// -- 결과를 받아서 이어서 처리를 해야 하는 경우가 많음.
			// -- 아이템 생성

			DbTransaction.SavePlayerStatus_Step1(this, Room);
		}

		public void HandleEquipItem(C_EquipItem equipPacket)
		{
			Item item = Inven.Get(equipPacket.ItemDbId);
			if (item == null)
				return;

			if (item.ItemType == ItemType.Consumable)
				return;

			// 착용 요청이라면, 겹치는 부위 해제
			if (equipPacket.Equipped)
			{
				Item unequipItem = null;

				if (item.ItemType == ItemType.Weapon)
				{
					unequipItem = Inven.Find(
						i => i.Equipped && i.ItemType == ItemType.Weapon);
				}
				else if (item.ItemType == ItemType.Armor)
				{
					ArmorType armorType = ((Armor)item).ArmorType;
					unequipItem = Inven.Find(
						i => i.Equipped && i.ItemType == ItemType.Armor
							&& ((Armor)i).ArmorType == armorType);
				}

				if (unequipItem != null)
				{
					// 메모리 선적용
					unequipItem.Equipped = false;

					// DB에 Noti
					DbTransaction.EquipItemNoti(this, unequipItem);

					// 클라에 통보
					S_EquipItem equipOkItem = new S_EquipItem();
					equipOkItem.ItemDbId = unequipItem.ItemDbId;
					equipOkItem.Equipped = unequipItem.Equipped;
					Session.Send(equipOkItem);
				}
			}

			{
				// 메모리 선적용
				item.Equipped = equipPacket.Equipped;

				// DB에 Noti
				DbTransaction.EquipItemNoti(this, item);

				// 클라에 통보
				S_EquipItem equipOkItem = new S_EquipItem();
				equipOkItem.ItemDbId = equipPacket.ItemDbId;
				equipOkItem.Equipped = equipPacket.Equipped;
				Session.Send(equipOkItem);
			}

			RefreshAdditionalStat();
		}

		public void RefreshAdditionalStat()
		{
			WeaponDamage = 0;
			ArmorDefence = 0;

			foreach (Item item in Inven.Items.Values)
			{
				if (item.Equipped == false)
					continue;

				switch (item.ItemType)
				{
					case ItemType.Weapon:
						WeaponDamage += ((Weapon)item).Damage;
						break;
					case ItemType.Armor:
						ArmorDefence += ((Armor)item).Defence;
						break;
				}
			}
		}
	}
}
