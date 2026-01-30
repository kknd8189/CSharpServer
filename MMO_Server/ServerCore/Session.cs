using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Intrinsics.Arm;
using System.Threading;

namespace ServerCore
{
    //TODO : 하드코딩 된 상수들 Config로 빼기

    public abstract class PacketSession : Session
	{
		public static readonly int HeaderSize = 2;

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
      
            while(true)
            {
                // PendingList 채우기 , 큐에 있는 거 싹 긁어모으기
                // ConcurrentQueue는 Count가 정확하지 않을 수 있어서 TryDequeue로 뺌

                while (_sendQueue.TryDequeue(out ArraySegment<byte> buff))
                {
                    _pendingList.Add(buff);
                }

                // 보낼 게 있다! -> 루프 탈출해서 전송하러 감
                if (_pendingList.Count > 0)
                {
                    break;
                }

                // 일단 깃발을 내림 (1 -> 0) "나 퇴근한다"
                Interlocked.Exchange(ref _sendRegistered, 0);
                if (_sendQueue.IsEmpty == false)
                {
                    // 누가 넣었다! 다시 깃발 들기 시도 (0 -> 1) "퇴근 취소!"
                    if (Interlocked.Exchange(ref _sendRegistered, 1) == 0)
                    {
                        // 성공적으로 다시 깃발 잡음 -> 처음으로 돌아가서 다시 긁어모으자.
                        continue;
                    }
                }
                // 큐도 진짜 비었고, 깃발도 잘 내렸다. 완벽한 퇴근.
                return;
            }

            // [Send] 실제 전송 (비동기)
            // 여기까지 왔다는 건 _pendingList에 데이터가 있다는 뜻.
            // Scatter-Gather (한 번에 여러 버퍼 전송)
            _sendArgs.BufferList = _pendingList;

			try
			{
				bool pending = _socket.SendAsync(_sendArgs);
				if (pending == false)
					OnSendCompleted(null, _sendArgs); // [중요] 동기 완료 시 직접 호출
            }
			catch (Exception e)
			{
				Console.WriteLine($"RegisterSend Failed {e}");
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

            if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
            {
                try
                {
                    _sendArgs.BufferList = null;
                    _pendingList.Clear();

                    OnSend(_sendArgs.BytesTransferred);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"OnSendCompleted Failed {e}");
                }
                // "전송 끝났으니, 큐에 뭐 또 쌓였는지 보러 가자"
                RegisterSend();
            }
            else
            {
                Disconnect();
            }
        }

		void RegisterRecv()
		{
			if (_disconnected == 1)
				return;

			_recvBufferSpan.Clean();
			ArraySegment<byte> segment = _recvBufferSpan.WriteSegment;
			_recvArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

			try
			{
				bool pending = _socket.ReceiveAsync(_recvArgs);
				if (pending == false)
					OnRecvCompletedSpan(null, _recvArgs);
			}
			catch (Exception e)
			{
				Console.WriteLine($"RegisterRecv Failed {e}");
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
            if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
            {
                try
                {
                    // Write 커서 이동
                    if (_recvBufferSpan.OnWrite(args.BytesTransferred) == false)
                    {
                        Disconnect();
                        return;
                    }

                    // 컨텐츠 쪽으로 데이터를 넘겨주고 얼마나 처리했는지 받는다
                    int processLen = OnRecvSpan(_recvBufferSpan.ReadSpan);
                    if (processLen < 0 || _recvBufferSpan.DataSize < processLen)
                    {
                        Disconnect();
                        return;
                    }

                    // Read 커서 이동
                    if (_recvBufferSpan.OnRead(processLen) == false)
                    {
                        Disconnect();
                        return;
                    }

                    RegisterRecv();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"OnRecvCompleted Failed {e}");
                }
            }
            else
            {
                Disconnect();
            }
        }
        #endregion
    }
}
