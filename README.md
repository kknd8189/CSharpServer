# CSharpServer — C# MMO 게임 서버 포트폴리오

[![CI](https://github.com/kknd8189/CSharpServer/actions/workflows/ci.yml/badge.svg)](https://github.com/kknd8189/CSharpServer/actions/workflows/ci.yml)

> **.NET 10 기반 C# MMO 게임 서버 — Custom 패킷 / Zone 기반 시야 / Redis 토큰 인증 /
> DB 비동기 배치 / GracefulShutdown / Docker 배포 / 부하 검증 1000 CCU.**

---

## 🎯 프로젝트 목표

> 글로벌 모바일 게임 **라이브 출시·운영 경험**을 바탕으로,
> 그때 *부족했던 측정 · 분석 · 운영 자동화*를 .NET 10 기반으로 **다시 설계**한 프로젝트입니다.

전 직장에서 만들었던 시스템 — 패킷 자동 생성기, 길드 분리 서버, 컨텐츠 백엔드, 운영툴 연동 — 을
이번엔 *처음부터* 다시 설계하면서 라이브 환경에서 못 했던 다음을 **명시적으로 검증**했습니다:

- **부하 측정·분석** — 1000 CCU 안정 운영, *가설(MariaDB) → 실측(ThreadPool/EF) → 진단*의 검증 프로세스
- **운영 안정성** — GracefulShutdown / DLQ / 로그 배치 / 포스트모템 집계
- **자동화** — Docker Compose 원샷 기동, GitHub Actions CI, 사내 배포 스크립트

> *"라이브에서 한 번 본 패턴을 다음엔 더 정제해서 만든다"* — 가 이 프로젝트의 핵심 가치입니다.

---

## 🏗 아키텍처

```
┌──────────────┐    HTTP 5000     ┌──────────────────┐
│ DummyClient  │ ───────────────▶ │  AccountServer   │ ──┐
│ (부하 클라)  │                  │  (ASP.NET Core)  │   │
└──────┬───────┘                  └──────────────────┘   │
       │                                                 ├─▶ MariaDB
       │                                                 │   (Account / Shared / GameDB / LogDB)
       │ TCP 7777                  ┌──────────────────┐  │
       └──────────────────────────▶│   GameServer     │ ─┤
                                   │   (.NET 10)      │  │
                                   └──────────────────┘  ├─▶ Redis 7
                                                         │   (인증 토큰 / 로그 큐)
                                                         │
                                                         └─▶ Docker Compose 스택
```

- **AccountServer**: 계정 생성·로그인 → Redis 토큰 발급
- **GameServer**: Redis 토큰 검증 → 게임 룸 입장 → Zone 기반 시야 동기화
- **MariaDB**: AccountDB / SharedDB / GameDB / LogDB 4종
- **Redis**: 인증 토큰 + 로그 배치 버퍼
- **Docker Compose**: 7개 컨테이너 원샷 기동 (`scripts/up.bat`)

---

## 🛠 기술 스택

| 영역 | 사용 기술 |
|---|---|
| 언어 / 런타임 | C# 13, **.NET 10** |
| 네트워크 | **Custom binary packet** (Span 기반 무복사 파싱) |
| 인증 | ASP.NET Core 10 + **Redis 토큰** |
| DB | MariaDB 10.6, **Pomelo EF Core + Dapper** 병행 |
| 캐시 / 로그 | Redis 7 (StackExchange.Redis) |
| 직렬화 | 자체 **PacketGenerator** (Google.Protobuf 제거) |
| 테스트 | xUnit, **BenchmarkDotNet** |
| 배포 | **Docker Compose**, GitHub Actions CI |
| 로깅 | Serilog (파일 + 콘솔 sink) |

---

## 🚀 5분 안에 실행

```bash
git clone https://github.com/kknd8189/CSharpServer.git
cd CSharpServer
```

1. **도커 스택 기동** — `CICD\scripts\up.bat` 더블클릭
   → MariaDB, Redis, AccountServer, GameServer, Elasticsearch, Kibana, Filebeat 7개 컨테이너가 백그라운드로 뜹니다.
2. **상태 확인** — `CICD\scripts\status.bat`로 7개 컨테이너 `healthy` 확인.
3. **부하 클라이언트 실행**
   ```bash
   cd MMO_Server
   dotnet run --project DummyClient -- 50    # 50명 spawn으로 시작
   ```
4. **종료** — `CICD\scripts\down.bat` (DB 데이터는 보존)
   완전 초기화는 `reset.bat` (DB 볼륨까지 삭제, 프롬프트 확인 후 실행)

자세한 도커 / CI 흐름은 [docs/deployment.md](docs/deployment.md) 참고.

---

## 🔑 핵심 기능

| # | 기능 | 한 줄 요약 | 상세 |
|---|---|---|---|
| 1 | **멀티스레드 잡 시스템** | GameLogic / DB / Network 3개 스레드 분리 + `JobSerializer`로 락 없이 안전 | [docs/architecture.md](docs/architecture.md) |
| 2 | **Custom 패킷 + Span 파싱** | Protobuf 제거하고 자체 코드 생성기로 `ReadOnlySpan<byte>` 무복사 처리 | [docs/networking.md](docs/networking.md) |
| 3 | **Redis 토큰 인증** | AccountServer → Redis 토큰 → GameServer 검증. 비동기 처리 | [docs/auth.md](docs/auth.md) |
| 4 | **DB 3-step 트랜잭션** | GameRoom thread ↔ DB thread 안전 동기화 + Dapper 로그 배치 | [docs/persistence.md](docs/persistence.md) |
| 5 | **GracefulShutdown + DLQ** | 정상 종료 시 잔여 잡 flush, 실패 잡은 DLQ로 집계 | [docs/graceful-shutdown.md](docs/graceful-shutdown.md) |
| 6 | **부하 테스트 1000 CCU** | DummyClient 점진적 부하 + Serilog 메트릭 자동 수집 + 병목 분석 | [docs/load-test.md](docs/load-test.md) |
| 7 | **Docker / CI** | 7개 컨테이너 Compose 원샷 기동, GitHub Actions로 build+test 자동화 | [docs/deployment.md](docs/deployment.md) |
| 8 | **관측 계층 분리** | 메트릭은 프로메테우스(틱 히스토그램 p99), 로그는 ES. 서버는 파일에만 기록해 수집기 장애와 분리 | [docs/monitoring.md](docs/monitoring.md) |
| 9 | **서버 검증 + 확률 검증** | 쿨다운/속도/텔레포트 검증에 오탐 방지(누적 점수·서버 기인 이동 구분), 드랍 확률 통계 검증 | [docs/monitoring.md](docs/monitoring.md) |

---

## 📊 성능 요약

**1000 CCU 부하 환경** (.NET 10 Release, Windows 11, Docker Compose):

| Players | TickAvg | TickMax | Recv/s | Sent/s |
|---:|---:|---:|---:|---:|
| 500  | **7.08 ms**  | 34.86 ms | 1,746 | 6,130 |
| 1000 | **10.6 ms** | ~50 ms | 3,460 | 14,500 |

**병목 분석 → 개선** 스토리:
- 초기: 500 CCU에서 신규 접속 차단. 가설은 `max_connections=151`
- 실측: 1000 CCU 부하 시 DB 연결 피크 146/1000 — **DB는 병목이 아니었음**
- 진짜 원인: **.NET ThreadPool starvation + 동기 EF Core 호출**로 IOCP 워커 점유
- 조치: `ThreadPool.SetMinThreads(200, 200)` + `await FirstOrDefaultAsync()` + `max_connections=1000`
- 결과: **500 → 1000 CCU (2.0×), Sent/s 6,130 → 14,500 (2.4×)**

CCU 2.0× 증가에 TickAvg 1.5× 증가 — 선형 이하의 부하 증가율로 효율적 스케일링 확인.

상세 분석 + 그래프 → [docs/load-test.md](docs/load-test.md)

---

## 📁 프로젝트 구조

```
CSharpServer/
├── MMO_Server/                      # .NET 솔루션 루트
│   ├── Server/                      # GameServer (게임 로직, Zone, JobSerializer)
│   ├── ServerCore/                  # 네트워크 라이브러리 (Session, Listener, 패킷 버퍼)
│   ├── AccountServer/               # ASP.NET Core 인증 API (계정 생성/로그인/토큰 발급)
│   ├── SharedDB/                    # 서버 공통 DB 모델 (Token, ServerInfo)
│   ├── DummyClient/                 # 부하 테스트 클라이언트
│   ├── PacketGenerator/             # 패킷 코드 자동 생성기
│   ├── Server.Tests/                # xUnit 단위 테스트
│   └── Server.Benchmarks/           # BenchmarkDotNet 성능 측정
├── CICD/
│   ├── docker-compose.yml           # 4-컨테이너 스택 정의
│   ├── Dockerfile.server            # GameServer 이미지
│   ├── Dockerfile.accountserver     # AccountServer 이미지
│   ├── db-init/                     # DB 초기 스키마 (auto-load)
│   └── scripts/                     # up/down/logs/status/reset .bat
├── Common/
│   ├── Config.json                  # 서버 설정 (로컬 실행용)
│   └── Data/                        # 게임 데이터 (스탯/아이템/몬스터 JSON)
├── Client/                          # Unity 클라이언트 (확인용)
└── docs/                            # 상세 기술 문서
```

---

## 🧪 테스트 / 벤치마크

```bash
cd MMO_Server
.\RunTests.bat        # xUnit 전체 실행
.\RunBenchmarks.bat   # BenchmarkDotNet 패킷 직렬화 성능 측정
```

GitHub Actions CI에서 매 push마다 `dotnet build + test`가 ubuntu-latest 환경에서 자동 실행됩니다.

---

## 📝 사용 가이드 / 상세 문서

- [docs/architecture.md](docs/architecture.md) — 전체 아키텍처 / 스레딩 모델 / Zone 시스템
- [docs/networking.md](docs/networking.md) — Custom 패킷 / Span 파싱 / Session 구조
- [docs/auth.md](docs/auth.md) — AccountServer + Redis 토큰
- [docs/persistence.md](docs/persistence.md) — DbTransaction 3-step / 로그 배치
- [docs/graceful-shutdown.md](docs/graceful-shutdown.md) — 정상 종료 시퀀스
- [docs/load-test.md](docs/load-test.md) — 부하 테스트 결과
- [docs/deployment.md](docs/deployment.md) — Docker / CI / 운영 스크립트
- [docs/monitoring.md](docs/monitoring.md) — 메트릭(프로메테우스/그라파나) · 로그(ES/Kibana) · 확률 검증
- [docs/ai-workflow.md](docs/ai-workflow.md) — Claude Code 로 관측 계층을 만든 방식과 그 과정에서 찾은 버그들
