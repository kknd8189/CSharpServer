using Serilog;
using Server.Game;
using ServerCore;
using System;
using System.Buffers.Binary;
using System.Net;
using Protocol;

namespace Server
{
    public partial class ClientSession : PacketSession
    {
        public PlayerServerState ServerState { get; private set; } = PlayerServerState.ServerStateLogin;
        public Player MyPlayer { get; set; }
        public int SessionId { get; set; }

        private string _clientIP = "Unknown";

        //object _lock = new object();
        //List<ArraySegment<byte>> _reserveQueue = new List<ArraySegment<byte>>();
        // 패킷 모아 보내기
        //int _reservedSendBytes = 0;
        //long _lastSendTick = 0;

        long _pingpongTick = 0;
        public void Ping()
        {
            // 세션이 이미 끊겼으면 여기서 체인을 끊는다.
            // 이 검사가 없으면 PushAfter 로 예약된 핑 잡이 종료된 세션에도 계속 돌아
            // 30초 뒤 "Ping timeout" 경고를 남긴다(이미 끊긴 뒤라 실제 조치는 없고 로그만 노이즈).
            if (Connected == false)
                return;

            if (_pingpongTick > 0)
            {
                long delta = (System.Environment.TickCount64 - _pingpongTick);
                if (delta > 30 * 1000)
                {
                    CoreLogger.Warn("Session",
                        "Ping timeout. Delta={DeltaMs}ms Limit={LimitMs}ms AccountId={AccountId} Remote={Remote}",
                        delta, 30 * 1000, AccountDbId, RemoteAddress);
                    Disconnect(CloseReason.PingTimeout);
                    return;
                }
            }

            S_Ping pingPacket = new S_Ping();
            Send(pingPacket);

            GameLogic.Instance.PushAfter(5000, Ping);
        }

        public void HandlePong()
        {
            _pingpongTick = System.Environment.TickCount64;
        }

        #region Network
        // 커스텀 제너레이터의 패킷 최대 크기. CLAUDE.md 기준 10KB 한도.
        // Write 전에 사이즈를 못 구하므로 한도만큼 예약 후 Close로 실제 사용분만 커밋.

        // IPacket을 SendBuffer에 "1회" 직렬화해 공유 가능한 세그먼트로 반환.
        // 반환 세그먼트는 여러 세션 큐에 그대로 넣어도 안전(쓰기 없음, 읽기 전용 공유).
        public static ArraySegment<byte> SerializeToSendBuffer(IPacket packet)
        {
            // 제너레이터가 구운 Write는 (size + msgId) 헤더를 span 선두에 직접 박고
            // 실제 기록된 바이트 수를 out size로 돌려준다.
            Span<byte> span = SendBufferSpanHelper.Open(MaxPacketSize);
            if (span.IsEmpty)
                return default;

            packet.Write(span, out ushort size);
            return SendBufferSpanHelper.Close(size);
        }

        // 이미 직렬화된 세그먼트를 이 세션으로 송신(Broadcast용).
        // Connected 체크 / 메트릭은 수신자별로 유지 → Sent/s 의미 동일.
        public void SendShared(ArraySegment<byte> segment)
        {
            if (Connected == false) return;

            ServerMetrics.IncrementPacketsSent();
            Send(segment);
        }

        // 예약만 하고 보내지는 않는다
        public void Send(IPacket packet)
        {
            if (Connected == false) return;

            ArraySegment<byte> pendingBuffer = SerializeToSendBuffer(packet);
            if (pendingBuffer.Array == null)
            {
                CoreLogger.Warn("Net", "SendBuffer open failed. Reserved={Reserved} Remote={Remote}", MaxPacketSize, RemoteAddress);
                return;
            }

            ServerMetrics.IncrementPacketsSent();

            Send(pendingBuffer);
            //lock (_lock)
            //{
            //    _reserveQueue.Add(pendingBuffer);
            //    _reservedSendBytes += totalSize;
            //}

            //byte[] sendBuffer = new byte[size + 4];
            //Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, sendBuffer, 0, sizeof(ushort));
            //Array.Copy(BitConverter.GetBytes((ushort)msgId), 0, sendBuffer, 2, sizeof(ushort));
            //Array.Copy(packet.ToByteArray(), 0, sendBuffer, 4, size);
            //lock (_lock)
            //{
            //	_reserveQueue.Add(sendBuffer);
            //	_reservedSendBytes += sendBuffer.Length;
            //}
        }

        // 실제 Network IO 보내는 부분
        //public void FlushSend()
        //{
        //	List<ArraySegment<byte>> sendList = null;
        //	lock (_lock)
        //	{
        //              if (_reserveQueue.Count == 0)
        //                  return;
        //              // 0.1초가 지났거나, 너무 패킷이 많이 모일 때 (1만 바이트)
        //              long delta = (System.Environment.TickCount64 - _lastSendTick);
        //		if (delta < 100 && _reservedSendBytes < 10000)
        //			return;
        //		// 패킷 모아 보내기
        //		_reservedSendBytes = 0;
        //		_lastSendTick = System.Environment.TickCount64;
        //		sendList = _reserveQueue;
        //		_reserveQueue = new List<ArraySegment<byte>>();
        //	}
        //	Send(sendList);
        //}

        public override void OnConnected(EndPoint endPoint)
        {
            ServerMetrics.IncrementSessionOpened();

            if(endPoint is IPEndPoint ipEndPoint)
            {
                _clientIP = ipEndPoint.Address.ToString();
            }
            {
                S_Connected connectedPacket = new S_Connected();
                Send(connectedPacket);
            }

            GameLogic.Instance.PushAfter(5000, Ping);
        }

        public string GetIpAddress()
        {
            return _clientIP;
        }

        //public override void OnRecvPacket(ArraySegment<byte> buffer)
        //{
        //	PacketManager.Instance.OnRecvPacket(this, buffer);
        //}

        public override void OnRecvPacketSpan(ReadOnlySpan<byte> buffer)
        {
            ServerMetrics.IncrementPacketsReceived();
            PacketManager.Instance.OnRecvPacketSpan(this, buffer);
        }

        public override void OnDisconnected(EndPoint endPoint)
        {
            // 세션 종료를 사유와 함께 남긴다. ServerCore 가 아니라 여기서 찍는 이유는
            // AccountDbId / PlayerDbId 같은 게임 컨텍스트가 이 계층에만 있기 때문.
            // 접속 유지 시간은 이탈 분석(진입 직후 이탈 vs 장시간 플레이)에 쓴다.
            ServerMetrics.RecordSessionClosed(CloseReason, ConnectedSeconds);

            Log.ForContext("EventType", "Session")
               .ForContext("CloseReason", CloseReason.ToString())
               .ForContext("AccountDbId", AccountDbId)
               .ForContext("PlayerDbId", MyPlayer?.PlayerDbId ?? 0)
               .ForContext("Remote", RemoteAddress)
               .Information("Session closed. Reason={CloseReason} DurationSec={DurationSec:F1} AccountId={AccountDbId}",
                   CloseReason, ConnectedSeconds, AccountDbId);

            SessionManager.Instance.Remove(this);

            GameLogic.Instance.Push(() =>
            {
                if (MyPlayer == null)
                    return;

                GameRoom room = GameLogic.Instance.Find(1);
                room.Push(room.LeaveGame, MyPlayer.Info.ObjectId);
            });

        }

        public override void OnSend(int numOfBytes)
        {
            //Console.WriteLine($"Transferred bytes: {numOfBytes}");
        }

        #endregion
    }
}
