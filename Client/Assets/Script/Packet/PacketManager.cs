
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

    //Dictionary를 버리고(Array)로 변경! 크기는 21
    PacketHandlerSpan[] _onRecvSpan = new PacketHandlerSpan[21];
    Action<PacketSession, IPacket>[] _handler = new Action<PacketSession, IPacket>[21];

    public Action<PacketSession, IPacket, ushort> CustomHandler { get; set; }

    public void Register()
    {		
        _onRecvSpan[(int)MsgId.S_EnterGame] = MakePacketSpan<S_EnterGame>;
        _handler[(int)MsgId.S_EnterGame] = PacketHandler.S_EnterGameHandler;
		
        _onRecvSpan[(int)MsgId.S_Spawn] = MakePacketSpan<S_Spawn>;
        _handler[(int)MsgId.S_Spawn] = PacketHandler.S_SpawnHandler;
		
        _onRecvSpan[(int)MsgId.S_Despawn] = MakePacketSpan<S_Despawn>;
        _handler[(int)MsgId.S_Despawn] = PacketHandler.S_DespawnHandler;
		
        _onRecvSpan[(int)MsgId.C_Move] = MakePacketSpan<C_Move>;
        _handler[(int)MsgId.C_Move] = PacketHandler.C_MoveHandler;
		
        _onRecvSpan[(int)MsgId.S_Move] = MakePacketSpan<S_Move>;
        _handler[(int)MsgId.S_Move] = PacketHandler.S_MoveHandler;
		
        _onRecvSpan[(int)MsgId.C_Skill] = MakePacketSpan<C_Skill>;
        _handler[(int)MsgId.C_Skill] = PacketHandler.C_SkillHandler;
		
        _onRecvSpan[(int)MsgId.S_Skill] = MakePacketSpan<S_Skill>;
        _handler[(int)MsgId.S_Skill] = PacketHandler.S_SkillHandler;
		
        _onRecvSpan[(int)MsgId.S_ChangeHp] = MakePacketSpan<S_ChangeHp>;
        _handler[(int)MsgId.S_ChangeHp] = PacketHandler.S_ChangeHpHandler;
		
        _onRecvSpan[(int)MsgId.S_Die] = MakePacketSpan<S_Die>;
        _handler[(int)MsgId.S_Die] = PacketHandler.S_DieHandler;
		
        _onRecvSpan[(int)MsgId.C_Login] = MakePacketSpan<C_Login>;
        _handler[(int)MsgId.C_Login] = PacketHandler.C_LoginHandler;
		
        _onRecvSpan[(int)MsgId.S_Login] = MakePacketSpan<S_Login>;
        _handler[(int)MsgId.S_Login] = PacketHandler.S_LoginHandler;
		
        _onRecvSpan[(int)MsgId.C_EnterGame] = MakePacketSpan<C_EnterGame>;
        _handler[(int)MsgId.C_EnterGame] = PacketHandler.C_EnterGameHandler;
		
        _onRecvSpan[(int)MsgId.C_CreatePlayer] = MakePacketSpan<C_CreatePlayer>;
        _handler[(int)MsgId.C_CreatePlayer] = PacketHandler.C_CreatePlayerHandler;
		
        _onRecvSpan[(int)MsgId.S_CreatePlayer] = MakePacketSpan<S_CreatePlayer>;
        _handler[(int)MsgId.S_CreatePlayer] = PacketHandler.S_CreatePlayerHandler;
		
        _onRecvSpan[(int)MsgId.S_ItemList] = MakePacketSpan<S_ItemList>;
        _handler[(int)MsgId.S_ItemList] = PacketHandler.S_ItemListHandler;
		
        _onRecvSpan[(int)MsgId.S_AddItem] = MakePacketSpan<S_AddItem>;
        _handler[(int)MsgId.S_AddItem] = PacketHandler.S_AddItemHandler;
		
        _onRecvSpan[(int)MsgId.C_EquipItem] = MakePacketSpan<C_EquipItem>;
        _handler[(int)MsgId.C_EquipItem] = PacketHandler.C_EquipItemHandler;
		
        _onRecvSpan[(int)MsgId.S_EquipItem] = MakePacketSpan<S_EquipItem>;
        _handler[(int)MsgId.S_EquipItem] = PacketHandler.S_EquipItemHandler;
		
        _onRecvSpan[(int)MsgId.S_ChangeStat] = MakePacketSpan<S_ChangeStat>;
        _handler[(int)MsgId.S_ChangeStat] = PacketHandler.S_ChangeStatHandler;

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
        if (id >= 0 && id < 21)
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
        if (id >= 0 && id < 21)
            return _handler[id];
        return null;
    }
}

}