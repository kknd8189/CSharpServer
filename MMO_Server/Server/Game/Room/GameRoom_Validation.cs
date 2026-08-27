using Protocol;
using Serilog;
using Server.Data;
using System;

namespace Server.Game
{
	// 클라이언트가 보낸 요청이 물리적으로 가능한지 서버가 판정하는 곳.
	// 데미지 값 자체는 서버가 DataManager 에서 읽어 계산하므로 조작할 수 없지만,
	// "얼마나 자주" 와 "얼마나 멀리" 는 클라가 마음대로 보낼 수 있어 여기서 막는다.
	public partial class GameRoom : JobSerializer
	{
		public enum ViolationKind
		{
			SkillCooldown,
			MoveSpeed,
			Teleport,
		}

		// 이동 예산 상한 = Speed × 이 값(초).
		// 크게 잡을수록 서버 랙에 관대해지지만 스피드핵 탐지가 늦어진다.
		// 1초면 2배속 핵이 약 1초 만에 예산을 바닥내고 걸린다.
		const float MoveBudgetCapSeconds = 1.0f;

		// 정상 클라이언트는 C_Move 에 항상 현재 위치에서 1칸 떨어진 목적지를 담는다.
		// 2칸까지 허용하는 건 순수한 안전 마진이며, 그 이상은 텔레포트로 간주한다.
		const int MaxCellsPerMove = 2;

		// 어뷰징 점수: 위반 1건당 가중치와 감쇠 속도.
		// 정상 유저가 랙으로 어쩌다 1건 받아도 시간이 지나면 사라지고,
		// 실제 핵은 초당 수십 건이라 순식간에 임계를 넘는다.
		const float AbuseScorePerViolation = 1.0f;
		const float AbuseScoreDecayPerSec = 0.2f;
		const float AbuseScoreKickThreshold = 20.0f;

		// 쿨다운이 풀렸는지만 검사한다. 실제 소모(NextSkillTick 갱신)는
		// 스킬이 진짜 발사되는 시점에 호출부에서 한다 —
		// 시전 중이라 못 쏜 경우까지 쿨다운을 먹이면 안 되기 때문.
		bool CheckSkillCooldown(Player player, Skill skillData, long now)
		{
			if (now >= player.NextSkillTick)
				return true;

			OnViolation(player, ViolationKind.SkillCooldown,
				"Skill cooldown violation. SkillId={SkillId} RemainMs={RemainMs} Cooldown={CooldownSec}",
				skillData.id, player.NextSkillTick - now, skillData.cooldown);
			return false;
		}

		// 목적지까지의 이동이 물리적으로 가능한지 검사한다.
		// 통과하면 이동 예산에서 이동 거리만큼 차감한다.
		bool TryConsumeMove(Player player, Vector3Int dest, long now)
		{
			Vector3Int cur = player.CellPos;

			// 체비셰프 거리 — 대각 이동을 1칸으로 세기 위함.
			// 지금 클라는 4방향만 쓰지만 대각이 추가돼도 판정이 그대로 유효하다.
			int dist = Math.Max(Math.Max(Math.Abs(dest.x - cur.x), Math.Abs(dest.y - cur.y)),
								Math.Abs(dest.z - cur.z));

			// 거리 0 = 상태/방향만 바뀐 경우(Idle↔Moving, 방향 전환). 예산을 쓰지 않는다.
			if (dist == 0)
				return true;

			// 텔레포트: 예산과 무관하게 즉시 위반. 정상 클라는 절대 만들 수 없는 값이다.
			if (dist > MaxCellsPerMove)
			{
				OnViolation(player, ViolationKind.Teleport,
					"Teleport attempt. From={From} To={To} Distance={Distance}",
					cur.ToString(), dest.ToString(), dist);
				return false;
			}

			// 경과 시간만큼 예산 적립 (상한 있음)
			float cap = Math.Max(player.Speed * MoveBudgetCapSeconds, MaxCellsPerMove);
			float elapsedSec = (now - player.LastMoveTick) / 1000f;
			if (elapsedSec > 0)
				player.MoveBudget = Math.Min(player.MoveBudget + player.Speed * elapsedSec, cap);
			player.LastMoveTick = now;

			if (player.MoveBudget < dist)
			{
				OnViolation(player, ViolationKind.MoveSpeed,
					"Move speed violation. Budget={Budget:F2} Needed={Needed} Speed={Speed}",
					player.MoveBudget, dist, player.Speed);
				return false;
			}

			player.MoveBudget -= dist;
			return true;
		}

		// 위반 공통 처리: 카운터 + 로그 + 누적 점수.
		// 카운터는 "비율"을 보기 위한 것이고(오탐이 나면 여기가 먼저 튄다),
		// 로그는 "누가 언제 무엇을" 을 남긴다.
		void OnViolation(Player player, ViolationKind kind, string template, params object[] args)
		{
			ServerMetrics.IncrementValidationRejected(kind);

			long now = Environment.TickCount64;

			// 시간 감쇠 후 가중치 적립
			if (player.LastAbuseTick > 0)
			{
				float decay = (now - player.LastAbuseTick) / 1000f * AbuseScoreDecayPerSec;
				player.AbuseScore = Math.Max(0, player.AbuseScore - decay);
			}
			player.LastAbuseTick = now;
			player.AbuseScore += AbuseScorePerViolation;

			ILogger log = Log
				.ForContext("EventType", "Abuse")
				.ForContext("ViolationKind", kind.ToString())
				.ForContext("PlayerDbId", player.PlayerDbId)
				.ForContext("AccountDbId", player.Session?.AccountDbId ?? 0)
				.ForContext("AbuseScore", Math.Round(player.AbuseScore, 2))
				.ForContext("Remote", player.Session?.RemoteAddress);

			if (player.AbuseScore >= AbuseScoreKickThreshold)
			{
				log.Warning(template + " -> KICK (score {AbuseScore} >= {Threshold})",
					AppendArgs(args, player.AbuseScore, AbuseScoreKickThreshold));
				player.Session?.Disconnect();
			}
			else
			{
				log.Warning(template, args);
			}
		}

		static object[] AppendArgs(object[] args, params object[] extra)
		{
			object[] merged = new object[args.Length + extra.Length];
			Array.Copy(args, merged, args.Length);
			Array.Copy(extra, 0, merged, args.Length, extra.Length);
			return merged;
		}

		// 서버가 알고 있는 권위 좌표를 위반자 본인에게만 되돌려 보낸다.
		// 이걸 안 보내면 클라는 자기 화면에서 이미 움직인 상태로 남아 서버와 갈라지고,
		// 이후 이동이 계속 어긋나면서 고무줄 현상이 된다. Broadcast 가 아닌 이유는
		// 다른 플레이어들에게는 애초에 잘못된 이동이 전파된 적이 없기 때문.
		void SendPositionCorrection(Player player)
		{
			if (player.Session == null)
				return;

			S_Move correction = new S_Move();
			correction.ObjectId = player.Info.ObjectId;
			correction.PosInfo = player.Info.PosInfo;
			player.Session.Send(correction);
		}
	}
}
