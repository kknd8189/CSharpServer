# 인증 — AccountServer + Redis 토큰

> 게임 서버는 클라이언트가 **직접 접속하지 못하게** 분리되어 있다.
> 모든 클라이언트는 먼저 AccountServer(HTTP)로 로그인해 토큰을 받고,
> 그 토큰으로 GameServer(TCP)에 인증한다. 토큰 저장소는 **Redis**.

---

## 1. 전체 흐름

```
   ┌──────────────┐                ┌──────────────────┐
   │   Client     │                │ AccountServer    │
   │              │                │ (ASP.NET Core 10)│
   └──────┬───────┘                └────────┬─────────┘
          │                                 │
          │ ① POST /api/account/create      │
          ├────────────────────────────────▶│
          │   { name, password }            ├─▶ AccountDB (INSERT)
          │◀────────────────────────────────┤
          │   { createOk: true }            │
          │                                 │
          │ ② POST /api/account/login       │
          ├────────────────────────────────▶│
          │   { name, password }            ├─▶ AccountDB (SELECT)
          │                                 │   match → token = Guid.NewGuid()
          │                                 ├─▶ Redis SETEX Session:{id} {token} 300s
          │◀────────────────────────────────┤
          │   { accountId, token,           │
          │     serverList }                │
          │                                 │
          │ ③ TCP connect + C_Login                            ┌──────────────────┐
          ├───────────────────────────────────────────────────▶│   GameServer     │
          │   { accountId, token }                             │                  │
          │                                                    │ Redis GETDEL     │
          │                                                    │ Session:{id}     │
          │◀───────────────────────────────────────────────────┤
          │   S_Login(LoginOk=1, players)                      │
          │                                                    │
          │                                                    │ ↳ lazy provision │
          │                                                    │   GameDB.Account │
          │                                                    └──────────────────┘
```

---

## 2. AccountServer — HTTP 엔드포인트

### `POST /api/account/create`

```csharp
// AccountServer/Controllers/AccountController.cs
public CreateAccountPacketRes CreateAccount([FromBody] CreateAccountPacketReq req)
{
    var account = _context.Accounts
        .AsNoTracking()                                    // 조회 전용 — change tracker 우회
        .Where(a => a.AccountName == req.AccountName)
        .FirstOrDefault();

    if (account == null)
    {
        _context.Accounts.Add(new AccountDb {
            AccountName = req.AccountName,
            Password    = req.Password,   // ⚠️ 평문 — 본격 운영 전에 bcrypt/Argon2 필요
        });
        var success = _context.SaveChangesEx();
        return new() { CreateOk = success };
    }
    return new() { CreateOk = false };   // 이미 존재
}
```

### `POST /api/account/login`

```csharp
public async Task<LoginAccountPacketRes> LoginAccount([FromBody] LoginAccountPacketReq req)
{
    var account = _context.Accounts
        .AsNoTracking()
        .Where(a => a.AccountName == req.AccountName && a.Password == req.Password)
        .FirstOrDefault();

    if (account == null) return new() { LoginOk = false };

    // 1회용 세션 토큰 발급
    string sessionToken = Guid.NewGuid().ToString("N");
    bool ok = await RedisAuth.SaveSessionTokenAsync(account.AccountDbId, sessionToken);
    if (!ok) return new() { LoginOk = false };

    return new() {
        LoginOk    = true,
        AccountId  = account.AccountDbId,
        Token      = sessionToken,
        ServerList = LoadServerList(),     // SharedDB.Servers에서 BusyScore 같이 반환
    };
}
```

---

## 3. RedisAuth — 토큰 저장/검증

```csharp
// SharedDB/Redis/RedisAuth.cs
public static class RedisAuth
{
    // AccountServer: 로그인 성공 시 호출
    public static async Task<bool> SaveSessionTokenAsync(int accountId, string token)
    {
        var db = RedisManager.Instance.GetDatabase();
        string key = $"Session:{accountId}";
        return await db.StringSetAsync(key, token, TimeSpan.FromSeconds(300));  // TTL 5분
    }

    // GameServer: C_Login 핸들러에서 호출
    public static async Task<bool> VerifyTokenAsync(int accountId, string clientToken)
    {
        var db = RedisManager.Instance.GetDatabase();
        string key = $"Session:{accountId}";
        string storedToken = await db.StringGetAsync(key);

        if (storedToken != null && storedToken == clientToken)
        {
            await db.KeyDeleteAsync(key);   // 1회용 — 즉시 삭제
            return true;
        }
        return false;
    }
}
```

### 설계 선택

