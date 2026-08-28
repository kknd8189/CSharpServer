# 네트워킹 — Custom 패킷 / Span 무복사 파싱 / 락프리 Session

> 이 서버는 **Protobuf를 의도적으로 걷어내고 자체 PacketGenerator + Span 기반 파이프라인**으로 교체했다.
> 그 동기와 구조, 그리고 Session 레이어가 어떻게 락 없이 동시성을 처리하는지 정리한다.

---

## 1. 패킷 포맷

```
+------------------+------------------+----------------------+
|  Size (UInt16)   |   MsgId (UInt16) |   Payload (variable) |
|   2 bytes        |    2 bytes       |   ...                |
+------------------+------------------+----------------------+
       ↑                  ↑
  Little Endian      MsgId enum 값
```

- Header 4바이트 (Size 2 + Id 2)
- 최대 패킷 크기 **10 KB** — 그 이상 들어오면 즉시 disconnect (해킹/오버플로우 방지)
- 최소 패킷 크기는 Header 자체 — 그보다 작은 size 필드 들어오면 disconnect (무한 루프 방지)

```csharp
// ServerCore/Session.cs : PacketSession.OnRecvSpan
if (dataSize < HeaderSize) { /* disconnect */ return -1; }
if (dataSize > 1024 * 10)  { /* disconnect */ return -1; }
```

---

## 2. 왜 ProtoBuf를 버렸는가

기존 구현은 `Google.Protobuf`를 사용했지만 두 가지 문제:

1. **Span 인터페이스 미지원** — `CodedInputStream`이 `byte[]`만 받음. IOCP에서 받은 `ReadOnlySpan<byte>`를 매번 `ArrayPool` 빌려서 복사해야 함 → **부하 테스트에서 ServerPacketManager의 MakePacket 함수가 hot path**로 측정됨
2. **자동 생성 코드의 추가 알로케이션** — `MergeFrom()` 호출 시 string/repeated field가 list/string 인스턴스 새로 생성

대안: **자체 PacketGenerator**로 `Read(ReadOnlySpan<byte>)` / `Write(Span<byte>)` 메서드 직접 생성. 복사 0회, 알로케이션은 패킷 클래스 자체 1개 뿐.

---

## 3. PacketGenerator — 코드 자동 생성

### 입력 (Google Sheets)

