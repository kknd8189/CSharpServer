using System;
using System.Buffers;

namespace ServerCore
{
    public class RecvBufferSpan : IDisposable
    {
        byte[] _buffer;
        int _readPos;
        int _writePos;
        int _capacity;

        public RecvBufferSpan(int bufferSize)
        {
            //_buffer = new byte[bufferSize];
            _buffer = ArrayPool<byte>.Shared.Rent(bufferSize); 
            _capacity = bufferSize; // 빌린 크기 != 실제 크기 따라서 bufferSize를 _capacity에 따로 저장
        }
        
        // 사용한 버퍼 반납
        public void Dispose()
        {
            if(_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
        }

        public int DataSize { get { return _writePos - _readPos; } }
        public int FreeSize { get { return _capacity - _writePos; } }

        public ReadOnlySpan<byte> ReadSpan
        {
            get { return new ReadOnlySpan<byte>(_buffer, _readPos, DataSize); }
        }

        public Span<byte> WriteSpan
        {
            get { return new Span<byte>(_buffer, _writePos, FreeSize); }
        }

        // Socket API용 ReceiveAsync에 넘겨줄 Segment
        public ArraySegment<byte> WriteSegment
        {
            get { return new ArraySegment<byte>(_buffer, _writePos, FreeSize); }
        }

        public void Clean()
        {
            int dataSize = DataSize;
            if (dataSize == 0)
            {
                // 남은 데이터가 없으면 복사하지 않고 커서 위치만 리셋
                _readPos = _writePos = 0;
            }
            else
            {
                // 남은 찌끄레기가 있으면 시작 위치로 복사
                //Span.CopyTo: 내부적으로 memmove(Overlap-Safe) 로직을 사용
                //소스 영역과 목적지 영역이 겹쳐 있어도, 데이터를 깨먹지 않고 안전하고 빠르게 이동
                //JIT 컴파일러가 AVX / SIMD 명령어를 사용해서 대량의 데이터도 순식간에 밀어버립니다.

                /*
                ; --- [1 & 2] Span 생성 단계(비용: 0에 수렴)-- -
                ; 배열(buffer)은 객체이므로 실제 데이터는 헤더(16byte) 뒤에 있음.
                ; rcx(배열주소) + 0x10(16) = 실제 데이터 시작 주소(0번지)
                add rcx, 16; rcx = &buffer[0](Dest 주소 확보 완료)

                ; Source 주소 계산(lea: Load Effective Address)
                ; rax = rcx(&buffer[0]) + rdx(readPos)
                lea rax, [rcx + rdx]; rax = &buffer[readPos](Source 주소 확보 완료)


                ; --- [3] CopyTo 단계(비용: 실제 복사) ---

                ; 이제 복사 함수(memmove)를 호출하기 위해 파라미터 세팅
                ; 파라미터 순서: (Dest, Source, Length)

                ; r8 = dataSize(길이는 이미 r8에 들어있음, 건들 필요 없음)
                ; rdx = Source 주소(아까 계산한 rax 값을 rdx로 옮김)
                mov rdx, rax
                ; rcx = Dest 주소(이미 & buffer[0]을 가리키고 있음, 건들 필요 없음)

                ; ★ 여기가 핵심!데이터 복사 호출
                ; C#은 내부적으로 최적화된 Buffer.Memmove를 호출합니다.
                call    System.Buffer.Memmove(Byte ByRef, Byte ByRef, UInt64)

                ret; 함수 종료 */

                /*
                ; ---함수 호출 준비(Calling Convention) ---
                ; Array.Copy는 인자가 5개입니다.
                ; x64 규약상 4개는 레지스터(rcx, rdx, r8, r9), 5번째부터는 스택([rsp])을 씁니다.

                ; 1.Source Array(rcx)
                mov rcx, rsi; buffer 주소

                ; 2.Source Index(rdx)
                mov edx, ebx; readPos 값

                ; 3.Dest Array(r8)
                mov r8, rsi; buffer 주소(Source와 동일)

                ; 4.Dest Index(r9)
                xor r9d, r9d; 0(Dest Index는 항상 0이니까 XOR로 0 만듦)

                ; 5.Length(Stack) - 여기가 Span과 가장 큰 차이!
                ; 레지스터가 모자라서 메모리(스택)에 값을 씁니다. (메모리 쓰기 비용 발생)
                mov dword ptr[rsp + 20h], r14d; dataSize를 스택에 저장

                call System.Array.Copy(System.Array, Int32, System.Array, Int32, Int32)

                ; (함수 내부에서 수많은 검증 로직이 수행된 후 리턴)
                ret*/

                ReadOnlySpan<byte> validData = new ReadOnlySpan<byte>(_buffer, _readPos, dataSize);
                validData.CopyTo(new Span<byte>(_buffer, 0, dataSize));

                _readPos = 0;
                _writePos = dataSize;
            }
        }

        public bool OnRead(int numOfBytes)
        {
            if (numOfBytes > DataSize)
                return false;

            _readPos += numOfBytes;
            return true;
        }

        public bool OnWrite(int numOfBytes)
        {
            if (numOfBytes > FreeSize)
                return false;

            _writePos += numOfBytes;
            return true;
        }
    }
}
