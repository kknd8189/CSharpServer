# 배포 — Docker / GitHub Actions CI / 운영 스크립트

> 로컬에서 한 줄로 전체 스택을 띄우고, GitHub push 1번에 빌드/테스트가 돌아간다.
> 개발-검증-실행 사이의 마찰을 줄이는 게 목적.

---

## 1. 컨테이너 스택 — `CICD/docker-compose.yml`

총 **4개 컨테이너**:

| 서비스 | 이미지 | 포트 | 책임 |
|---|---|---|---|
| `mariadb` | `mariadb:10.6` (+ `--max-connections=1000`) | 3306 | 모든 DB (Account / Shared / Game / Log) |
| `redis` | `redis:7-alpine` | 6379 | 세션 토큰 / 로그 버퍼 |
| `accountserver` | `mmo-accountserver:local` (자체 빌드) | 5000 | HTTP 인증 API |
| `server` | `mmo-server:local` (자체 빌드) | 7777 | TCP 게임 서버 |

### healthcheck로 의존성 관리

```yaml
mariadb:
  healthcheck:
    test: ["CMD", "healthcheck.sh", "--connect", "--innodb_initialized"]
    interval: 5s
    retries: 20

accountserver:
  depends_on:
    mariadb: { condition: service_healthy }
    redis:   { condition: service_healthy }
```

서버는 DB가 *실제로 받을 준비된 시점*에야 기동 — `docker compose up`이 한 번에 깨끗하게 떨어진다.

### DB 자동 초기화 — `db-init/*.sql`

```yaml
mariadb:
  volumes:
    - ./db-init:/docker-entrypoint-initdb.d:ro
```

`docker-entrypoint-initdb.d`에 마운트된 `.sql` 파일들이 **빈 데이터 볼륨일 때만** 자동 실행됨:

```
CICD/db-init/
├── 01-init.sql              사용자 + DB 4개 생성
├── 02-logdb-tables.sql      LogDB 스키마 (login / reward)
├── 03-gamedb-schema.sql     GameDB 스키마 (Account / Player / Item)
├── 04-accountdb-schema.sql  AccountDB 스키마
└── 05-shareddb-schema.sql   SharedDB 스키마 (Token / Servers)
```

→ `reset.bat`로 볼륨 통째 날린 뒤 `up`해도 스키마는 동일하게 재구성됨.

---

## 2. 멀티스테이지 Dockerfile

빌드 환경(SDK)과 실행 환경(runtime) 분리로 최종 이미지 크기 최소화.

### `Dockerfile.server` — GameServer (콘솔 앱)

```dockerfile
# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# csproj만 먼저 복사 → restore 캐시 (소스 변경 시 NuGet 다시 안 받음)
COPY MMO_Server/Server/Server.csproj           MMO_Server/Server/
COPY MMO_Server/ServerCore/ServerCore.csproj   MMO_Server/ServerCore/
COPY MMO_Server/SharedDB/SharedDB.csproj       MMO_Server/SharedDB/
RUN dotnet restore MMO_Server/Server/Server.csproj

COPY MMO_Server/Server         MMO_Server/Server
COPY MMO_Server/ServerCore     MMO_Server/ServerCore
COPY MMO_Server/SharedDB       MMO_Server/SharedDB
RUN dotnet publish MMO_Server/Server/Server.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime    # ⭐ aspnet 아님
WORKDIR /app
USER app                                                 # 비루트
COPY --from=build --chown=app:app /app/publish .
EXPOSE 7777
ENTRYPOINT ["dotnet", "Server.dll"]
```

핵심 포인트:

1. **layer 분리**: `.csproj`만 복사 → restore → 소스 복사. 소스 변경 시 `restore` 캐시 hit
2. **runtime 이미지 (aspnet 아님)** — GameServer는 콘솔 앱, ASP.NET 미사용
3. **비루트 USER app** — 보안
4. **AppHost 비활성** (`UseAppHost=false`) — `dotnet Server.dll`로 실행, 컨테이너에서 불필요한 wrapper 제거

### `Dockerfile.accountserver` — AccountServer (ASP.NET Core)

거의 동일하지만 runtime은 `aspnet:10.0`, `ENV ASPNETCORE_URLS=http://+:5000`으로 0.0.0.0 바인딩 강제.

`appsettings.json`의 ConnectionString은 환경변수로 **컨테이너 시점에 오버라이드**:

```yaml
# docker-compose.yml
environment:
  ConnectionStrings__DefaultConnection: "Server=mariadb;...;Database=AccountDB;..."
  ConnectionStrings__SharedConnection:  "Server=mariadb;...;Database=SharedDB;..."
  ConnectionStrings__RedisConnection:   "redis:6379"
```

dev 환경의 localhost 설정을 그대로 두고도 컨테이너에선 서비스명 기반 호스트로 동작.

---

## 3. Config 외부 주입 — `Config.docker.json` 마운트 트릭

GameServer는 `ConfigManager`가 `/Common/config.json`에서 설정을 읽음.
컨테이너 안에선 빌드 시점이 아니라 **런타임에 호스트 파일을 마운트해서 주입**:

