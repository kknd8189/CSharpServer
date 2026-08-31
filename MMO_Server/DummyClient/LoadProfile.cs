namespace DummyClient
{
	// 부하의 "공간 분포"만 제어한다. 패킷 전송률도, 이동 규칙도 건드리지 않는다.
	// 흩어짐 → 밀집으로 바꿀 때 달라지는 변수가 오직 "시야 안 인원(팬아웃)" 하나여야
	// 측정된 차이를 밀집도 탓으로 돌릴 수 있다.
	//
	// 왜 필요한가:
	// 서버는 접속 시 맵 전역(111x111 = 12,321칸)에 균등 랜덤 스폰한다(GameRoom.EnterGame).
	// 그래서 700 CCU 라도 시야(11x11 = 121칸) 안에는 평균 7명뿐이고, 브로드캐스트
	// 팬아웃이 실제보다 훨씬 가볍게 잡힌다.
	// 진짜 유저는 마을·보스·사냥터 목에 뭉치는데, 이 서버는 브로드캐스트가 틱 시간의
	// 64% 를 차지하므로 분포 차이가 성능에 가장 크게 작용한다.
	//
	//   흩어짐 700명   → 0.057/칸 → 시야 내 ~7명
	//   반경 15 밀집   → 0.73/칸  → 시야 내 ~88명   (팬아웃 12배)
	//   반경 8 밀집    → 2.4/칸   → 시야 포화 ~121명 (팬아웃 17배)
	//
	// 재접속 없이 토글되므로 "같은 세션 집합"으로 A/B 를 잰다.
	// 세션 구성이 달라져서 생기는 교란이 없다.
	static class LoadProfile
	{
		// REPL 스레드가 쓰고 더미 타이머 스레드들이 읽는다.
		public static volatile bool Cluster;

		// int 는 정렬된 읽기/쓰기가 원자적이라 값이 찢어지지 않는다.
		// 토글 직후 한두 틱이 옛 반경으로 움직여도 측정에 의미 없는 오차라 락을 두지 않았다.
		public static int CenterX = 45;
		public static int CenterZ = 45;
		public static int Radius = 15;

		public static string Describe()
		{
			return Cluster
				? $"cluster ON  center=({CenterX},{CenterZ}) radius={Radius}"
				: "cluster OFF (맵 전역 랜덤 워크)";
		}
	}
}
