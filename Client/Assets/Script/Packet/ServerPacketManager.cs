
using ServerCore;
using System;
using System.Buffers.Binary;

namespace Protocol
{

/// <summary>
/// 이 클래스는 자동 생성 됩니다. 절대 직접 수정하지 마세요.
/// </summary>
public class PacketManager
{
    #region Singleton
    static PacketManager _instance = new PacketManager();
    public static PacketManager Instance { get { return _instance; } }
    #endregion

    PacketManager()
    {
        Register();
    }

    // Action<T> 대신 Span을 받을 수 있는 커스텀 델리게이트 선언
    // Span은 제네릭 타입 인자로 사용할 수 없기 때문
    public delegate void PacketHandlerSpan(PacketSession session, ReadOnlySpan<byte> buffer, ushort id);

    //Dictionary를 버리고(Array)로 변경! 크기는 23
    PacketHandlerSpan[] _onRecvSpan = new PacketHandlerSpan[23];
    Action<PacketSession, IPacket>[] _handler = new Action<PacketSession, IPacket>[23];

    public Action<PacketSession, IPacket, ushort> CustomHandler { get; set; }

    public void Register()
    {		
        _onRecvSpan[(int)MsgId.C_Move] = MakePacketSpan<C_Move>;
        _handler[(int)MsgId.C_Move] = PacketHandler.C_MoveHandler;
		
        _onRecvSpan[(int)MsgId.C_Skill] = MakePacketSpan<C_Skill>;
        _handler[(int)MsgId.C_Skill] = PacketHandler.C_SkillHandler;
		
        _onRecvSpan[(int)MsgId.C_Login] = MakePacketSpan<C_Login>;
        _handler[(int)MsgId.C_Login] = PacketHandler.C_LoginHandler;
		
        _onRecvSpan[(int)MsgId.C_EnterGame] = MakePacketSpan<C_EnterGame>;
        _handler[(int)MsgId.C_EnterGame] = PacketHandler.C_EnterGameHandler;
		
        _onRecvSpan[(int)MsgId.C_CreatePlayer] = MakePacketSpan<C_CreatePlayer>;
        _handler[(int)MsgId.C_CreatePlayer] = PacketHandler.C_CreatePlayerHandler;
		
        _onRecvSpan[(int)MsgId.C_EquipItem] = MakePacketSpan<C_EquipItem>;
        _handler[(int)MsgId.C_EquipItem] = PacketHandler.C_EquipItemHandler;
		
        _onRecvSpan[(int)MsgId.C_Pong] = MakePacketSpan<C_Pong>;
        _handler[(int)MsgId.C_Pong] = PacketHandler.C_PongHandler;

    }

    public void OnRecvPacketSpan(PacketSession session, ReadOnlySpan<byte> buffer)
    {
        // Server.ServerMetrics.IncrementPacketsReceived();
        ushort count = 0;

        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(count));
        count += 2;

        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(count));
        count += 2;

        //  Dictionary의 TryGetValue 없이 인덱스로 한방에 꽂아버림 (O(1))
        if (id >= 0 && id < 23)
        {
            PacketHandlerSpan action = _onRecvSpan[id];
            if (action != null)
                action.Invoke(session, buffer, id);
        }
    }

    //  ProtoBuf, ArrayPool, CodedInputStream, CopyTo 전부 삭제! 완벽한 Zero-Allocation
    void MakePacketSpan<T>(PacketSession session, ReadOnlySpan<byte> buffer, ushort id) where T : IPacket, new()
    {
        try
        {
            T pkt = new T();
            pkt.Read(buffer);

            if (CustomHandler != null)
            {
                CustomHandler.Invoke(session, pkt, id);
            }
            else
            {
                Action<PacketSession, IPacket> action = _handler[id];
                if (action != null)
                    action.Invoke(session, pkt);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"MakePacketSpan Error: {e}");
        }
    }

    public Action<PacketSession, IPacket> GetPacketHandler(ushort id)
    {
        if (id >= 0 && id < 23)
            return _handler[id];
        return null;
    }
}

}