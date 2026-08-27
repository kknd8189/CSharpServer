using Protocol;
using Server.Data;
using Server.DB;
using Server.DB.LogDB;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Server.Game
{
	public class Monster : GameObject
	{
		public int TemplateId { get; private set; }

		public Monster()
		{
			ObjectType = GameObjectType.Monster;
		}

		public void Init(int templateId)
		{
			TemplateId = templateId;

			MonsterData monsterData = null;
			DataManager.MonsterDict.TryGetValue(TemplateId, out monsterData);
			Stat.MergeFrom(monsterData.stat);
			Stat.Hp = monsterData.stat.MaxHp;
			State = CreatureState.Idle;
		}

		// FSM (Finite State Machine)
		IJob _job;
		public override void Update()
		{
			switch (State)
			{
				case CreatureState.Idle:
					UpdateIdle();
					break;
				case CreatureState.Moving:
					UpdateMoving();
					break;
				case CreatureState.Skill:
					UpdateSkill();
					break;
				case CreatureState.Dead:
					UpdateDead();
					break;
			}

			// 5프레임 (0.2초마다 한번씩 Update)
			if (Room != null)
				_job = Room.PushAfter(200, Update);
		}

		Player _target;
		int _searchCellDist = 10;
		int _chaseCellDist = 20;

		long _nextSearchTick = 0;
		protected virtual void UpdateIdle()
		{
			if (_nextSearchTick > Environment.TickCount64)
				return;
			_nextSearchTick = Environment.TickCount64 + 1000;

			Player target = Room.FindClosestPlayer(CellPos, _searchCellDist);

			if (target == null)
				return;

			_target = target;
			State = CreatureState.Moving;
		}

		int _skillRange = 1;
		long _nextMoveTick = 0;
		protected virtual void UpdateMoving()
		{
			if (_nextMoveTick > Environment.TickCount64)
				return;
			int moveTick = (int)(1000 / Speed);
			_nextMoveTick = Environment.TickCount64 + moveTick;

			if (_target == null || _target.Room != Room)
			{
				_target = null;
				State = CreatureState.Idle;
				BroadcastMove();
				return;
			}

			Vector3Int dir = _target.CellPos - CellPos;
			int dist = dir.cellDistFromZero;
			if (dist == 0 || dist > _chaseCellDist)
			{
				_target = null;
				State = CreatureState.Idle;
				BroadcastMove();
				return;
			}

			List<Vector3Int> path = Room.Map.FindPath(CellPos, _target.CellPos, checkObjects: true);
			if (path.Count < 2 || path.Count > _chaseCellDist)
			{
				_target = null;
				State = CreatureState.Idle;
				BroadcastMove();
				return;
			}

			// 스킬로 넘어갈지 체크
			if (dist <= _skillRange && (dir.x == 0 || dir.y == 0))
			{
				_coolTick = 0;
				State = CreatureState.Skill;
				return;
			}

			// 이동
			Dir = GetDirFromVec(path[1] - CellPos);
			Room.Map.ApplyMove(this, path[1]);
			BroadcastMove();
		}

		void BroadcastMove()
		{
			// 다른 플레이어한테도 알려준다
			S_Move movePacket = new S_Move();
			movePacket.ObjectId = Id;
			movePacket.PosInfo = PosInfo;
			Room.Broadcast(CellPos, movePacket);
		}

		long _coolTick = 0;
		protected virtual void UpdateSkill()
		{
			if (_coolTick == 0)
			{
				// 유효한 타겟인지
				if (_target == null || _target.Room != Room)
				{
					_target = null;
					State = CreatureState.Moving;
					BroadcastMove();
					return;
				}

				// 스킬이 아직 사용 가능한지
				Vector3Int dir = (_target.CellPos - CellPos);
				int dist = dir.cellDistFromZero;
				bool canUseSkill = (dist <= _skillRange && (dir.x == 0 || dir.y == 0));
				if (canUseSkill == false)
				{
					State = CreatureState.Moving;
					BroadcastMove();
					return;
				}

				// 타게팅 방향 주시
				MoveDir lookDir = GetDirFromVec(dir);
				if (Dir != lookDir)
				{
					Dir = lookDir;
					BroadcastMove();
				}

				Skill skillData = null;
				DataManager.SkillDict.TryGetValue(1, out skillData);

				// 데미지 판정
				_target.OnDamaged(this, skillData.damage + TotalAttack);

				// 스킬 사용 Broadcast
				S_Skill skill = new S_Skill() { Info = new SkillInfo() };
				skill.ObjectId = Id;
				skill.Info.SkillId = skillData.id;
				Room.Broadcast(CellPos, skill);

				// 스킬 쿨타임 적용
				int coolTick = (int)(1000 * skillData.cooldown);
				_coolTick = Environment.TickCount64 + coolTick;
			}

			if (_coolTick > Environment.TickCount64)
				return;

			_coolTick = 0;
		}

		protected virtual void UpdateDead()
		{

		}

		public override void OnDead(GameObject attacker)
		{
			if (_job != null)
			{
				_job.Cancel = true;
				_job = null;
			}

			base.OnDead(attacker);

			GameObject owner = attacker.GetOwner();
			if (owner.ObjectType == GameObjectType.Player)
			{
				Player player = (Player)owner;
				RewardData rewardData = GetRandomReward(player);
				if (rewardData != null)
					DbTransaction.RewardPlayer(player, rewardData, Room, "MonsterDrop");
			}
		}

		// 게임 스레드 전용. 몬스터가 죽을 때마다 new Random() 을 만들면
		// 매번 힙 할당이 생기고, 인스턴스마다 시드 상태가 따로 놀아 분포를 검증하기 어렵다.
		static readonly Random _rewardRand = new Random();

		RewardData GetRandomReward(Player player)
		{
			MonsterData monsterData = null;
			if (DataManager.MonsterDict.TryGetValue(TemplateId, out monsterData) == false)
				return null;

			// [확률 버그 수정] 예전: Next(0, 101) 로 0~100 을 뽑고 rand <= sum 으로 비교.
			// 0 과 100 이 모두 포함돼 경우의 수가 101 개인데 확률은 100 분율이라 어긋났다.
			// probability=1(1% 의도)이면 rand ∈ {0,1} 이 당첨 → 2/101 ≈ 1.98% 로 약 2배.
			// 저확률 아이템일수록 오차가 커지는, 가챠 게임에선 치명적인 형태였다.
			// 지금: 1~100 중 하나를 뽑아 누적 확률과 비교한다. probability=1 → 정확히 1%.
			int roll = _rewardRand.Next(1, 101);

			int sum = 0;
			foreach (RewardData rewardData in monsterData.rewards)
			{
				sum += rewardData.probability;

				if (roll <= sum)
				{
					LogHelper.LogItemRoll(player.PlayerDbId, TemplateId, roll,
						rewardData.itemId, rewardData.probability, "MonsterDrop");
					return rewardData;
				}
			}

			// 미당첨도 남긴다. 당첨만 기록하면 분모(시행 횟수)를 몰라
			// "실제 확률이 설정값과 맞는가"를 검증할 수 없다.
			LogHelper.LogItemRoll(player.PlayerDbId, TemplateId, roll, null, sum, "MonsterDrop");
			return null;
		}
	}
}
