using Protocol;
using ServerCore;
using System;
using System.Net;

public class ServerSession : PacketSession
{
	public int DummyId { get; set; }
	public int AccountId { get; set; }
	public string Token { get; set; }

	private const int MaxPacketSize = 10 * 1024;

	public void Send(IPacket packet)
	{
		Span<byte> span = SendBufferSpanHelper.Open(MaxPacketSize);
		if (span.IsEmpty) return;

		packet.Write(span, out ushort size);
		ArraySegment<byte> pendingBuffer = SendBufferSpanHelper.Close(size);
		Send(pendingBuffer);
	}

	public override void OnConnected(EndPoint endPoint)
	{
	}

	public override void OnDisconnected(EndPoint endPoint)
	{
	}

	public override void OnRecvPacketSpan(ReadOnlySpan<byte> buffer)
	{
		PacketManager.Instance.OnRecvPacketSpan(this, buffer);
	}

	public override void OnSend(int numOfBytes)
	{
	}
}
