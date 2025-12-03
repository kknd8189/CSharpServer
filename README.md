# CSharpServer
CSharpSeveer Programming for portfolio


TODO LIST
1. Flat buffer -> Ring Buffer 변경
2. 패킷처리 lock 기반 Queue에서 MPSC QUEUE로 변경
3. 보안로직 Game Server에 바로 붙지 못하게 LoginServer에서 토큰 발급 및 GameServer에 검증 절차 추가
4. 대용량 패킷 컨텐츠 제작 -> 레이드, 경매장
    4-1. Lock 기반 처리
    4-2. MPSC Queue 처리
    성능 비교

완료 List
2. DATABASE 연동
3. 더미클라이언트 기능 강화
4. UNITY 연동
5. 컨텐츠 제작
 5-1. 로그인
 5-2. World 서버 선택
 

           ┌─────────────────────┐
           │   ServerCore        │ ← 공통 기능(네트워킹, 로깅 등)
           └─────────────────────┘
                     ▲
                     │ (상속/확장)
                     ▼
           ┌─────────────────────┐
           │      Server         │ ← 어플리케이션 전용 기능(비즈니스 로직)
           └─────────────────────┘
