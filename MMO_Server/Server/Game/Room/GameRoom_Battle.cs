using ServerCore;
using Protocol;
using Server.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Server.Game
{
	public partial class GameRoom : JobSerializer
	{
		public void HandleMove(Player player, C_Move movePacket)
		{
			if (player == null)
				return;

			PositionInfo movePosInfo = movePacket.PosInfo;
			ObjectInfo info = player.Info;
			Vector3Int dest = new Vector3Int(movePosInfo.PosX, movePosInfo.PosY, movePosInfo.PosZ);

			// 속도/텔레포트 검증. 실패 시 서버 권위 좌표로 되돌려 보낸다.
			if (TryConsumeMove(player, dest, System.Environment.TickCount64) == false)
			{
				SendPositionCorrection(player);
				return;
			}

			// 다른 좌표로 이동할 경우, 갈 수 있는지 체크
			Vector3Int cur = player.CellPos;
			if (dest.x != cur.x || dest.y != cur.y || dest.z != cur.z)
			{
				if (Map.CanGo(dest) == false)
				{
					// 지형/점유 위반은 어뷰징이라기보다 클라-서버 상태 불일치인 경우가 많다.
					// (다른 플레이어가 먼저 그 칸을 차지하는 등) 위반 점수는 매기지 않고 보정만 한다.
					SendPositionCorrection(player);
					return;
				}
			}

			info.PosInfo.State = movePosInfo.State;
			info.PosInfo.MoveDir = movePosInfo.MoveDir;
			Map.ApplyMove(player, new Vector3Int(movePosInfo.PosX, movePosInfo.PosY, movePosInfo.PosZ));

			// 다른 플레이어한테도 알려준다
			S_Move resMovePacket = new S_Move();
			resMovePacket.ObjectId = player.Info.ObjectId;
			resMovePacket.PosInfo = movePacket.PosInfo;

			Broadcast(player.CellPos, resMovePacket);
		}

		public void HandleSkill(Player player, C_Skill skillPacket)
		{
			if (player == null)
				return;

			ObjectInfo info = player.Info;

			// 스킬 데이터 조회를 먼저 한다. 예전에는 Broadcast 를 먼저 해서,
			// 존재하지 않는 SkillId 를 보내면 그게 그대로 전파되고 플레이어는
			// State=Skill 에 갇힌 채 남았다.
			Data.Skill skillData = null;
			if (DataManager.SkillDict.TryGetValue(skillPacket.Info.SkillId, out skillData) == false)
				return;

			// 쿨다운을 상태 검사보다 먼저 본다.
			// State != Idle 로 먼저 걸러버리면, 첫 스킬 직후 State=Skill 이 되면서
			// 연타가 조용히 return 되어 카운터에도 로그에도 안 남는다.
			// 게다가 클라가 C_Move(State=Idle)를 끼워 넣으면 상태 게이트는 우회된다.
			// 실제 발사 빈도를 제한하는 건 쿨다운이므로 이쪽이 먼저 판정해야 한다.
			long now = System.Environment.TickCount64;
			if (CheckSkillCooldown(player, skillData, now) == false)
				return;

			// 시전 중/이동 중에는 못 쓴다 — 어뷰징이 아니라 게임 규칙이므로 조용히 거부한다.
			if (info.PosInfo.State != CreatureState.Idle)
				return;

			// 실제로 발사되는 시점에만 쿨다운을 소모한다.
			player.NextSkillTick = now + (long)(1000 * skillData.cooldown);

			info.PosInfo.State = CreatureState.Skill;
			S_Skill skill = new S_Skill() { Info = new SkillInfo() };
			skill.ObjectId = info.ObjectId;
			skill.Info.SkillId = skillPacket.Info.SkillId;
			Broadcast(player.CellPos, skill);

			switch (skillData.skillType)
			{
				case SkillType.Auto:
					{
						Vector3Int skillPos = player.GetFrontCellPos(info.PosInfo.MoveDir);
						GameObject target = Map.Find(skillPos);
						if (target != null)
						{
							// hit 처리 자리 (현재는 no-op). 부하 테스트 시 콘솔 도배 방지로 로그 제거.
						}
					}
					break;
				case SkillType.Projectile:
					{
						Arrow arrow = ObjectManager.Instance.Add<Arrow>();
						if (arrow == null)
							return;

						arrow.Owner = player;
						arrow.Data = skillData;
						arrow.PosInfo.State = CreatureState.Moving;
						arrow.PosInfo.MoveDir = player.PosInfo.MoveDir;
						arrow.PosInfo.PosX = player.PosInfo.PosX;
						arrow.PosInfo.PosY = player.PosInfo.PosY;
						arrow.Speed = skillData.projectile.speed;
						Push(EnterGame, arrow, false);
					}
					break;
			}
		}

	}
}
