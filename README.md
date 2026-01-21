# CSharpServer
CSharpSeveer Programming for portfolio


TODO LIST
Cancle. Flat buffer -> Ring Buffer 변경 // 포인터 변경만으로 버퍼의 위치 이동이 가능한 C++ 과 달리 C# Array에서 사용하기 불편해서 Flat Buffer 유지
1. 패킷처리 lock 기반 Queue에서 MPSC QUEUE로 변경
2. 보안로직 Game Server에 바로 붙지 못하게 LoginServer에서 토큰 발급 및 GameServer에 검증 절차 추가
3. 대용량 패킷 컨텐츠 제작 -> 레이드, 경매장
    4-1. Lock 기반 처리
    4-2. MPSC Queue 처리
    성능 비교
4. 패킷처리부분 - byte 배열에서 span을 사용하여 복사비용을 들이지 않고 빠르게 처리할수 있는 방식 만들기
    - ProtoBuf에서 Span 관련된 인터페이스를 지원하지 않아서 Span-> Array로 Copy 비용 발생 // ServerPacketManager.cs MakerPacketSpan 함수 113줄 
    - Custom Packet Generate 제작 

5. Redis 및 EF Core + Dapper 사용할수 있는 구조 추가


완료 List
2. DATABASE 연동
3. 더미클라이언트 기능 강화
4. UNITY 연동
5. 컨텐츠 제작
 5-1. 로그인
 5-2. World 서버 선택
