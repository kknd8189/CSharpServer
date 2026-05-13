# CSharpServer
CSharpSeveer Programming for portfolio

TODO LIST

1. Redis 및 EF Core + Dapper 사용할수 있는 구조 추가

2. 보안로직 Game Server에 바로 붙지 못하게 LoginServer에서 토큰 발급 및 GameServer에 검증 절차 추가 

3. 패킷처리부분 - byte 배열에서 span을 사용하여 복사비용을 들이지 않고 빠르게 처리할수 있는 방식 만들기
    - ProtoBuf에서 Span 관련된 인터페이스를 지원하지 않아서 Span-> Array로 Copy 비용 발생 // ServerPacketManager.cs MakerPacketSpan 함수 113줄 
    - Custom Packet Generate 제작 

4. LogDB Thread Pool 생성

5. 스트레스 테스트

후 순위
Doker Container And Jenkins 자동화 컨테이너 제작
대용량 패킷 컨텐츠 제작 -> 레이드, 경매장


Memo : 
ushort.maxValue 버퍼 값
채팅 패킷 (S_Chat): 넉넉잡아 200 바이트
65,535 / 200  ≈ 327개
LOH (Large Object Heap): 85,000 바이트 이상. (느린 GC, 단편화 발생 시 성능 저하 심함)


## 스트레스 테스트 결과 (2026-05-07)

### 테스트 방법
- **클라이언트**: DummyClient — 단계적 Ramp-up (100 → 200 → 400 → 800 동시 접속)
- **각 단계 유지 시간**: 3분
- **메트릭 수집 주기**: 5초 (Serilog 파일 sink)
- **수집 항목**: PacketsRecv/s, PacketsSent/s, TickAvg/Max(us), Players
- **빌드 환경**: .NET 10.0, Windows 11

### 결과 요약

| Stage | Build | Players | TickAvg | p95 TickAvg | p95 TickMax | Recv/s | Sent/s |
|-------|-------|---------|---------|-------------|-------------|--------|--------|
| Stage 1 | Debug   | 200 | 4.39 ms  | 5.45 ms  | 30.75 ms | 693   | 2,608 |
| Stage 1 | Release | 200 | 2.33 ms  | 2.62 ms  | 10.41 ms | 680   | 2,226 |
| Stage 2 | Debug   | 400 | 7.57 ms  | 8.45 ms  | 39.19 ms | 1,392 | 5,158 |
| Stage 2 | Release | 400 | 4.78 ms  | 5.20 ms  | 19.53 ms | 1,395 | 4,902 |
| Stage 3 | Debug   | 500 | 11.03 ms | 12.08 ms | 60.44 ms | 1,743 | 6,238 |
| Stage 3 | Release | 500 | 7.08 ms  | 7.80 ms  | 34.86 ms | 1,746 | 6,130 |

### 주요 관찰
- **Release 빌드 효과**: 평균 틱 35~47% 감소, p95 TickMax 42~66% 감소. JIT 최적화 효과가 분명히 측정됨
- **틱 레이턴시 선형 증가**: 플레이어 수에 비례해 평균 틱 시간 증가 (예: Release 200→500명에서 2.33→7.08 ms, 약 3배)
- **틱 처리율 일정**: 약 29 TPS로 안정적으로 유지 (게임 루프 rate-limited)
- **현재 한계 ~500명**: 800 목표였으나 양 빌드 모두 500에서 신규 접속 차단. 원인은 Listener Accept 풀/ThreadPool 워커 한계로 추정 — **추후 분석 및 개선 예정**

### 후속 과제
1. 500명 캡 원인 파악 (Listener backlog, ArrayPool, ThreadPool 워커 수 등)
2. 메트릭을 Elasticsearch + Kibana로 송출하여 실시간 시각화
3. 부하 시나리오 다양화 (이동/스킬/채팅 비율 조정)