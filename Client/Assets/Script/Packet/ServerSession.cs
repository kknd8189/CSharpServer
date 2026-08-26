using Protocol;
using ServerCore;
using System;
using System.Net;
using UnityEngine;

public class ServerSession : PacketSession
{
	// 서버 PacketSession의 10KB 상한과 동일
	const int MaxPacketSize = 10 * 1024;

	// PacketGenerator가 생성한 IPacket.Write로 직접 직렬화.
	// SendBufferSpanHelper가 60KB 청크에서 자리를 빌려주고, Close가 실제 사용분만 세그먼트로 잘라준다.
	public void Send(IPacket packet)
	{
		Span<byte> span = SendBufferSpanHelper.Open(MaxPacketSize);
		if (span.IsEmpty)
			return;

		packet.Write(span, out ushort size);
		ArraySegment<byte> sendBuffer = SendBufferSpanHelper.Close(size);
		Send(sendBuffer);
	}

	public override void OnConnected(EndPoint endPoint)
	{
		Debug.Log($"OnConnected : {endPoint}");

		// 수신 스레드에서 바로 핸들러를 부르지 않고 큐에 쌓는다.
		// 실제 처리는 Unity 메인 스레드(NetworkManager.Update)에서 — GameObject 조작은 메인 스레드 전용
		PacketManager.Instance.CustomHandler = (s, m, i) =>
		{
			PacketQueue.Instance.Push(i, m);
		};
	}

	public override void OnDisconnected(EndPoint endPoint)
	{
		Debug.Log($"OnDisconnected : {endPoint}");
	}

	public override void OnRecvPacketSpan(ReadOnlySpan<byte> buffer)
	{
		PacketManager.Instance.OnRecvPacketSpan(this, buffer);
	}

	public override void OnSend(int numOfBytes)
	{
	}
}
