# CSharpServer
CSharpSeveer Programming for portfolio


TODO LIST
1. Flat buffer -> Ring Buffer 변경


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
