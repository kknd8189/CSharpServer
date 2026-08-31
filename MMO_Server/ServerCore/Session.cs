using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ServerCore
{
    //TODO : 하드코딩 된 상수들 Config로 빼기

    public abstract class PacketSession : Session
    {
        public static readonly int HeaderSize = 2;

        // 우리 게임 패킷은 절대 이 크기를 넘지 않는다. 초과 시 버퍼 오버플로/먹통 방지로 끊는다.
        public const int MaxPacketSize = 1024 * 10;

        //legacy code
        //public sealed override int OnRecv(ArraySegment<byte> buffer)
        //{
        //	int processLen = 0;
        //	while (true)
        //	{
        //		// 최소한 헤더는 파싱할 수 있는지 확인
        //		if (buffer.Count < HeaderSize)
        //			break;
        //		// 패킷이 완전체로 도착했는지 확인
        //		ushort dataSize = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
        //		if (buffer.Count < dataSize)
        //			break;
        //		// 여기까지 왔으면 패킷 조립 가능
        //		OnRecvPacket(new ArraySegment<byte>(buffer.Array, buffer.Offset, dataSize));
        //		processLen += dataSize;
        //		buffer = new ArraySegment<byte>(buffer.Array, buffer.Offset + dataSize, buffer.Count - dataSize);
        //	}
        //	return processLen;
        //}

        public sealed override int OnRecvSpan(ReadOnlySpan<byte> buffer)
        {
            int processLen = 0;
            while (true)
            {
                // 최소한 헤더는 파싱할 수 있는지 확인
                if (buffer.Length < HeaderSize)
                    break;

                //BinaryPrimitives.ReadUInt16LittleEndian
                //; [x64 Assembly]
                //; 그냥 메모리 주소(rdx)에서 2바이트(word)를 긁어서 레지스터(eax)에 넣음
                //movzx eax, word ptr[rdx]

                //BinaryPrimitives.ReadUInt16BigEndian
                //; 1.일단 가져옴
                //movzx eax, word ptr[rdx]
                //; 2.바이트 순서를 뒤집음(롤/ Swap)
                //rol ax, 8; 0x1234-> 0x3412로 뒤집기
                //; 추가 비용이 1 Cycle 발생하지만, 의도가 명확합니다.

                ushort dataSize = BinaryPrimitives.ReadUInt16LittleEndian(buffer);

                // [중요 1] 최소 사이즈 체크 (무한 루프 방지)
                // 헤더 크기(2바이트)보다 데이터가 작을 순 없음. (혹은 약속된 최소 크기)
                if (dataSize < HeaderSize)
                {
                    // 정상 클라이언트는 절대 만들 수 없는 패킷 → 조작 의심.
                    // Abuse 로 태깅해 ES 에서 어뷰징 집계/알럿 대상이 되게 한다.
                    CoreLogger.Warn("Abuse",
                        "Packet size too small. Size={PacketSize} Min={MinSize} Remote={Remote}",
                        dataSize, HeaderSize, RemoteAddress);
                    return -1; // -1을 리턴해서 Disconnect 유도
                }

                // [중요 2] 최대 사이즈 체크 (버퍼 오버플로우/먹통 방지)
                // 예: 우리 게임 패킷은 절대 10KB를 넘지 않는다.
                if (dataSize > MaxPacketSize) // 10KB 제한
                {
                    CoreLogger.Warn("Abuse",
                        "Packet size too large. Size={PacketSize} Max={MaxSize} Remote={Remote}",
                        dataSize, MaxPacketSize, RemoteAddress);
                    return -1;
                }

                //패킷 완전체가 도착했는지 확인 (패킷 크기만큼 데이터가 있는지)
                if (buffer.Length < dataSize)
                    break;

                OnRecvPacketSpan(buffer.Slice(0, dataSize));
                processLen += dataSize;
                buffer = buffer.Slice(dataSize);
            }

            return processLen;
        }

        //public abstract void OnRecvPacket(ArraySegment<byte> buffer);

        public abstract void OnRecvPacketSpan(ReadOnlySpan<byte> buffer);

    }


    public abstract class Session
    {
        protected bool Connected { get { return Volatile.Read(ref _disconnected) == 0; } }
        protected Socket _socket;

        int _disconnected = 0;
        RecvBufferSpan _recvBufferSpan;

        //MPSCQueue로 락프리 구현
        ConcurrentQueue<ArraySegment<byte>> _sendQueue = new ConcurrentQueue<ArraySegment<byte>>();
        // Send Queue 최대 크기 - 초과 시 느린 클라이언트로 판단하고 연결 끊음
        // ConcurrentQueue.Count는 O(N) 스냅샷이므로, 별도의 원자적 카운터로 추적
        int _sendQueueCount = 0;
        const int MAX_SEND_QUEUE_SIZE = 1000;
        // [단일 송신자 계약] Lock 대체용 원자성 플래그 (0: 대기중 , 1:전송중)
        //
        // Interlocked.Exchange(ref _sendRegistered, 1) 은 "1 을 넣고, 넣기 전 값을 돌려준다" 를
        // CPU 명령 하나로 끊기지 않게 처리한다. 그래서 N 개 스레드가 동시에 도달해도
        // 0 을 돌려받는 스레드는 정확히 하나뿐이고, 그 하나만 실제 전송을 담당한다.
        //
        // 락과의 차이: 락은 "기다렸다가 들어가는" 것이고 이건 "이미 있으면 그냥 가는" 것이다.
        // 대기가 없으니 IOCP 스레드가 블로킹되지 않는다.
        int _sendRegistered = 0;
        int _socketClosed = 0;
        // 큐(입구)와 소켓(출구) 사이의 적재 공간. 이번 전송에 실을 짐을 여기 모은다.
        // 생명주기: RegisterSend 에서 Add → _sendArgs.BufferList 로 전달 → ProcessSendSuccess 에서 Clear.
        // Clear 는 내부 배열을 유지하고 길이만 0 으로 만들어서 다음 배치에 재할당이 없다.
        //
        // ConcurrentQueue 가 아니라 평범한 List 인 이유:
        // _sendRegistered 가 "이 리스트는 한 스레드만 만진다" 를 보장하기 때문이다.
        // 경합을 없애면 자료구조를 단순한 걸 쓸 수 있다 — 락프리 설계의 부수 효과.
        List<ArraySegment<byte>> _pendingList = new List<ArraySegment<byte>>();
        SocketAsyncEventArgs _sendArgs;
        SocketAsyncEventArgs _recvArgs;

        // I/O 참조 카운팅: 비동기 I/O가 진행 중인 동안 리소스 Dispose를 방지
        // 세션 활성(1) + 비동기 Recv(+1) + 비동기 Send(+1)
        // 모든 카운트가 해제되어 0이 되면 안전하게 리소스 정리
        //
        // _ioCount == 0 은 "세션도 닫혔고 커널에 걸린 I/O 도 전부 끝났다" 는 뜻이고,
        // 이게 ArrayPool 에 수신 버퍼를 돌려줘도 되는 유일한 안전 신호다.
        //
        // 0 이 아니라 1 로 시작하는 이유:
        // Recv 완료(1→0) 와 다음 RegisterRecv(0→1) 사이에는 걸린 I/O 가 하나도 없는 찰나가 생긴다.
        // 0 에서 시작하면 그 순간 카운트가 0 을 찍어 버퍼와 SAEA 가 파괴되고,
        // 멀쩡히 살아있는 세션이 파괴된 자원을 다시 쓰게 된다.
        // 그래서 세션 자신이 참조 1 개를 들고 있고, 그것은 CloseSocket 에서만 놓는다.
        int _ioCount = 1; // 세션 자체의 참조 (CloseSocket에서 해제)

        public abstract void OnConnected(EndPoint endPoint);
        //public abstract int  OnRecv(ArraySegment<byte> buffer);
        public abstract int OnRecvSpan(ReadOnlySpan<byte> buffer);
        public abstract void OnSend(int numOfBytes);
        public abstract void OnDisconnected(EndPoint endPoint);

        void Clear()
        {
            _sendQueue.Clear();
            _pendingList.Clear();
        }

        // I/O 참조 카운트를 감소시키고, 0이 되면 리소스 정리
        // CloseSocket, OnRecvCompleted, OnSendCompleted 등에서 호출
        void ReleaseIO()
        {
            if (Interlocked.Decrement(ref _ioCount) == 0)
            {
                _sendArgs.Dispose();
                _recvArgs.Dispose();
                _recvBufferSpan?.Dispose();
                _recvBufferSpan = null;
            }
        }

        // 위반/오류 로그에서 "누가"를 식별하기 위한 캐시.
        // 소켓이 닫힌 뒤 RemoteEndPoint 를 읽으면 ObjectDisposedException 이 나므로
        // 접속 시점에 한 번 문자열로 떠 둔다.
        public string RemoteAddress { get; private set; }

        int _closeReason = (int)CloseReason.Unknown;
        public CloseReason CloseReason { get { return (CloseReason)Volatile.Read(ref _closeReason); } }

        long _connectedTick;
        // 접속 유지 시간(초). 세션 종료 로그에 남겨 이탈 분석에 쓴다.
        public double ConnectedSeconds
        {
            get { return _connectedTick == 0 ? 0 : (Environment.TickCount64 - _connectedTick) / 1000.0; }
        }

        public void Start(Socket socket)
        {
            _socket = socket;

            try { RemoteAddress = socket.RemoteEndPoint?.ToString(); }
            catch { RemoteAddress = null; }

            _closeReason = (int)CloseReason.Unknown;
            _connectedTick = Environment.TickCount64;

            // 상태 초기화 (세션 재사용 시 이전 상태가 남아있지 않도록)
            _disconnected = 0;
            _socketClosed = 0;
            _sendRegistered = 0;
            _ioCount = 1;
            _sendQueueCount = 0;
            _recvBufferSpan = new RecvBufferSpan(65535);

            // 매 Start마다 새 SocketAsyncEventArgs 생성 + 이벤트 등록
            // ReleaseIO에서 이전 args를 Dispose하므로, 재사용 시 새로 만들어야 함
            // 새 객체에 += 하는 것이므로 이벤트 핸들러 누적 문제 없음
            _sendArgs = new SocketAsyncEventArgs();
            _recvArgs = new SocketAsyncEventArgs();
            _sendArgs.Completed += OnSendCompleted;
            _recvArgs.Completed += OnRecvCompletedSpan;

            RegisterRecv();
        }

        //실제 소켓을 닫은 함수를 따로 분리
        public void CloseSocket()
        {
            if (_socket == null) return;

            if (Interlocked.Exchange(ref _socketClosed, 1) == 1)
                return;

            EndPoint endPoint = null;
            try { endPoint = _socket.RemoteEndPoint; }
            catch { }

            try
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }   
            catch {}

            Clear();

            _socket = null;

            OnDisconnected(endPoint);

            // 세션 참조 해제 (비동기 I/O가 아직 진행 중이면 _ioCount > 0이므로 Dispose되지 않음)
            // 마지막 IOCP 콜백이 완료될 때 _ioCount가 0이 되면서 Dispose 실행
            ReleaseIO();
        }

        public void Send(List<ArraySegment<byte>> sendBuffList)
        {
            if (sendBuffList.Count == 0)
                return;

            foreach (ArraySegment<byte> sendBuff in sendBuffList)
                _sendQueue.Enqueue(sendBuff);

            // 원자적 카운터로 큐 크기 추적
            if (Interlocked.Add(ref _sendQueueCount, sendBuffList.Count) > MAX_SEND_QUEUE_SIZE)
            {
                // 여태 조용히 끊고 있었다. 운영에서 "이 유저 왜 튕겼나"에 답하려면 사유가 남아야 한다.
                CoreLogger.Warn("Session",
                    "Slow client kicked. QueueSize={QueueSize} Limit={Limit} Remote={Remote}",
                    Volatile.Read(ref _sendQueueCount), MAX_SEND_QUEUE_SIZE, RemoteAddress);
                Disconnect(CloseReason.SlowClient);
                return;
            }

            if (Interlocked.Exchange(ref _sendRegistered, 1) == 0)
            {
                RegisterSend();
            }
        }

        public void Send(ArraySegment<byte> sendBuff)
        {
            _sendQueue.Enqueue(sendBuff);

            if (Interlocked.Increment(ref _sendQueueCount) > MAX_SEND_QUEUE_SIZE)
            {
                CoreLogger.Warn("Session",
                    "Slow client kicked. QueueSize={QueueSize} Limit={Limit} Remote={Remote}",
                    Volatile.Read(ref _sendQueueCount), MAX_SEND_QUEUE_SIZE, RemoteAddress);
                Disconnect(CloseReason.SlowClient);
                return;
            }

            // 0 을 받은 스레드만 전송을 담당한다. 1 을 받은 스레드는 큐에 넣기만 하고 빠진다.
            // 방금 넣은 그 패킷은 지금 전송 중인 스레드가 다음 드레인에서 같이 가져간다.
            //
            // 그래서 배치 크기가 부하에 따라 스스로 조절된다:
            //   한가하면 → 큐에 나 혼자 → 즉시 전송 (지연 최소)
            //   바쁘면   → 전송 도중 쌓인 것들이 한 번에 묶여 나감 (syscall 절약)
            // 틱 단위 고정 병합과 달리 병합 주기를 고를 필요가 없다.
            if (Interlocked.Exchange(ref _sendRegistered, 1) == 0)
            {
                RegisterSend();
            }
        }

        public void Disconnect(CloseReason reason = CloseReason.Normal)
        {
            // 최초 호출의 사유만 남긴다.
            // 끊는 도중 송신 실패 등으로 Disconnect 가 연쇄 호출되는데,
            // 그때 NetworkError 가 진짜 원인(예: Kicked)을 덮어쓰면 안 된다.
            Interlocked.CompareExchange(ref _closeReason, (int)reason, (int)CloseReason.Unknown);

            // 1. "종료 모드"로 전환 (Flag On)
            if (Interlocked.Exchange(ref _disconnected, 1) == 1)
                return;

            // 2. 소켓을 바로 닫는 게 아니라, 전송 큐를 확인하러 보냄
            // - 만약 전송 중이라면: 그 스레드가 다 보내고 닫을 것임
            // - 만약 쉬고 있다면: 지금 깨워서 남은 거 보내고 닫게 함
            if (Interlocked.Exchange(ref _sendRegistered, 1) == 0)
            {
                RegisterSend();
            }
        }

        #region 네트워크 통신
        bool ProcessSendSuccess(SocketAsyncEventArgs args)
        {
            // SocketError가 Success가 아니거나, 보낸 바이트가 0이면 연결 끊긴 것으로 간주
            if (args.SocketError != SocketError.Success || args.BytesTransferred <= 0)
            {
                CloseSocket();
                return false;
            }

            _sendArgs.BufferList = null;
            _pendingList.Clear();

            OnSend(_sendArgs.BytesTransferred);

            return true;
        }

        void RegisterSend()
        {
            if (Volatile.Read(ref _disconnected ) == 1 && _sendQueue.IsEmpty && _pendingList.Count == 0)
            {
                CloseSocket();
                return;
            }

            //지역 변수 스냅샷
            List<ArraySegment<byte>> pendingList = _pendingList;

            // 큐에 있는 모든 패킷을 리스트로 이동 (Batching)
            //         while (_sendQueue.Count > 0)
            //{
            //	ArraySegment<byte> buff = _sendQueue.Dequeue();
            //	_pendingList.Add(buff);
            //}

            while (true)
            {
                // PendingList 채우기 , 큐에 있는 거 싹 긁어모으기
                // ConcurrentQueue는 Count가 정확하지 않을 수 있어서 TryDequeue로 뺌

                //Gather - 큐에서 꺼낸 만큼 카운터 감소
                int dequeued = 0;
                while (_sendQueue.TryDequeue(out ArraySegment<byte> buff))
                {
                    pendingList.Add(buff);
                    dequeued++;
                }
                if (dequeued > 0)
                    Interlocked.Add(ref _sendQueueCount, -dequeued);

                // Check & Exit
                if (pendingList.Count == 0)
                {
                    //더 보낼 건 없는데, "종료 예약"이 걸려있다?
                    if (Volatile.Read(ref _disconnected) == 1)
                    {
                        CloseSocket(); // 여기서 진짜 종료!
                        return;
                    }

                    //종료 예약도 없고, 보낼 것도 없으면 대기 상태로
                    Interlocked.Exchange(ref _sendRegistered, 0);

                    // (Double Check) 그 사이에 누가 또 넣었으면 다시 시작
                    //
                    // [lost wakeup] 이 재확인이 없으면 "아무도 전송하지 않는" 상태가 만들어진다.
                    //
                    //   나 (전송 담당)                다른 스레드
                    //   ─────────────────────────────────────────────────────────
                    //   큐 비었음 확인
                    //                                 Enqueue(패킷)
                    //                                 Exchange(1) → 1 "누가 하고 있네" → 리턴
                    //   Exchange(0) 플래그 반납
                    //   return                        ← 큐에 패킷이 남았는데 담당자가 없다
                    //
                    // 그래서 플래그를 내린 뒤 큐를 한 번 더 본다. 비어있지 않으면 다시 잡는다.
                    // 그 사이 제3의 스레드가 먼저 잡았다면 아래 Exchange 가 1 을 반환하므로
                    // 나는 그냥 return 한다 — 어느 경우든 반드시 누군가는 처리한다.
                    if (_sendQueue.IsEmpty == false)
                    {
                        if (Interlocked.Exchange(ref _sendRegistered, 1) == 0)
                            continue;
                    }
                    return;
                }

                // Send
                _sendArgs.BufferList = _pendingList;

                try
                {
                    // [I/O 참조 카운팅] SendAsync 호출 전에 카운트 증가
                    Interlocked.Increment(ref _ioCount);

                    bool pending = _socket.SendAsync(_sendArgs);

                    // 동기 완료 (즉시 전송됨) - IOCP 콜백이 오지 않으므로 카운트 원복
                    if (pending == false)
                    {
                        Interlocked.Decrement(ref _ioCount);

                        if (ProcessSendSuccess(_sendArgs) == false)
                        {
                            return;
                        }

                        continue;
                    }
                }
                catch (Exception e)
                {
                    // SendAsync 자체 예외: 증가시킨 카운트 원복
                    Interlocked.Decrement(ref _ioCount);
                    CoreLogger.Error("Net", e, "RegisterSend failed. Remote={Remote}", RemoteAddress);
                    CloseSocket();
                    return;
                }

                return; // Pending 상태면 종료 (콜백의 finally에서 ReleaseIO)
            }
        }

        void OnSendCompleted(object sender, SocketAsyncEventArgs args)
        {
            //lock (_lock)
            //{
            //	if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
            //	{
            //		try
            //		{
            //			_sendArgs.BufferList = null;
            //			_pendingList.Clear();

            //			OnSend(_sendArgs.BytesTransferred);

            //			if (_sendQueue.Count > 0)
            //				RegisterSend();
            //		}
            //		catch (Exception e)
            //		{
            //			CoreLogger.Error("Net", e, "OnSendCompleted failed. Remote={Remote}", RemoteAddress);
            //		}
            //	}
            //	else
            //	{
            //		Disconnect();
            //	}
            //}
            try
            {
                if (ProcessSendSuccess(args))
                {
                    RegisterSend();
                }
            }
            catch (Exception e)
            {
                CoreLogger.Error("Net", e, "OnSendCompleted failed. Remote={Remote}", RemoteAddress);
                Disconnect(CloseReason.NetworkError);
            }
            finally
            {
                ReleaseIO();
            }
        }
        bool ProcessRecv(SocketAsyncEventArgs args)
        {
            if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
            {
                try
                {
                    // [경합 방어] 로컬 변수로 캡처
                    // CloseSocket()이 다른 스레드에서 _recvBufferSpan = null을 할 수 있으므로
                    // 필드를 직접 반복 접근하면 도중에 null이 될 수 있다.
                    // 로컬 변수에 한 번 캡처하면, 이후 null 체크 한 번으로 안전하게 사용 가능.
                    var recvBuffer = _recvBufferSpan;
                    if (recvBuffer == null)
                    {
                        Disconnect(CloseReason.ProtocolError);
                        return false;
                    }

                    // 1. Write 커서 이동
                    if (recvBuffer.OnWrite(args.BytesTransferred) == false)
                    {
                        Disconnect(CloseReason.ProtocolError);
                        return false;
                    }

                    // 2. 컨텐츠 쪽으로 데이터 넘기기 (패킷 파싱)
                    int processLen = OnRecvSpan(recvBuffer.ReadSpan);

                    if (processLen < 0 || recvBuffer.DataSize < processLen)
                    {
                        Disconnect(CloseReason.ProtocolError);
                        return false;
                    }

                    // 3. Read 커서 이동 (처리한 만큼 버퍼 비우기)
                    if (recvBuffer.OnRead(processLen) == false)
                    {
                        Disconnect(CloseReason.ProtocolError);
                        return false;
                    }

                    return true;
                }
                catch (Exception e)
                {
                    CoreLogger.Error("Net", e, "ProcessRecv failed. Remote={Remote}", RemoteAddress);
                }
            }

            // 에러 상황 or 0바이트 수신(연결 끊김).
            // 위쪽 예외 경로에서 이미 사유를 정했다면 CompareExchange 가 덮어쓰지 않는다.
            Disconnect(CloseReason.ClientClosed);
            return false;
        }

        void RegisterRecv()
        {
            if (Volatile.Read(ref _disconnected) == 1)
                return;

            // [핵심] 재귀 호출을 막기 위한 루프
            while (true)
            {
                // [경합 방어] 로컬 변수 캡처 + null 체크
                // CloseSocket()이 다른 스레드에서 실행되면 _recvBufferSpan이 null이 될 수 있음
                var recvBuffer = _recvBufferSpan;
                if (recvBuffer == null)
                    return;

                // 1. 버퍼 정리 및 공간 확보
                recvBuffer.Clean();
                ArraySegment<byte> segment = recvBuffer.WriteSegment;
                _recvArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

                try
                {
                    // [I/O 참조 카운팅] ReceiveAsync 호출 전에 카운트 증가
                    // 반드시 호출 "전에" 증가시켜야 함
                    // 이유: ReceiveAsync가 pending 반환 후 IOCP 콜백이 다른 스레드에서
                    //       즉시 실행될 수 있음. 콜백의 ReleaseIO보다 먼저 증가해야 올바른 카운트 유지
                    //
                    // 뒤로 옮기면 이렇게 된다:
                    //
                    //   나                             IOCP 스레드
                    //   ─────────────────────────────────────────────────────────
                    //   ReceiveAsync 호출 (커널 등록)
                    //                                  데이터 도착 → 콜백 → ReleaseIO()
                    //                                  1 → 0 → 버퍼 ArrayPool 반납, SAEA Dispose
                    //   ← 이제서야 리턴
                    //   Increment (0 → 1)              ← 이미 파괴된 자원에 뒤늦게 소유권 주장
                    //
                    // 세션은 살아있는데 자원만 사라진다. 게다가 반납된 버퍼를 다른 세션이 빌려가면
                    // 커널이 아직 쓰고 있는 배열을 그 세션이 수신 버퍼로 쓰게 된다(세션 간 오염).
                    // 크래시가 안 나고, 범인(A)과 증상(B)이 다른 세션에서 나타나서 추적이 어렵다.
                    //
                    // 원칙: 참조 카운트는 "위험이 시작되기 전에" 올린다.
                    // 비동기 등록의 순간부터 그 작업은 이미 내 통제 밖이다.
                    //
                    // 대신 앞에서 올렸으니 콜백이 오지 않는 경로는 손으로 원복해야 한다.
                    //   pending == true  → 콜백의 ReleaseIO 가 감소  ┐
                    //   pending == false → 아래에서 직접 Decrement   ├ 모든 경로에서 정확히 1 회
                    //   예외             → catch 에서 Decrement      ┘
                    // 이 "증가 1 회 : 감소 1 회" 불변식이 깨지면 누수 아니면 조기 해제다.
                    Interlocked.Increment(ref _ioCount);

                    // 2. 수신 요청
                    bool pending = _socket.ReceiveAsync(_recvArgs);

                    // 3. 동기 완료 (즉시 받음) - IOCP 콜백이 오지 않으므로 카운트 원복
                    if (pending == false)
                    {
                        Interlocked.Decrement(ref _ioCount);

                        if (ProcessRecv(_recvArgs))
                        {
                            continue;
                        }
                        else
                        {
                            return;
                        }
                    }

                    // 4. 비동기 완료 (Pending)
                    // IOCP 콜백(OnRecvCompletedSpan)의 finally에서 ReleaseIO로 카운트 감소
                    break;
                }
                catch (Exception e)
                {
                    // ReceiveAsync 자체 예외: 증가시킨 카운트 원복
                    Interlocked.Decrement(ref _ioCount);
                    CoreLogger.Error("Net", e, "RegisterRecv failed. Remote={Remote}", RemoteAddress);
                    Disconnect(CloseReason.NetworkError);
                    return;
                }
            }
        }

        //void OnRecvCompleted(object sender, SocketAsyncEventArgs args)
        //{
        //if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
        //{
        //	try
        //	{
        //		// Write 커서 이동
        //		if (_recvBufferSpan.OnWrite(args.BytesTransferred) == false)
        //		{
        //			Disconnect();
        //			return;
        //		}

        //		// 컨텐츠 쪽으로 데이터를 넘겨주고 얼마나 처리했는지 받는다
        //		int processLen = OnRecv(_recvBuffer.ReadSegment);
        //                 if (processLen < 0 || _recvBuffer.DataSize < processLen)
        //		{
        //			Disconnect();
        //			return;
        //		}

        //		// Read 커서 이동
        //		if (_recvBuffer.OnRead(processLen) == false)
        //		{
        //			Disconnect();
        //			return;
        //		}

        //		RegisterRecv();
        //	}
        //	catch (Exception e)
        //	{
        //		Console.WriteLine($"OnRecvCompleted Failed {e}");
        //	}
        //}
        //else
        //{
        //	Disconnect();
        //}
        //}
        void OnRecvCompletedSpan(object sender, SocketAsyncEventArgs args)
        {
            try
            {
                if (ProcessRecv(args))
                {
                    RegisterRecv();
                }
            }
            catch (Exception e)
            {
                CoreLogger.Error("Net", e, "OnRecvCompletedSpan failed. Remote={Remote}", RemoteAddress);
                Disconnect(CloseReason.NetworkError);
            }
            finally
            {
                // 이 IOCP 콜백에 대한 I/O 참조 해제
                // RegisterRecv에서 새 ReceiveAsync를 시작했다면 이미 새로운 Increment가 됨
                // 따라서 여기서 Decrement해도 새 I/O의 참조는 유지됨
                ReleaseIO();
            }
        }
        #endregion
    }
}
