using System;
using System.Threading;


namespace ServerCore
{
    public class SendBufferSpanHelper
    {
        public static ThreadLocal<SendBufferSpan> CurrentBuffer = new ThreadLocal<SendBufferSpan>(() => { return null; });

        public static int ChunkSize { get; set; } = 4096 * 15; // 60KB LOH에 저장되지 않도록 크기 조정 -> 부족할 경우 이후 늘리는 식으로

        public static Span<byte> Open(int reserveSize)
        {
            if (CurrentBuffer.Value == null)
                CurrentBuffer.Value = new SendBufferSpan(ChunkSize);

            if (CurrentBuffer.Value.FreeSize < reserveSize)
                CurrentBuffer.Value = new SendBufferSpan(ChunkSize);

            return CurrentBuffer.Value.Open(reserveSize);
        }

        public static ArraySegment<byte> Close(int usedSize)
        {
            return CurrentBuffer.Value.Close(usedSize);
        }
    }

    public class SendBufferSpan
    {
        byte[] _buffer;
        int _usedSize = 0;

        public int FreeSize { get { return _buffer.Length - _usedSize; } }

        public SendBufferSpan(int chunkSize)
        {
            _buffer = new byte[chunkSize];
        }

        public Span<byte> Open(int reserveSize)
        {
            if (reserveSize > FreeSize)
                return null;

            return new Span<byte>(_buffer, _usedSize, reserveSize);
        }

        //IO Queue에 넘겨줄 ArraySegment 반환
        public ArraySegment<byte> Close(int usedSize)
        {
            ArraySegment<byte> segment = new ArraySegment<byte>(_buffer, _usedSize, usedSize);
            _usedSize += usedSize;
            return segment;
        }
    }
}
