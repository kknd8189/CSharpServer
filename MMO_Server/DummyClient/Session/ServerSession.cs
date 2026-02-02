using Google.Protobuf;
using Google.Protobuf.Protocol;
using ServerCore;
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Net;

public class ServerSession : PacketSession
{
	public int DummyId { get; set; }

	public void Send(IMessage packet)
	{
		string msgName = packet.Descriptor.Name.Replace("_", string.Empty);
		MsgId msgId = (MsgId)Enum.Parse(typeof(MsgId), msgName);
		int size = packet.CalculateSize();
		int totalSize = size + 4;

        //헤더 크기 4 byte
        Span<byte> sendBuffer = SendBufferHelper.Open(totalSize);
        BinaryPrimitives.WriteUInt16LittleEndian(sendBuffer.Slice(0), (ushort)totalSize);
        BinaryPrimitives.WriteUInt16LittleEndian(sendBuffer.Slice(2), (ushort)msgId);

        //byte[] sendBuffer = new byte[size + 4];
        //Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, sendBuffer, 0, sizeof(ushort));
        //Array.Copy(BitConverter.GetBytes((ushort)msgId), 0, sendBuffer, 2, sizeof(ushort));
        //Array.Copy(packet.ToByteArray(), 0, sendBuffer, 4, size);

        packet.WriteTo(sendBuffer.Slice(4));
        Send(SendBufferSpanHelper.Close(totalSize));
	}

	public override void OnConnected(EndPoint endPoint)
	{
		//Console.WriteLine($"OnConnected : {endPoint}");
	}

	public override void OnDisconnected(EndPoint endPoint)
	{
		//Console.WriteLine($"OnDisconnected : {endPoint}");
	}

	//public override void OnRecvPacket(ArraySegment<byte> buffer)
	//{
	//	PacketManager.Instance.OnRecvPacket(this, buffer);
	//}

    public override void OnRecvPacketSpan(ReadOnlySpan<byte> buffer)
    {
        PacketManager.Instance.OnRecvPacketSpan(this, buffer);
    }

    public override void OnSend(int numOfBytes)
	{
		//Console.WriteLine($"Transferred bytes: {numOfBytes}");
	}
}