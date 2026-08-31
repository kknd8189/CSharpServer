# CSharpServer — C# MMO 게임 서버 포트폴리오

[![CI](https://github.com/kknd8189/CSharpServer/actions/workflows/ci.yml/badge.svg)](https://github.com/kknd8189/CSharpServer/actions/workflows/ci.yml)

> **.NET 10 기반 C# MMO 게임 서버** — Custom 패킷 / Zone 기반 시야 / Redis 토큰 인증 /
> DB 비동기 배치 / GracefulShutdown / 관측 파이프라인 / 서버 검증.
>
> 부하 한계를 **틱 히스토그램 p99** 로 판정하고, 병목을 특정해 **p99 를 61% 개선**했다.
> 단일 존 **700 CCU** 까지 30Hz 예산 유지 (p99 33.9ms) — **부하가 맵에 균등 분포할 때**.
> 실제 유저처럼 뭉치게 하면 같은 700 이 p99 245ms 로 무너진다 (실질 한계 250~300 CCU).

---

## 🎯 프로젝트 목표

> 글로벌 모바일 게임 **라이브 출시·운영 경험**을 바탕으로,
> 그때 *부족했던 측정 · 분석 · 운영 자동화*를 .NET 10 기반으로 **다시 설계**한 프로젝트입니다.

전 직장에서 만들었던 시스템 — 패킷 자동 생성기, 길드 분리 서버, 컨텐츠 백엔드, 운영툴 연동 — 을
이번엔 *처음부터* 다시 설계하면서 라이브 환경에서 못 했던 다음을 **명시적으로 검증**했습니다:

- **부하 측정·분석** — *가설(MariaDB) → 실측(ThreadPool/EF) → 진단* 으로 접속 병목을 뚫고,
  판정 기준을 **틱 p99** 로 바꾼 뒤 브로드캐스트 팬아웃을 특정해 **61% 개선**
- **관측** — 메트릭은 Prometheus/Grafana, 로그는 Elasticsearch/Kibana 로 분리.
  *"몇 %인가"* 와 *"누가 언제 무엇을"* 을 다른 파이프라인으로
- **서버 검증** — 스킬 쿨다운 / 이동 속도 / 텔레포트. 서버 랙이 만드는 **오탐을 전제로** 설계
- **운영 안정성** — GracefulShutdown / DLQ / 60초 주기 저장 / 포스트모템 집계
- **자동화** — Docker Compose 원샷 기동, GitHub Actions CI, 대시보드 코드 프로비저닝

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
                                                         │
   ┌─────────────────────────────────────────────────┐   │
   │  관측 — 게임 서버는 파일에만 쓰고, 수집기가 가져간다  │   │
   │                                                 │   │
   │  /metrics :9091 ◀── scrape ── Prometheus ─▶ Grafana :3000
   │  *.jsonl ──▶ Filebeat ──▶ Elasticsearch ─▶ Kibana :5601
   └─────────────────────────────────────────────────┘   │
                                                         └─▶ Docker Compose (9 컨테이너)
```

- **AccountServer**: 계정 생성·로그인 → Redis 토큰 발급
- **GameServer**: Redis 토큰 검증 → 게임 룸 입장 → Zone 기반 시야 동기화 + 서버 검증
- **MariaDB**: AccountDB / SharedDB / GameDB / LogDB 4종
- **Redis**: 인증 토큰 + 로그 배치 버퍼
- **관측**: Prometheus + Grafana(성능) / Elasticsearch + Kibana(로그) — 게임 서버와 분리
- **Docker Compose**: 9개 컨테이너 원샷 기동 (`scripts/up.bat`)

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
   → 게임 스택 4개(MariaDB / Redis / AccountServer / GameServer) + 관측 스택 5개(Prometheus / Grafana / Elasticsearch / Kibana / Filebeat), 총 9개 컨테이너가 백그라운드로 뜹니다.
2. **상태 확인** — `CICD\scripts\status.bat`로 9개 컨테이너 `healthy` 확인.
   대시보드: Grafana `localhost:3000` (성능) / Kibana `localhost:5601` (로그, 최초 1회 `node CICD/kibana/setup.js`)
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
| 6 | **부하 테스트 + 병목 개선** | 판정 기준을 틱 p99 로 바꾸고 팬아웃 병목 특정 → **p99 61% 개선**, 700 CCU | [docs/load-test.md](docs/load-test.md) |
| 7 | **Docker / CI** | 9개 컨테이너 Compose 원샷 기동, GitHub Actions로 build+test 자동화 | [docs/deployment.md](docs/deployment.md) |
| 8 | **관측 계층 분리** | 메트릭은 프로메테우스(틱 히스토그램 p99), 로그는 ES. 서버는 파일에만 기록해 수집기 장애와 분리 | [docs/monitoring.md](docs/monitoring.md) |
| 9 | **서버 검증 + 확률 검증** | 쿨다운/속도/텔레포트 검증에 오탐 방지(누적 점수·서버 기인 이동 구분), 드랍 확률 통계 검증 | [docs/monitoring.md](docs/monitoring.md) |

---

## 📊 성능 요약

한계는 하나가 아니라 **성격이 다른 두 지표**다.

| 지표 | 무엇이 막히나 | 결과 |
|---|---|---|
| **접속 수용** | 신규 접속이 거부됨 | 500 → **1,100+** |
| **게임 로직 처리** | 30Hz 틱 예산 초과 | **700 CCU** (p99 33.9ms, 예산 초과 0.9%) |
| **게임 로직 처리 (밀집)** | 브로드캐스트 팬아웃 | **250~300 CCU** (700 명 밀집 시 p99 245ms) |

(.NET 10 Release, Windows 11, Docker Compose, 단일 존, 클라이언트 동일 호스트, **부하 균등 분포**)

| CCU | 틱 p50 | 틱 p99 | 예산 초과 | 수신/s | 송신/s |
|---:|---:|---:|---:|---:|---:|
| 500 | 6.3 ms | 20.4 ms | 0.00% | 1,472 | 10,810 |
| **700** | 9.6 ms | **33.9 ms** | **0.90%** | 2,278 | 20,443 |
| 900 | 18.5 ms | 72.0 ms | 18.58% | 3,088 | 32,502 |
| 1,100 | 52.0 ms | 225.9 ms | 62.50% | 3,948 | 47,914 |

**같은 700 CCU, 분포만 바꿨을 때** ([상세](docs/load-test.md#10-밀집-시나리오--같은-700-명-분포만-바꾸면))

| 분포 | 팬아웃 p50 | 송신/s | 틱 p50 | 틱 p99 |
|---|---:|---:|---:|---:|
| 균등 (기본) | 6.2 | 19,400 | 12 ms | **34 ms** |
| 밀집 (반경 30) | 14.3 | 37,300 | 24 ms | 96 ms |
| 밀집 (반경 20) | **21.7** | **50,000** | **45 ms** | **245 ms** |

부하 2.6배에 지연 7.2배 — 초선형이다. 그리고 밀집 구간에서는 **중앙값조차** 예산을 넘는다.

### 스토리 ① 접속 병목 — 틀린 가설을 실측으로 기각

- 초기: 500 CCU에서 신규 접속 차단. 가설은 `max_connections=151`
- 실측: DB 연결 피크 146/1000 — **DB는 병목이 아니었음**
- 진짜 원인: **.NET ThreadPool starvation + 동기 EF Core 호출**
- 조치: `SetMinThreads(200,200)` + `await FirstOrDefaultAsync()` + `max_connections=1000`
- 결과: **1,100 CCU 까지 로그인 실패 0건**

### 스토리 ② 판정 기준을 바꾸자 답이 달라졌다

같은 서버인데 기준마다 답이 달랐다 — 최댓값 400 / 처리량 700 / **p99 500**.
30Hz 루프는 예산을 넘기면 sleep 을 생략해 따라잡으므로, **틱의 19%가 예산을 넘겨도
실측 Hz 는 멀쩡해 보인다.** 히스토그램이 없으면 볼 수 없다.

그 과정에서 **부하 클라이언트 자체의 결함**도 드러났다 — 이동의 절반이 맵에 없는
축으로 나가 서버가 폐기하고 있었다. 고친 뒤 같은 CCU 에서 송신이 **3.9× 증가**.

### 스토리 ③ 병목은 파라미터가 아니라 좌표축이었다

송신/수신 비율 **14.6배**로 브로드캐스트 팬아웃을 특정하고 파고들었더니,
맵은 x/z 평면인데 **시야 컬링을 x/y 로** 하고 있었다 — y 는 항상 0 이라 무의미했고
z 는 검사조차 안 해 의도한 시야(11×11)의 **1.8배**에 뿌리고 있었다.

- 700 CCU 기준 **p99 86.4 → 33.9 ms (−61%)**, 900 기준 288 → 72 ms (−75%)
- **연쇄 붕괴 해소** — 이전엔 999 CCU 에서 게임 스레드가 30초 막혀 세션 715개가
  강제 종료. 지금은 1,100 까지 킥 0건 (성능은 떨어져도 연결은 유지)

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
