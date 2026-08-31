using Prometheus;
using System;
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

		// 틱 안에서 어디에 시간이 쓰이는지 쪼개 본다.
		// 최적화 대상을 추측으로 고르면 엉뚱한 데를 판다 — 실제로 송신 배칭이
		// 이미 잘 돼 있는데 그걸 병목으로 짚은 적이 있다.
		private static readonly Histogram HotPathDuration = Metrics.CreateHistogram(
			"game_hotpath_duration_seconds", "게임 스레드 핫패스별 소요 시간.",
			new HistogramConfiguration
			{
				// 마이크로초 단위가 대부분이라 아래쪽을 촘촘히
				Buckets = new[] { 0.000_005, 0.000_02, 0.000_05, 0.000_2, 0.000_5, 0.002, 0.005, 0.02 },
				LabelNames = new[] { "path" }
			});

		private static readonly Counter HotPathCalls = Metrics.CreateCounter(
			"game_hotpath_calls_total", "핫패스 호출 수.",
			new CounterConfiguration { LabelNames = new[] { "path" } });

		// 브로드캐스트 1회가 몇 명에게 나갔는가(팬아웃).
		// 브로드캐스트가 틱 시간의 대부분을 먹는데, 그 비용은 "호출 수 x 팬아웃"이다.
		// 소요 시간만 보면 둘 중 어느 쪽이 늘었는지 구분할 수 없어서 팬아웃을 따로 센다.
		// 밀집 시나리오(DummyClient 의 cluster 명령)에서 이 값이 몇 배로 뛰는지가 핵심 지표다.
		//
		// 수신자 루프 "안"이 아니라 Broadcast 호출당 1 회만 관측한다.
		// 예전에 루프 안에서 재다가 초당 2만 회 측정이 되어 측정 자체가 부하가 됐고,
		// 재려던 대상을 측정 행위가 왜곡했다. 호출당 1 회면 700 CCU 에서 초당 ~7천 회라 무시 가능.
		private static readonly Histogram BroadcastRecipients = Metrics.CreateHistogram(
			"game_broadcast_recipients", "브로드캐스트 1회당 수신자 수.",
			new HistogramConfiguration
			{
				Buckets = new[] { 1.0, 2, 5, 10, 20, 40, 80, 160, 320, 640 }
			});

		public static void RecordBroadcastRecipients(int count) => BroadcastRecipients.Observe(count);

		// 지형/점유로 거부된 이동. 어뷰징이 아니라서 ValidationRejected 로 세지 않는데,
		// 그러면 지표에 아무 흔적이 남지 않는다.
		//
		// 밀집 시나리오 해석에 반드시 필요하다. 한 칸에 한 명만 설 수 있으므로
		// 밀도가 1 에 가까워지면 이동이 거부되기 시작하고, 거부된 이동은 브로드캐스트를
		// 만들지 않는다. 즉 팬아웃 부하는 밀도에 대해 단조 증가가 아니라 정점을 찍고 꺾인다.
		// 이 카운터가 없으면 "브로드캐스트가 줄었다"를 보고도
		//   밀집이 안 된 건지 / 밀집돼서 못 움직이는 건지
		// 구분할 수 없다.
		private static readonly Counter MoveBlocked = Metrics.CreateCounter(
			"game_move_blocked_total", "지형/점유로 거부된 이동 수.",
			new CounterConfiguration { LabelNames = new[] { "reason" } });

		public static void IncrementMoveBlocked(string reason) => MoveBlocked.WithLabels(reason).Inc();

		// using 으로 감싸 쓰는 측정 스코프. 게임 스레드 전용이라 동기화 없음.
		public struct HotPathScope : IDisposable
		{
			readonly string _path;
			readonly long _start;
			public HotPathScope(string path) { _path = path; _start = Stopwatch.GetTimestamp(); }
			public void Dispose()
			{
				double sec = (double)(Stopwatch.GetTimestamp() - _start) / Stopwatch.Frequency;
				HotPathDuration.WithLabels(_path).Observe(sec);
				HotPathCalls.WithLabels(_path).Inc();
			}
		}

		public static HotPathScope Measure(string path) => new HotPathScope(path);

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

		// 거부는 했지만 어뷰징으로 세지 않은 건수.
		// 서버가 스스로 플레이어를 옮긴 직후(사망 리스폰 등) 클라의 in-flight 이동이 여기 잡힌다.
		// 이 값이 비정상적으로 크면 유예 창이 너무 넓거나, 서버가 위치를 바꾸는
		// 다른 경로가 있는데 epoch 을 안 찍고 있다는 신호다.
		private static readonly Counter ValidationForgiven = Metrics.CreateCounter(
			"game_validation_forgiven_total", "거부했으나 어뷰징으로 세지 않은 요청 수(서버 기인 위치 변경 직후).",
			new CounterConfiguration { LabelNames = new[] { "kind" } });

		public static void IncrementValidationRejected(Game.GameRoom.ViolationKind kind)
		{
			ValidationRejected.WithLabels(kind.ToString()).Inc();
		}

		public static void IncrementValidationForgiven(Game.GameRoom.ViolationKind kind)
		{
			ValidationForgiven.WithLabels(kind.ToString()).Inc();
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
