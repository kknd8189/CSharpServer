using System;

namespace ServerCore
{
    // PacketGenerator로 생성되는 모든 패킷 클래스가 구현하는 인터페이스.
    // Span 기반 Read/Write로 zero-allocation 직렬화 수행.
    public interface IPacket
    {
        void Read(ReadOnlySpan<byte> span);
        void Write(Span<byte> span, out ushort size);
    }
}
