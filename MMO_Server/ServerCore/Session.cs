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
        //Lock 대체용 원자성 플래그 (0: 대기중 , 1:전송중)
        int _sendRegistered = 0;
        int _socketClosed = 0;
        List<ArraySegment<byte>> _pendingList = new List<ArraySegment<byte>>();
        SocketAsyncEventArgs _sendArgs;
        SocketAsyncEventArgs _recvArgs;

        // I/O 참조 카운팅: 비동기 I/O가 진행 중인 동안 리소스 Dispose를 방지
        // 세션 활성(1) + 비동기 Recv(+1) + 비동기 Send(+1)
        // 모든 카운트가 해제되어 0이 되면 안전하게 리소스 정리
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

        public void Start(Socket socket)
        {
            _socket = socket;

            try { RemoteAddress = socket.RemoteEndPoint?.ToString(); }
            catch { RemoteAddress = null; }

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
                Disconnect();
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
                Disconnect();
                return;
            }

            if (Interlocked.Exchange(ref _sendRegistered, 1) == 0)
            {
                RegisterSend();
            }
        }

        public void Disconnect()
        {
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
                Disconnect();
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
                        Disconnect();
                        return false;
                    }

                    // 1. Write 커서 이동
                    if (recvBuffer.OnWrite(args.BytesTransferred) == false)
                    {
                        Disconnect();
                        return false;
                    }

                    // 2. 컨텐츠 쪽으로 데이터 넘기기 (패킷 파싱)
                    int processLen = OnRecvSpan(recvBuffer.ReadSpan);

                    if (processLen < 0 || recvBuffer.DataSize < processLen)
                    {
                        Disconnect();
                        return false;
                    }

                    // 3. Read 커서 이동 (처리한 만큼 버퍼 비우기)
                    if (recvBuffer.OnRead(processLen) == false)
                    {
                        Disconnect();
                        return false;
                    }

                    return true;
                }
                catch (Exception e)
                {
                    CoreLogger.Error("Net", e, "ProcessRecv failed. Remote={Remote}", RemoteAddress);
                }
            }

            // 에러 상황 or 0바이트 수신(연결 끊김)
            Disconnect();
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
                    Disconnect();
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
                Disconnect();
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
