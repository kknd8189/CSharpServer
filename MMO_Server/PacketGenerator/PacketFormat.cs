namespace PacketGenerator
{
    class PacketManagerFormat
    {
        // {0} 패킷 등록 구문들
        // {1} 배열의 크기 (제너레이터가 계산한 최대 패킷 ID + 1)
        public static string managerFormat = @"
using ServerCore;
using System;
using System.Buffers.Binary;

namespace Protocol
{{

/// <summary>
/// 이 클래스는 자동 생성 됩니다. 절대 직접 수정하지 마세요.
/// </summary>
public class PacketManager
{{
    #region Singleton
    static PacketManager _instance = new PacketManager();
    public static PacketManager Instance {{ get {{ return _instance; }} }}
    #endregion

    PacketManager()
    {{
        Register();
    }}

    // Action<T> 대신 Span을 받을 수 있는 커스텀 델리게이트 선언
    // Span은 제네릭 타입 인자로 사용할 수 없기 때문
    public delegate void PacketHandlerSpan(PacketSession session, ReadOnlySpan<byte> buffer, ushort id);

    //Dictionary를 버리고(Array)로 변경! 크기는 {1}
    PacketHandlerSpan[] _onRecvSpan = new PacketHandlerSpan[{1}];
    Action<PacketSession, IPacket>[] _handler = new Action<PacketSession, IPacket>[{1}];

    public Action<PacketSession, IPacket, ushort> CustomHandler {{ get; set; }}

    public void Register()
    {{{0}
    }}

    public void OnRecvPacketSpan(PacketSession session, ReadOnlySpan<byte> buffer)
    {{
        // Server.ServerMetrics.IncrementPacketsReceived();
        ushort count = 0;

        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(count));
        count += 2;

        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(count));
        count += 2;

        //  Dictionary의 TryGetValue 없이 인덱스로 한방에 꽂아버림 (O(1))
        if (id >= 0 && id < {1})
        {{
            PacketHandlerSpan action = _onRecvSpan[id];
            if (action != null)
                action.Invoke(session, buffer, id);
        }}
    }}

    //  ProtoBuf, ArrayPool, CodedInputStream, CopyTo 전부 삭제! 수신 버퍼를 Span으로 직접 읽는 zero-copy 파싱.
    //  (단, 패킷 객체 new T()는 할당됨 — zero-copy지 zero-alloc 아님)
    void MakePacketSpan<T>(PacketSession session, ReadOnlySpan<byte> buffer, ushort id) where T : IPacket, new()
    {{
        try
        {{
            T pkt = new T();
            pkt.Read(buffer);

            if (CustomHandler != null)
            {{
                CustomHandler.Invoke(session, pkt, id);
            }}
            else
            {{
                Action<PacketSession, IPacket> action = _handler[id];
                if (action != null)
                    action.Invoke(session, pkt);
            }}
        }}
        catch (Exception e)
        {{
            Console.WriteLine($""MakePacketSpan Error: {{e}}"");
        }}
    }}

    public Action<PacketSession, IPacket> GetPacketHandler(ushort id)
    {{
        if (id >= 0 && id < {1})
            return _handler[id];
        return null;
    }}
}}

}}";

        // {0} 패킷 이름 (예: C_Move)
        public static string managerRegisterFormat = @"		
        _onRecvSpan[(int)MsgId.{0}] = MakePacketSpan<{0}>;
        _handler[(int)MsgId.{0}] = PacketHandler.{0}Handler;";
    }

    class PacketFormat
    {
        // {0} 패킷 이름 (예: C_Move)
        // {1} 멤버 변수들 
        // {2} Read 로직 
        // {3} Write 로직 
        public static string packetClassFormat = @"
        public class {0} : IPacket
        {{
        {1}

            public void Read(ReadOnlySpan<byte> span)
            {{
                ushort count = 0;
                count += sizeof(ushort); // Size
                count += sizeof(ushort); // Packet ID
        {2}
            }}

            public void Write(Span<byte> span, out ushort size)
            {{
                ushort count = 0;
                count += sizeof(ushort); // Size 
                count += sizeof(ushort); // Packet ID 
        {3}
                size = count;
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(0), size);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), (ushort)MsgId.{0});
            }}
        }}
";

        // 변수 1줄짜리 포맷 
        // {0} 타입, {1} 이름
        public static string memberFormat = "    public {0} {1};";
    


    // ======================== [Enum 포맷] ========================
        
        // {0} Enum 이름 (예: MsgId, CreatureState)
        // {1} Enum 멤버들
        public static string enumFormat = @"
public enum {0}
{{
{1}
}}
";
        // {0} 멤버 이름, {1} 멤버 값
        public static string enumMemberFormat = "    {0} = {1},";

        // ======================== [Struct 포맷] ========================

        // {0} 구조체 이름 (예: ItemInfo, StatInfo)
        // {1} 멤버 변수들
        // {2} Read 로직 (ref count 사용)
        // {3} Write 로직 (ref count 사용)
        public static string structFormat = @"
public class {0}
{{
{1}
    public void Read(ReadOnlySpan<byte> span, ref ushort count)
    {{
{2}
    }}

    public void Write(Span<byte> span, ref ushort count)
    {{
{3}
    }}
}}
";
    }
}