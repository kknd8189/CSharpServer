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
                    // 로그 찍고 연결 끊어야 함 (해킹 의심)
                    Console.WriteLine($"[Error] Packet size too small: {dataSize}");
                    return -1; // -1을 리턴해서 Disconnect 유도
                }

                // [중요 2] 최대 사이즈 체크 (버퍼 오버플로우/먹통 방지)
                // 예: 우리 게임 패킷은 절대 10KB를 넘지 않는다.
                if (dataSize > 1024 * 10) // 10KB 제한
                {
                    Console.WriteLine($"[Error] Packet size too large: {dataSize}");
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
        Socket _socket;
        int _disconnected = 0;

        //RecvBuffer _recvBuffer = new RecvBuffer(65535);

        RecvBufferSpan _recvBufferSpan = new RecvBufferSpan(65535);

        //object _lock = new object();
        //Queue<ArraySegment<byte>> _sendQueue = new Queue<ArraySegment<byte>>();

        //MPSCQueue로 락프리 구현
        ConcurrentQueue<ArraySegment<byte>> _sendQueue = new ConcurrentQueue<ArraySegment<byte>>();
        //Lock 대체용 원자성 플래그 (0: 대기중 , 1:전송중)
        int _sendRegistered = 0;

        List<ArraySegment<byte>> _pendingList = new List<ArraySegment<byte>>();
        SocketAsyncEventArgs _sendArgs = new SocketAsyncEventArgs();
        SocketAsyncEventArgs _recvArgs = new SocketAsyncEventArgs();

        public abstract void OnConnected(EndPoint endPoint);
        //public abstract int  OnRecv(ArraySegment<byte> buffer);
        public abstract int OnRecvSpan(ReadOnlySpan<byte> buffer);
        public abstract void OnSend(int numOfBytes);
        public abstract void OnDisconnected(EndPoint endPoint);

        void Clear()
        {
            //lock (_lock)
            //{
            _sendQueue.Clear();
            _pendingList.Clear();
            //}
        }

        public void Start(Socket socket)
        {
            _socket = socket;

            _recvArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnRecvCompletedSpan);
            _sendArgs.Completed += new EventHandler<SocketAsyncEventArgs>(OnSendCompleted);

            RegisterRecv();
        }

        public void Send(List<ArraySegment<byte>> sendBuffList)
        {
            if (sendBuffList.Count == 0)
                return;

            //lock (_lock)
            //{
            //	foreach (ArraySegment<byte> sendBuff in sendBuffList)
            //		_sendQueue.Enqueue(sendBuff);

            //	
            //	if (_pendingList.Count == 0)
            //		RegisterSend();
            //}

            foreach (ArraySegment<byte> sendBuff in sendBuffList)
                _sendQueue.Enqueue(sendBuff);

            // 현재 전송 중인 패킷이 없다면 전송 시작
            if (Interlocked.Exchange(ref _sendRegistered, 1) == 0)
            {
                RegisterSend();
            }
        }

        public void Send(ArraySegment<byte> sendBuff)
        {
            //lock (_lock)
            //{
            //	_sendQueue.Enqueue(sendBuff);
            //	if (_pendingList.Count == 0)
            //		RegisterSend();
            //}

            _sendQueue.Enqueue(sendBuff);

            if (Interlocked.Exchange(ref _sendRegistered, 1) == 0)
            {
                RegisterSend();
            }
        }

        public void Disconnect()
        {
            // 중복 처리 방지
            if (Interlocked.Exchange(ref _disconnected, 1) == 1)
                return;

            OnDisconnected(_socket.RemoteEndPoint);

            _socket.Shutdown(SocketShutdown.Both);
            _socket.Close();

            _recvBufferSpan?.Dispose();
            _recvBufferSpan = null;

            Clear();

            _sendArgs.Dispose();
            _recvArgs.Dispose();
        }

        #region 네트워크 통신
        bool ProcessSendSuccess(SocketAsyncEventArgs args)
        {
            // SocketError가 Success가 아니거나, 보낸 바이트가 0이면 연결 끊긴 것으로 간주
            if (args.SocketError != SocketError.Success || args.BytesTransferred <= 0)
            {
                Disconnect();
                return false;
            }

            _sendArgs.BufferList = null;
            _pendingList.Clear();

            OnSend(_sendArgs.BytesTransferred);

            return true;
        }

        void RegisterSend()
        {
            if (_disconnected == 1)
                return;

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

                //Gather
                while (_sendQueue.TryDequeue(out ArraySegment<byte> buff))
                {
                    _pendingList.Add(buff);
                }

                // Check & Exit
                if (_pendingList.Count == 0)
                {
                    Interlocked.Exchange(ref _sendRegistered, 0);
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
                    bool pending = _socket.SendAsync(_sendArgs);

                    // 동기 완료 (즉시 전송됨)
                    if (pending == false)
                    {
                        //성공 여부 체크
                        if (ProcessSendSuccess(_sendArgs) == false)
                        {
                            // 실패했으면(Disconnect됨) 루프 종료
                            return;
                        }

                        // 성공했으면 루프 처음으로 돌아가서 다음 큐 처리
                        continue;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"RegisterSend Failed {e}");
                    Disconnect(); // SendAsync 자체에서 예외 나면 연결 끊기
                    return;
                }

                return;
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
            //			Console.WriteLine($"OnSendCompleted Failed {e}");
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
                    // 성공했다면 다음 큐 확인하러 가기
                    RegisterSend();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"OnSendCompleted Failed {e}");
                Disconnect();
            }
        }
        bool ProcessRecv(SocketAsyncEventArgs args)
        {
            if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
            {
                try
                {
                    // 1. Write 커서 이동
                    if (_recvBufferSpan.OnWrite(args.BytesTransferred) == false)
                    {
                        Disconnect();
                        return false;
                    }

                    // 2. 컨텐츠 쪽으로 데이터 넘기기 (패킷 파싱)
                    // OnRecvSpan 내부에서 처리한 만큼 길이를 리턴받음
                    int processLen = OnRecvSpan(_recvBufferSpan.ReadSpan);

                    if (processLen < 0 || _recvBufferSpan.DataSize < processLen)
                    {
                        Disconnect();
                        return false;
                    }

                    // 3. Read 커서 이동 (처리한 만큼 버퍼 비우기)
                    if (_recvBufferSpan.OnRead(processLen) == false)
                    {
                        Disconnect();
                        return false;
                    }

                    // 성공적으로 처리함
                    return true;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"ProcessRecv Failed {e}");
                }
            }

            // 에러 상황 or 0바이트 수신(연결 끊김)
            Disconnect();
            return false;
        }

        void RegisterRecv()
        {
            if (_disconnected == 1)
                return;

            // [핵심] 재귀 호출을 막기 위한 루프
            while (true)
            {
                // 1. 버퍼 정리 및 공간 확보
                _recvBufferSpan.Clean();
                ArraySegment<byte> segment = _recvBufferSpan.WriteSegment;
                _recvArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

                try
                {
                    // 2. 수신 요청
                    bool pending = _socket.ReceiveAsync(_recvArgs);

                    // 3. 동기 완료 (즉시 받음)
                    if (pending == false)
                    {
                        // 처리 로직 수행
                        if (ProcessRecv(_recvArgs))
                        {
                            // 성공했으면 루프를 돌면서 "즉시" 다시 수신 대기
                            continue;
                        }
                        else
                        {
                            // 연결 끊김 등으로 실패했으면 종료
                            return;
                        }
                    }

                    // 4. 비동기 완료 (Pending)
                    // IOCP 스레드가 나중에 OnRecvCompletedSpan을 호출해줌
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"RegisterRecv Failed {e}");
                    Disconnect(); // ReceiveAsync 자체 에러 처리
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
                // 받은 데이터 처리
                if (ProcessRecv(args))
                {
                    //  성공했으면 다시 수신 대기 루프 시작
                    RegisterRecv();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"OnRecvCompletedSpan Failed {e}");
                Disconnect();
            }
        }
        #endregion
    }
}