```yaml
server:
  volumes:
    - ./Config.docker.json:/Common/config.json:ro     # ⭐ 호스트 파일 → 컨테이너 경로
    - ../Common/Data:/Common/Data:ro                  # 게임 데이터 (스탯/아이템 등)
    - ../Common/MapData:/Common/MapData:ro
    - server-logs:/app/logs                           # 로그는 named volume
```

`Config.docker.json`의 connection string은 `Server=mariadb;Port=3306;...` 서비스명 기반.
로컬 실행용(`Common/Config.json`)은 `Server=localhost;...` — 둘 다 유지.

---

## 4. 실행 스크립트 — `CICD/scripts/`

복잡한 `docker compose -f CICD\docker-compose.yml ...` 커맨드를 더블클릭 한 번으로 추상화.

| 파일 | 동작 |
|---|---|
| `up.bat` | `docker compose up -d --build` — 빌드 + 백그라운드 기동 |
| `down.bat` | `docker compose down` — 컨테이너만 제거 (DB 볼륨 보존) |
| `logs.bat` | `logs -f --tail=100 server accountserver` — 로그 follow |
| `status.bat` | `docker compose ps` — 컨테이너 상태/헬스 요약 |
| `reset.bat` | `down -v` 후 재기동 — **DB 초기화** (`yes` 확인 프롬프트) |

모든 스크립트는 `pushd "%~dp0..\.."` 로 리포 루트로 이동한 뒤 `CICD\docker-compose.yml`을 지정.
→ 어디서 더블클릭해도 동작.

### 일반 워크플로우

```
up.bat       # 처음 환경 띄울 때
status.bat   # 4개 컨테이너 healthy 확인
logs.bat     # 로그 follow (Ctrl+C로 빠져나옴, 컨테이너는 계속 동작)
down.bat     # 작업 끝 (다음 up 시 DB 데이터 보존)
reset.bat    # 스키마 바꾼 뒤 처음부터 다시 (DB 통째 날림)
```

---

## 5. GitHub Actions CI — `.github/workflows/ci.yml`

매 push / PR마다 두 job 병렬 실행.

### `build-test` — .NET 솔루션 빌드 + xUnit 테스트

```yaml
build-test:
  runs-on: ubuntu-latest
  defaults: { run: { working-directory: MMO_Server } }
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-dotnet@v4
      with: { dotnet-version: '10.0.x' }
    - run: dotnet restore Server.sln
    - run: dotnet build Server.sln --configuration Release --no-restore
    - run: dotnet test Server.sln --configuration Release --no-build \
             --logger "trx;LogFileName=test-results.trx" \
             --results-directory ./TestResults
    - uses: actions/upload-artifact@v4
      if: always()
      with:
        name: test-results
        path: MMO_Server/TestResults/*.trx
```

- `.trx` 파일 artifact 업로드 → 실패 시 다운로드해서 어떤 테스트가 깨졌는지 바로 확인 가능
- `if: always()` — 빌드/테스트 실패해도 artifact는 업로드

### `docker-build` — 두 이미지 빌드 검증

```yaml
docker-build:
  runs-on: ubuntu-latest
  steps:
    - uses: actions/checkout@v4
    - uses: docker/setup-buildx-action@v3
    - uses: docker/build-push-action@v6      # server 이미지
      with:
        context: .
        file: CICD/Dockerfile.server
        push: false
        tags: mmo-server:ci
        cache-from: type=gha,scope=server
        cache-to: type=gha,scope=server,mode=max
    - uses: docker/build-push-action@v6      # accountserver 이미지
      with: ...
```

- `push: false` — 레지스트리 푸시 안 함, 빌드만 검증
- **GHA 캐시** (`cache-from/to: type=gha`) — 이전 push의 NuGet/dotnet 빌드 layer 재사용
- Buildx 캐시 효과로 두 번째 빌드부터는 1~2분으로 단축

README에 [![CI](https://github.com/.../actions/workflows/ci.yml/badge.svg)] 배지 — 첫 페이지에서 상태 가시화.

---

## 6. 운영 시 추가 고려사항 (현재 미적용)

| 항목 | 현재 | 운영 단계에서 필요 |
|---|---|---|
| HTTPS | dev 인증서 없음, HTTP 5000 | Nginx/Caddy/ALB로 TLS termination |
| 이미지 레지스트리 | 로컬만 (`mmo-server:local`) | GHCR / ECR / private registry |
| Secret 관리 | `docker-compose.yml`에 평문 | Docker Secret / AWS SSM / Vault |
| 모니터링 | Serilog 파일 로그만 | Prometheus + Grafana, ES + Kibana |
| 멀티 호스트 | 단일 머신 Compose | k8s / ECS / Nomad |
| Healthcheck | mariadb/redis만 | accountserver/server에도 추가 (Kestrel `/health`) |

이 부분은 로드맵에 두고 [load-test.md](load-test.md#7-후속-과제)와 함께 점진적으로 진행.

---

## 7. 관련 문서

- [architecture.md](architecture.md) — 컨테이너 토폴로지 / 컴포넌트 의존성
- [auth.md](auth.md) — AccountServer 컨테이너 환경변수로 ConnectionString 오버라이드
- [load-test.md](load-test.md) — 도커 환경 부하 측정 결과
