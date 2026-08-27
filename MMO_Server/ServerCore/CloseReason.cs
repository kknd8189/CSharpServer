namespace ServerCore
{
    // 세션이 왜 끊겼는지. 운영에서 "이 유저 왜 튕겼나"에 답하려면 필요하다.
    // 지금까지는 전부 뭉뚱그려 "Player disconnected" 한 줄만 남아
    // 정상 종료인지 킥인지 네트워크 문제인지 구분할 수 없었다.
    public enum CloseReason
    {
        Unknown = 0,

        // 클라이언트가 정상적으로 연결을 종료
        Normal,

        // 0바이트 수신 — 상대가 소켓을 닫음 (앱 종료, 네트워크 단절 등)
        ClientClosed,

        // 소켓 I/O 예외
        NetworkError,

        // 패킷 파싱/버퍼 처리 실패. 조작 의심 구간이기도 하다.
        ProtocolError,

        // 송신 큐가 한계를 넘음 — 클라가 받아가지 못하고 있음
        SlowClient,

        // 핑 무응답
        PingTimeout,

        // 서버 판단으로 강제 종료 (어뷰징 누적 등)
        Kicked,

        // 인증 실패 (토큰 검증 실패 등)
        AuthFailed,

        // 서버 정상 종료 절차
        ServerShutdown,
    }
}
