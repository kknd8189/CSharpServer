using Google.Protobuf;
using Google.Protobuf.Protocol;
using ServerCore;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;

class PacketManager
{
	#region Singleton
	static PacketManager _instance = new PacketManager();
	public static PacketManager Instance { get { return _instance; } }
	#endregion

	PacketManager()
	{
		Register();
	}

	Dictionary<ushort, Action<PacketSession, ArraySegment<byte>, ushort>> _onRecv = new Dictionary<ushort, Action<PacketSession, ArraySegment<byte>, ushort>>();
	Dictionary<ushort, Action<PacketSession, IMessage>> _handler = new Dictionary<ushort, Action<PacketSession, IMessage>>();
		
	public Action<PacketSession, IMessage, ushort> CustomHandler { get; set; }

    // Action<T> 대신 Span을 받을 수 있는 커스텀 델리게이트 선언
    // Span은 제네릭 타입 인자로 사용할 수 없기 때문
    public delegate void PacketHandlerSpan(PacketSession session, ReadOnlySpan<byte> buffer, ushort id);
    Dictionary<ushort, PacketHandlerSpan> _onRecvSpan = new Dictionary<ushort, PacketHandlerSpan>();

    public void Register()
	{		
		//_onRecv.Add((ushort)MsgId.CMove, MakePacketSpan<C_Move>);
		_handler.Add((ushort)MsgId.CMove, PacketHandler.C_MoveHandler);		
		//_onRecv.Add((ushort)MsgId.CSkill, MakePacketSpan<C_Skill>);
		_handler.Add((ushort)MsgId.CSkill, PacketHandler.C_SkillHandler);		
		//_onRecv.Add((ushort)MsgId.CLogin, MakePacketSpan<C_Login>);
		_handler.Add((ushort)MsgId.CLogin, PacketHandler.C_LoginHandler);		
		//_onRecv.Add((ushort)MsgId.CEnterGame, MakePacketSpan<C_EnterGame>);
		_handler.Add((ushort)MsgId.CEnterGame, PacketHandler.C_EnterGameHandler);		
		//_onRecv.Add((ushort)MsgId.CCreatePlayer, MakePacketSpan<C_CreatePlayer>);
		_handler.Add((ushort)MsgId.CCreatePlayer, PacketHandler.C_CreatePlayerHandler);		
		//_onRecv.Add((ushort)MsgId.CEquipItem, MakePacketSpan<C_EquipItem>);
		_handler.Add((ushort)MsgId.CEquipItem, PacketHandler.C_EquipItemHandler);		
		//_onRecv.Add((ushort)MsgId.CPong, MakePacketSpan<C_Pong>);
		_handler.Add((ushort)MsgId.CPong, PacketHandler.C_PongHandler);

		_onRecvSpan.Add((ushort)MsgId.CMove, MakePacketSpan<C_Move>);
		_onRecvSpan.Add((ushort)MsgId.CSkill, MakePacketSpan<C_Skill>);
		_onRecvSpan.Add((ushort)MsgId.CLogin, MakePacketSpan<C_Login>);
		_onRecvSpan.Add((ushort)MsgId.CEnterGame, MakePacketSpan<C_EnterGame>);
		_onRecvSpan.Add((ushort)MsgId.CCreatePlayer, MakePacketSpan<C_CreatePlayer>);
		_onRecvSpan.Add((ushort)MsgId.CEquipItem, MakePacketSpan<C_EquipItem>);
		_onRecvSpan.Add((ushort)MsgId.CPong, MakePacketSpan<C_Pong>);
    }

	public void OnRecvPacket(PacketSession session, ArraySegment<byte> buffer)
	{
		ushort count = 0;

		ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
		count += 2;
		ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);
		count += 2;

		Action<PacketSession, ArraySegment<byte>, ushort> action = null;
		if (_onRecv.TryGetValue(id, out action))
			action.Invoke(session, buffer, id);
	}

    public void OnRecvPacketSpan(PacketSession session, ReadOnlySpan<byte> buffer)
    {
        ushort count = 0;

        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(count));
        count += 2;

        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(count));
        count += 2;

        if (_onRecvSpan.TryGetValue(id, out PacketHandlerSpan action))
        {
            // Span을 그대로 넘겨주어 복사 비용 '0' 유지
            action.Invoke(session, buffer, id);
        }
    }

 //   void MakePacket<T>(PacketSession session, ArraySegment<byte> buffer, ushort id) where T : IMessage, new()
	//{
	//	T pkt = new T();
	//	pkt.MergeFrom(buffer.Array, buffer.Offset + 4, buffer.Count - 4);

	//	if (CustomHandler != null)
	//	{
	//		CustomHandler.Invoke(session, pkt, id);
	//	}
	//	else
	//	{
	//		Action<PacketSession, IMessage> action = null;
	//		if (_handler.TryGetValue(id, out action))
	//			action.Invoke(session, pkt);
	//	}
	//}
	                                        
	void MakePacketSpan<T>(PacketSession session, ReadOnlySpan<byte> buffer, ushort id) where T : IMessage, new()
	{
        // 힙 할당 비용은 없지만 Copy 비용 발생..... ProtoBuf 인터페이스에서 Span 기능 제공 안함
        // TODO : Custom Packet Generator를 제작후 Zero Allocation  , Zero Copy를 구현
        int payloadSize = buffer.Length - 4;
        byte[] rentBuffer = ArrayPool<byte>.Shared.Rent(payloadSize);
        try
        {
            buffer.Slice(4, payloadSize).CopyTo(rentBuffer);

            T pkt = new T();

            pkt.MergeFrom(new CodedInputStream(rentBuffer, 0, payloadSize));

            if (CustomHandler != null)
            {
                CustomHandler.Invoke(session, pkt, id);
            }
            else
            {
                Action<PacketSession, IMessage> action = null;
                if (_handler.TryGetValue(id, out action))
                    action.Invoke(session, pkt);
            }
        }

		catch (Exception e)
		{
			Console.WriteLine($"MakePacketSpan Error: {e}");
        }

        finally
        {
            ArrayPool<byte>.Shared.Return(rentBuffer);
        }
    }


    public Action<PacketSession, IMessage> GetPacketHandler(ushort id)
	{
		Action<PacketSession, IMessage> action = null;
		if (_handler.TryGetValue(id, out action))
			return action;
		return null;
	}
}