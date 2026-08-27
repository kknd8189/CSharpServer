using Prometheus;
using ServerCore;
using System.Diagnostics;

namespace Server
{
	// 성능 메트릭은 프로메테우스가 담당한다.
	// "누가 언제 무엇을"(개별 사건)은 Serilog → ES 쪽이고,
	// 여기는 "얼마나 자주 / 얼마나 오래"(집계값)만 다룬다.
	//
	// 이전에는 5초마다 avg/max 를 직접 계산해 로그 한 줄로 내보냈는데,
	// 그 방식은 개별 틱의 분포를 서버 안에서 버리기 때문에 p99 를 구할 수 없었다.
	// 부하 테스트에서 "틱 최대 기준 400 CCU vs 지속 프레임레이트 기준 600 CCU" 로
	// 판정이 갈렸던 게 정확히 그 한계였다. 히스토그램은 분포를 그대로 남긴다.
	public static class ServerMetrics
	{
		// 30Hz = 프레임당 33.3ms 예산. 그 부근을 촘촘히 나눠야 예산 초과 비율이 보인다.
		// 마지막 버킷 위로 넘어가는 값은 +Inf 에 쌓인다.
		private static readonly double[] TickBuckets =
		{
			0.0005, 0.001, 0.002, 0.005, 0.010, 0.016, 0.025,
			0.0333,          // 30Hz 예산선
			0.050, 0.100, 0.250, 0.500, 1.000
		};

		private static readonly Histogram TickDuration = Metrics.CreateHistogram(
			"game_tick_duration_seconds",
			"게임 로직 1틱 처리 시간. 30Hz 예산은 0.0333초.",
			new HistogramConfiguration { Buckets = TickBuckets });

		// 일이 있었던 틱과 유휴 틱을 나눠 센다.
		// 합치면 유휴가 평균을 0 쪽으로 끌어내려 실제 부하가 가려진다.
		private static readonly Counter TicksTotal = Metrics.CreateCounter(
			"game_ticks_total", "게임 로직 틱 수.", new CounterConfiguration { LabelNames = new[] { "kind" } });

		private static readonly Gauge PlayersConnected = Metrics.CreateGauge(
			"game_players_connected", "현재 접속 중인 플레이어 수.");

		private static readonly Counter PacketsTotal = Metrics.CreateCounter(
			"game_packets_total", "처리한 패킷 수.",
			new CounterConfiguration { LabelNames = new[] { "direction" } });

		// 거부 "비율"을 보기 위한 것. 갑자기 튀면 핵이 유행했거나,
		// 우리 임계값이 잘못돼 정상 유저를 끊고 있거나 둘 중 하나다.
		// 후자가 훨씬 흔하고 치명적이라 배포 직후 반드시 확인해야 한다.
		private static readonly Counter ValidationRejected = Metrics.CreateCounter(
			"game_validation_rejected_total", "서버 검증에서 거부된 요청 수.",
			new CounterConfiguration { LabelNames = new[] { "kind" } });

		private static readonly Counter SessionsClosed = Metrics.CreateCounter(
			"game_sessions_closed_total", "종료된 세션 수.",
			new CounterConfiguration { LabelNames = new[] { "reason" } });

		private static readonly Counter SessionsOpened = Metrics.CreateCounter(
			"game_sessions_opened_total", "새로 맺어진 세션 수.");

		private static readonly Histogram SessionDuration = Metrics.CreateHistogram(
			"game_session_duration_seconds", "세션 유지 시간.",
			new HistogramConfiguration
			{
				Buckets = new[] { 1.0, 5.0, 30.0, 60.0, 300.0, 900.0, 1800.0, 3600.0 },
				LabelNames = new[] { "reason" }
			});

		public static void IncrementPacketsReceived() => PacketsTotal.WithLabels("recv").Inc();
		public static void IncrementPacketsSent() => PacketsTotal.WithLabels("send").Inc();

		// 현재 누적값 조회. 프로메테우스 카운터는 단조 증가라 리셋이 없고,
		// 초당 처리량은 쿼리 시점에 rate() 가 계산한다.
		// 여기서는 테스트에서 증가분을 확인하는 용도로만 쓴다.
		public static double PacketsReceivedValue => PacketsTotal.WithLabels("recv").Value;
		public static double PacketsSentValue => PacketsTotal.WithLabels("send").Value;

		public static void SetPlayersConnected(int count) => PlayersConnected.Set(count);

		public static void IncrementSessionOpened() => SessionsOpened.Inc();

		public static void RecordSessionClosed(CloseReason reason, double durationSeconds)
		{
			string label = reason.ToString();
			SessionsClosed.WithLabels(label).Inc();
			SessionDuration.WithLabels(label).Observe(durationSeconds);
		}

		public static void IncrementValidationRejected(Game.GameRoom.ViolationKind kind)
		{
			ValidationRejected.WithLabels(kind.ToString()).Inc();
		}

		// GameLogic.Update 한 바퀴에 대해 호출된다.
		// Stopwatch ticks 로 받아 초 단위로 변환해 관측한다.
		public static void RecordTick(long elapsedSwTicks)
		{
			if (elapsedSwTicks <= 0)
			{
				// 처리할 잡이 없어 즉시 끝난 틱. 히스토그램에 넣으면 분포가 0 쪽으로 쏠린다.
				TicksTotal.WithLabels("idle").Inc();
				return;
			}

			TicksTotal.WithLabels("work").Inc();
			TickDuration.Observe((double)elapsedSwTicks / Stopwatch.Frequency);
		}
	}
}