패킷 정의는 [Google Sheets](https://docs.google.com/spreadsheets/d/1XotKHBhAAndcumdiXWSZ4jYn9DmRj7pqzDF7bbB3dCo)에서 관리:

- `Enums` — enum 정의 (예: `PlayerServerState`)
- `Structs` — 재사용 가능한 구조체 (예: `StatInfo`, `PositionInfo`)
- `Packets` — 실제 메시지 (예: `C_Move`, `S_Login`)

### 출력 타겟 3곳

```csharp
// PacketGenerator/Program.cs
new OutputTarget { Dir = "../../Server/Packet",        PrefixFilter = "C_" },  // 서버: 수신 C_*
new OutputTarget { Dir = "../../../Client/Assets/...", PrefixFilter = "S_" },  // 유니티: 수신 S_*
new OutputTarget { Dir = "../../DummyClient/Packet",   PrefixFilter = "S_" },  // 부하 클라
```

`PrefixFilter`로 각 타겟이 *수신하는* 패킷만 핸들러를 생성하도록 분리. 송신용은 양쪽 모두 생성됨.

### 생성된 코드 예시

```csharp
public class C_Move : IPacket
{
    public PositionInfo PosInfo;
    public ushort Protocol => (ushort)MsgId.C_Move;

    public void Read(ReadOnlySpan<byte> span)
    {
        ushort count = 0;
        count += sizeof(ushort);  // size
        count += sizeof(ushort);  // id
        PosInfo = new PositionInfo();
        PosInfo.Read(span, ref count);
    }

    public void Write(Span<byte> span, out ushort size) { ... }
}
```

**복사 없음** — `Read(ReadOnlySpan<byte>)`가 수신 버퍼의 메모리를 직접 읽음.

---

## 4. PacketManager — Array 기반 O(1) 라우팅

```csharp
// 자동 생성: Server/Packet/ServerPacketManager.cs
PacketHandlerSpan[] _onRecvSpan = new PacketHandlerSpan[23];

public void OnRecvPacketSpan(PacketSession session, ReadOnlySpan<byte> buffer)
{
    ushort size = BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    ushort id   = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2));

    if (id < 23)
    {
        var action = _onRecvSpan[id];
        if (action != null) action.Invoke(session, buffer, id);
    }
}
```

- `Dictionary` 대신 **고정 크기 배열** — 해시 계산/충돌 회피 비용 0
- `MsgId` enum 최대값 + 1을 컴파일 타임에 결정 (PacketGenerator가 채워줌)
- 핸들러는 메서드 그룹 참조라 alloc 없음

---

## 5. RecvBufferSpan — ArrayPool + Span

```csharp
// ServerCore/RecvBufferSpan.cs
public class RecvBufferSpan : IDisposable
{
    public RecvBufferSpan(int bufferSize)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);   // ⭐ 풀에서 빌림
        _capacity = bufferSize;
    }

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);  // 이중 반환 방지
        if (buffer != null)
            ArrayPool<byte>.Shared.Return(buffer);
        // ⭐ clearArray:true 안 함 — 65KB memset 매번 도는 비용이 더 큼.
        //    어차피 _readPos/_writePos 커서 밖은 절대 안 읽음.
    }

    public Span<byte> WriteSpan => new(_buffer, _writePos, FreeSize);
    public ReadOnlySpan<byte> ReadSpan => new(_buffer, _readPos, DataSize);
}
```

- 세션 1개당 **64KB 버퍼** (10KB 패킷 × 6+개 큐잉 대응)
- `ArrayPool<byte>.Shared` 사용 → GC 압박 최소화
- 1000세션 = 약 64MB. Pool이 알아서 재사용

### Clean() — 잔여 데이터 정렬

```csharp
public void Clean()
{
    int dataSize = DataSize;
    if (dataSize == 0)
        _readPos = _writePos = 0;
    else
    {
        // Span.CopyTo는 내부적으로 memmove → AVX/SIMD까지 활용
        ReadOnlySpan<byte> validData = new(_buffer, _readPos, dataSize);
        validData.CopyTo(new Span<byte>(_buffer, 0, dataSize));
        _readPos = 0;
        _writePos = dataSize;
    }
}
```

`Array.Copy` 대신 `Span.CopyTo`를 쓰는 이유: 인자 수가 적어 레지스터 전달이 가능하고, JIT가 SIMD 명령어로 컴파일해 줌.

---

## 6. Session — 락 없는 Send/Recv 파이프라인

```
Session (abstract)
└── PacketSession (size 헤더 파싱)
    └── ClientSession (게임 로직)
```

### Send Queue — `ConcurrentQueue` + `Interlocked` flag

```csharp
// ServerCore/Session.cs
ConcurrentQueue<ArraySegment<byte>> _sendQueue = new();
int _sendQueueCount = 0;
int _sendRegistered = 0;     // 0 = idle, 1 = SendAsync 진행 중

const int MAX_SEND_QUEUE_SIZE = 1000;

public void Send(ArraySegment<byte> sendBuff)
{
    _sendQueue.Enqueue(sendBuff);

    // 큐 폭발 방어 — 느린 클라이언트 차단
    if (Interlocked.Increment(ref _sendQueueCount) > MAX_SEND_QUEUE_SIZE)
    {
        Disconnect();
        return;
    }

    // 0→1 전환에 성공한 단 한 스레드만 SendAsync 호출. 락 없음
    if (Interlocked.Exchange(ref _sendRegistered, 1) == 0)
        RegisterSend();
}
```

### RegisterSend 패턴 — gather → send → re-check

```csharp
while (true)
{
    int dequeued = 0;
    while (_sendQueue.TryDequeue(out var buff))
    {
        _pendingList.Add(buff);
        dequeued++;
    }
    Interlocked.Add(ref _sendQueueCount, -dequeued);

    if (_pendingList.Count == 0)
    {
        Interlocked.Exchange(ref _sendRegistered, 0);   // 1→0 (idle 복귀)

        // double-check — 그 사이 누가 또 넣었을 수도
        if (!_sendQueue.IsEmpty &&
            Interlocked.Exchange(ref _sendRegistered, 1) == 0)
            continue;
        return;
    }

    _sendArgs.BufferList = _pendingList;
    bool pending = _socket.SendAsync(_sendArgs);    // 비동기 송신
    if (!pending) { /* 동기 완료 → 즉시 다음 iteration */ continue; }
    return;
}
```

이 패턴 덕분에 **여러 IOCP 스레드가 동시에 같은 세션에 `Send()`를 호출해도 lock 없이 안전**. `_sendRegistered` flag가 mutex 역할 (단 1개 스레드만 SendAsync 진행).

### I/O 참조 카운팅 — 안전한 Dispose

비동기 I/O가 진행 중일 때 세션을 dispose하면 콜백에서 use-after-free.

```csharp
int _ioCount = 1;  // 세션 자체 ref + 진행 중 비동기 I/O 1개당 +1

void ReleaseIO()
{
    if (Interlocked.Decrement(ref _ioCount) == 0)
    {
        _sendArgs.Dispose();
        _recvArgs.Dispose();
        _recvBufferSpan?.Dispose();
    }
}

void RegisterRecv()
{
    Interlocked.Increment(ref _ioCount);          // 콜백 전에 먼저 +1
    bool pending = _socket.ReceiveAsync(_recvArgs);
    if (!pending) { Interlocked.Decrement(ref _ioCount); /* 동기 완료 */ }
    // 비동기: 콜백의 finally 블록에서 ReleaseIO() 호출
}
```

`CloseSocket()`이 다른 스레드에서 실행돼도 `_ioCount > 0`이면 실제 dispose는 마지막 콜백까지 미뤄짐.

---

## 7. 보안: 패킷 크기 검증 + Send 큐 상한

| 가드 | 위치 | 의도 |
|---|---|---|
| `dataSize < HeaderSize` | `PacketSession.OnRecvSpan` | 잘못된 size → 무한 루프 차단 |
| `dataSize > 10KB` | `PacketSession.OnRecvSpan` | 메모리 폭발 / DoS 방지 |
| `_sendQueueCount > 1000` | `Session.Send` | 느린 클라/연결 끊긴 클라 — 메모리 누수 방지 |

---

## 8. 측정 — Custom Packet의 효과

부하 테스트 메트릭 (단일 존, 200 → 1,100 CCU 램프업):

| CCU | 수신/s | 송신/s | 틱 p99 |
|---:|---:|---:|---:|
| 500 | 1,472 | 10,810 | 20.4 ms |
| **700** | 2,278 | 20,443 | **33.9 ms** ← 30Hz 예산 경계 |
| 900 | 3,088 | 32,502 | 72.0 ms |
| 1,100 | 3,948 | 47,914 | 225.9 ms |

수신은 CCU 에 선형이지만 **송신은 그보다 가파르다** — 비용이 접속자 수가 아니라
*시야 안 접속자 쌍*에 비례하는 브로드캐스트 특성이다.
이 비율(한때 14.6배)로 병목을 특정해 시야 컬링을 고쳤고, 700 CCU 기준 p99 를
86.4 → 33.9ms 로 **61% 개선**했다. 자세한 내용은 [load-test.md](load-test.md) 참고.

---

## 9. 관련 문서

- [architecture.md](architecture.md) — 스레딩 모델 / JobSerializer / Zone
- [auth.md](auth.md) — Redis 토큰 검증 흐름
- [load-test.md](load-test.md) — 부하 메트릭 + 병목 분석
- [monitoring.md](monitoring.md) — 메트릭/로그 파이프라인
