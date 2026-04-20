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