| 선택 | 이유 |
|---|---|
| **1회용 토큰 (verify 후 즉시 삭제)** | 재사용 / replay attack 차단. 재로그인 필요 시 AccountServer 다시 거쳐야 함 |
| **TTL 300s** | 사용자가 로그인 후 게임 서버 접속까지 시간 (사용자가 서버 선택 화면에 머무는 시간 포함). 너무 길면 보안 약화 |
| **`GUID.ToString("N")`** | 32자 hex, URL-safe, 충돌 확률 사실상 0. 암호학적 강도는 부족하나 1회용 + 짧은 TTL이라 OK |
| **async API 사용** | sync 버전 (`db.StringGet`)은 부하 시 IOCP 워커 점유 → ThreadPool 고갈. multiplexer에 비동기로 위임 |

비동기 일관성의 중요성은 [load-test.md](load-test.md#3-가설과-실측--병목-분석)에서 측정으로 확인됨.

---

## 4. GameServer — C_Login 핸들러

```csharp
// Server/Session/ClientSession_PreGame.cs
public async Task<bool> HandleLoginAsync(C_Login loginPacket)
{
    if (ServerState != PlayerServerState.ServerStateLogin) return false;

    // ① Redis 토큰 검증 (async — IOCP 워커 블로킹 회피)
    if (!await RedisAuth.VerifyTokenAsync(loginPacket.AccountID, loginPacket.Token))
    {
        Send(new S_Login { LoginOk = 0 });
        Disconnect();
        return false;
    }

    // ② GameDB에서 Account 행 조회 (player 목록 포함)
    using (var db = new AppDbContext())
    {
        var findAccount = await db.Accounts
            .Include(a => a.Players)
            .Where(a => a.AccountDbId == loginPacket.AccountID)
            .FirstOrDefaultAsync();

        // ③ Lazy provisioning — GameDB에 Account 행이 없으면 직접 생성
        if (findAccount == null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT IGNORE INTO Account (AccountDbId) VALUES ({loginPacket.AccountID})");
            findAccount = await db.Accounts.Include(a => a.Players)... ;
        }

        AccountDbId = findAccount.AccountDbId;
        // 로비 진입 + 캐릭터 목록 전송
        ServerState = PlayerServerState.ServerStateLobby;
        Send(new S_Login { LoginOk = 1, Players = ... });
    }
}
```

### Lazy provisioning 이유

- **AccountServer (AccountDB)** = 인증 정보 (이름/비밀번호)
- **GameServer (GameDB)** = 게임 데이터 (캐릭터, 인벤토리, 스탯)
- 두 DB는 분리됨. AccountDb.AccountDbId만 공유 PK로 사용.

신규 가입자가 처음 게임 서버에 접속한 순간 GameDB에 자기 행이 없음 → **첫 로그인 시 INSERT IGNORE로 lazy 생성**. 동시 다중 접속 시 PK 중복은 `INSERT IGNORE`가 안전하게 흡수.

---

## 5. 부하 테스트 자동화 (DummyClient)

부하 클라이언트는 **계정 생성 → 로그인 → 게임 서버 접속**을 모두 자동 수행:

```csharp
// DummyClient/Program.cs - SpawnDummies
string accountName = $"DummyClient_{id:D4}";
await AccountServerClient.CreateAccountAsync(accountName, "1234");

var login = await AccountServerClient.LoginAsync(accountName, "1234");
if (login == null) { /* fail */ return; }

var connector = new Connector();
connector.Connect(_endPoint,
    () => SessionManager.Instance.Generate(login.AccountId, login.Token),
    count: 1);
```

1000명 부하 시 1000개 계정이 AccountDB에 만들어지고 1000개 토큰이 Redis에 발급됐다가 5분 안에 모두 GETDEL 됨.

---

## 6. 보안 한계 & 후속 과제

| 항목 | 현재 | 운영 시 필요 |
|---|---|---|
| 비밀번호 저장 | 평문 | bcrypt / Argon2id 해싱 |
| HTTPS | 컨테이너 HTTP 5000만 노출 (dev) | TLS termination (Nginx / Caddy / ALB) |
| 토큰 강도 | GUID (`N`) | 추가 페퍼 (HMAC) + 짧은 TTL 유지 |
| Rate limit | 없음 | AccountServer login에 IP rate limit |
| 동일 계정 동시 로그인 | 둘 다 허용 | 기존 세션 강제 종료 (kick) |
| 회원가입 검증 | 이름 중복만 | 이메일 인증, CAPTCHA |

---

## 7. 관련 문서

- [architecture.md](architecture.md) — 전체 시스템 / 컴포넌트 구성
- [persistence.md](persistence.md) — AccountDB / SharedDB / GameDB / LogDB 분리 구조
- [load-test.md](load-test.md) — 1000 CCU 부하 시 Redis/AccountServer 거동
