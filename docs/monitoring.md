# 관측 — 메트릭은 프로메테우스, 로그는 Elasticsearch

> 성능 지표(집계값)와 이벤트 로그(개별 사건)를 서로 다른 파이프라인으로 나눈 이유와 구성.
> 부하 생성은 [load-test.md](load-test.md) 참고.

```
                    ┌─ 메트릭 (집계값 / "몇 %인가") ────────────────────────┐
GameServer ──:9091/metrics──◀── scrape ── Prometheus ──▶ Grafana :3000
  │                                                      틱 p99 / CCU / 처리량
  │
  └─ 로그 (개별 사건 / "누가 언제 무엇을") ─────────────────────────────────┐
       *.jsonl ──▶ Filebeat ──▶ Elasticsearch ──▶ Kibana :5601
                                 Abuse / Session / Ops
```

**왜 나눴나.** 한동안 메트릭도 5초마다 로그 한 줄로 ES에 넣었다. 규모가 작아서 동작은
했지만 결정적인 한계가 있었다 — 서버가 avg/max 를 미리 계산해 내보내므로
**개별 틱의 분포가 서버 안에서 사라졌고, p99 를 구할 수 없었다.**
실제로 부하 테스트에서 "틱 최대 기준 400 CCU vs 지속 프레임레이트 기준 600 CCU" 로
판정이 갈렸는데, 어느 쪽이 맞는지 답할 방법이 없었다. 프로메테우스 히스토그램은
버킷별 카운터를 그대로 노출하므로 분위수를 쿼리 시점에 계산할 수 있다.

부수적인 차이도 있다. 예전엔 `recv / 5.0` 처럼 **수집 주기가 코드에 박혀 있어서**
주기를 바꾸면 대시보드 의미가 깨졌다. 프로메테우스는 카운터만 노출하고
`rate()` 를 쿼리 시점에 계산하므로 그런 결합이 없다.

---

## 1. 메트릭 — 프로메테우스

서버는 `MetricServer` 로 `:9091/metrics` 를 열어두기만 하고, 프로메테우스가 5초마다
긁어간다(pull). 서버가 직접 밀어내지 않으므로 **수집기가 죽어도 게임 로직은 무영향**이다.

| 메트릭 | 타입 | 용도 |
|---|---|---|
| `game_tick_duration_seconds` | Histogram | 틱 처리 시간 분포. 30Hz 예산 0.0333초 버킷 포함 |
| `game_ticks_total{kind}` | Counter | work / idle 틱 수 → `rate()` 로 실측 Hz |
| `game_players_connected` | Gauge | 동시 접속자 |
| `game_packets_total{direction}` | Counter | recv / send |
| `game_validation_rejected_total{kind}` | Counter | 검증 거부 (오탐 감시용) |
| `game_sessions_closed_total{reason}` | Counter | 종료 사유별 |
| `game_session_duration_seconds{reason}` | Histogram | 접속 유지 시간 |

**틱 히스토그램 버킷**은 30Hz 예산(33.3ms) 부근을 촘촘히 나눴다. 그래야 이 쿼리가 의미를 갖는다:

```promql
# 예산을 넘긴 틱의 비율 — 이 값이 유의미하게 커지는 CCU 가 실질적 수용 한계
1 - (
  sum(rate(game_tick_duration_seconds_bucket{le="0.0333"}[1m]))
  / sum(rate(game_tick_duration_seconds_count[1m]))
)

# 분위수 — 최댓값 하나에 좌우되지 않는다
histogram_quantile(0.99, sum(rate(game_tick_duration_seconds_bucket[1m])) by (le))
```

**idle 틱을 분리해 세는 이유**: 처리할 잡이 없어 즉시 끝난 틱을 히스토그램에 넣으면
분포가 0 쪽으로 쏠려 실제 부하가 가려진다. `game_ticks_total{kind="idle"}` 로 따로 센다.

Grafana 대시보드와 데이터소스는 `CICD/grafana/provisioning/` 에서 **코드로 프로비저닝**된다.
컨테이너를 새로 만들어도 수동 설정이 필요 없다.

---

## 2. 로그 — Elasticsearch

### 왜 파일을 경유하나

```
GameServer ──write──▶ /app/logs/*.jsonl ──tail──▶ Filebeat ──bulk──▶ Elasticsearch ──▶ Kibana
 (파일에만 쓴다)        (server-logs 볼륨)          (사이드카)                            :5601
```

**서버가 ES로 직접 쏘지 않는다.** Serilog에는 Elasticsearch 싱크가 있지만 쓰지 않았다.

| | 직접 전송 (Serilog ES 싱크) | 파일 경유 (현재 구조) |
|---|---|---|
| ES 장애 시 | 싱크가 버퍼링/재시도. 버퍼 초과 시 로그 유실, 최악의 경우 서버 스레드 지연 | 서버는 **무영향**. 파일에 계속 쓴다 |
| ES 복구 후 | 버퍼에 남은 것만 복구 | Filebeat가 **저장된 오프셋부터** 이어서 전송 |
| 서버 코드 | ES 주소/인증/재시도 정책을 서버가 알아야 함 | 서버는 ES의 존재 자체를 모름 |
| 배포 | 서버 재배포 필요 | Filebeat만 교체 |

게임서버에서 로깅 경로가 외부 서비스 가용성에 묶이면 안 된다는 게 핵심이다.
30Hz 틱을 도는 게임 스레드가 로그 전송 때문에 밀리는 상황을 만들지 않는다.

---

### Serilog 싱크 3개

`Server/Program.cs` `Main` 진입부.

| 싱크 | 경로 | 보존 | 용도 |
|---|---|---|---|
| Console | stdout | — | 개발 중 눈으로 확인, `docker logs` |
| File (텍스트) | `logs/server-.txt` | 14일 | 사람이 읽는 보존용 |
| File (**CLEF JSON**) | `logs/server-.jsonl` | 7일 | Filebeat 수집용 |

JSON 싱크는 `CompactJsonFormatter`를 쓴다. 텍스트 로그는 메시지가 한 줄 문자열로
평탄화되어 구조가 사라지지만, CLEF는 메시지 템플릿의 프로퍼티를 개별 필드로 남긴다.

```
텍스트: [Metrics] PacketsRecv/s=0.0 ... TickMax=278us Players=0
JSON  : {"@t":"...","TickMaxUs":278,"Players":0,"EventType":"Metrics", ...}
                                  ^^^ 숫자 필드 → Kibana에서 바로 집계/차트 가능
```

### 필드 규칙 (ECS)

커스텀 필드는 아무 이름이나 쓰면 안 된다. Filebeat가 설치하는 ECS 템플릿과
충돌하면 문서 전체가 400으로 드롭된다.

```csharp
.Enrich.WithProperty("service.name", "gameserver")   // service 는 ECS 에서 객체
Log.Logger = Log.Logger.ForContext("labels.world", Name);  // 커스텀 필드는 labels.* 아래
```

> **겪은 문제**: 처음에 `service`에 문자열 `"gameserver"`를 그대로 넣었더니
> `object mapping for [service] tried to parse field [service] as object,
> but found a concrete value` 로 **모든 이벤트가 드롭**됐다.
> Filebeat 쪽 `expand_keys: true`가 점 표기(`service.name`)를 객체로 펼쳐준다.

### 이벤트 종류 (EventType)

로그는 종류별로 태깅해 Kibana에서 분리해 본다. 메트릭은 여기 없다 — 프로메테우스로 갔다.

| EventType | 내용 | 남기는 곳 |
|---|---|---|
| `Abuse` | 검증 위반 개별 건 (쿨다운/속도/텔레포트, 비정상 패킷 크기) | `GameRoom_Validation`, `PacketSession` |
| `Session` | 세션 종료 사유 + 유지 시간 | `ClientSession.OnDisconnected` |
| `Net` | 소켓 I/O 오류, accept 실패 | `ServerCore` (CoreLogger) |
| `Ops` | 기동/종료, DLQ, Redis 끊김 | `Program.cs` |

```csharp
Log.ForContext("EventType", "Abuse")
   .ForContext("ViolationKind", kind.ToString())
   .ForContext("PlayerDbId", player.PlayerDbId)
   .Warning("Teleport attempt. From={From} To={To} Distance={Distance}", ...);
```

`ServerCore` 는 외부 의존성 0 을 유지해야 하므로 Serilog 를 직접 참조하지 않는다.
`CoreLogger.Sink` 델리게이트만 노출하고 `Program.cs` 가 Serilog 를 꽂으며,
이때 category 가 `EventType` 으로 승격된다.

---
### Filebeat 설정 (`CICD/filebeat.yml`)

```yaml
filebeat.inputs:
  - type: filestream
    paths: ["/logs/server-*.jsonl"]
    parsers:
      - ndjson: { target: "", expand_keys: true }   # 이미 JSON 이라 grok 불필요
```

`server-logs` 볼륨을 **읽기 전용**으로 공유하고, `filebeat-data` 볼륨에 읽은 오프셋을
저장한다. 오프셋 볼륨이 없으면 컨테이너 재시작마다 파일을 처음부터 다시 읽어 중복이 쌓인다.

### 인덱스 — data stream

ES 8.x는 data stream이 기본이다. 날짜별 인덱스를 직접 만들지 않고 `mmo-server`
데이터 스트림 하나에 쓰면 ES가 `.ds-mmo-server-<날짜>-000001` 로 롤오버한다.

```yaml
output.elasticsearch:
  index: "mmo-server"
setup.template.name: "mmo-server"
setup.template.pattern: "mmo-server*"    # "mmo-server-*" 로 두면 매칭 실패
```

> **겪은 문제**: 패턴을 `mmo-server-*`(하이픈)로 뒀더니 data stream 이름 `mmo-server`와
> 매칭되지 않아 `no matching index template found for data stream [mmo-server]` 400,
> 이벤트가 전부 드롭됐다.

### 타임스탬프

```yaml
processors:
  - timestamp: { field: "@t", layouts: ["2006-01-02T15:04:05.999999999Z07:00"] }
```

이걸 안 하면 `@timestamp`에 **수집 시각**이 찍힌다. Filebeat가 밀리거나 재시작 후
밀린 로그를 몰아 읽으면 시간축이 실제 발생 시각과 어긋나 그래프를 믿을 수 없게 된다.

---

## 3. 실행

```bash
docker compose -f CICD/docker-compose.yml up -d --build
```

| 서비스 | 포트 | 확인 |
|---|---|---|
| **Grafana** | 3000 | 대시보드 `MMO Server` 자동 등록 (익명 열람 허용) |
| **Prometheus** | 9090 | `curl localhost:9090/api/v1/targets` → gameserver `health:"up"` |
| GameServer `/metrics` | 9091 | `curl localhost:9091/metrics \| grep game_` |
| Kibana | 5601 | 브라우저에서 열기 |
| Elasticsearch | 9200 | `curl localhost:9200/_cat/indices/*mmo-server*?v` |
| Filebeat | — | `docker logs mmo-filebeat` |

Grafana는 데이터소스·대시보드가 프로비저닝 파일로 자동 등록되므로 **수동 설정이 없다**.
Kibana는 API 호출이 필요해 스크립트로 묶어 뒀다 (최초 1회, 재실행 안전):

```bash
node CICD/kibana/setup.js
```

> `saved_objects/_import` 를 쓰지 않는 이유: 마이그레이션 버전이 없는 Lens 객체를
> Kibana 가 구버전으로 간주해 변환하다 500 이 난다. create API 는 현재 버전으로
> 바로 저장하므로 그 문제가 없다.

### 컨테이너 로그 디렉토리 권한

서버는 비루트(`app`, uid 1654)로 돈다. `Dockerfile.server`에서 마운트 지점을
미리 만들어 소유권을 넘긴다.

```dockerfile
RUN mkdir -p /app/logs && chown app:app /app/logs
```

> **겪은 문제**: 이게 없으면 빈 named volume이 root 소유로 생성되어 서버가 로그 파일을
> 만들지 못한다. **Serilog 파일 싱크는 실패해도 예외를 던지지 않아** 콘솔 로그는 정상인데
> 파일만 조용히 비어 있다. 볼륨은 최초 생성 시 소유권이 굳으므로, 기존 볼륨이 있다면
> `docker volume rm mmo-stack_server-logs` 로 재생성해야 한다.

---

## 4. 대시보드에서 볼 것

**Grafana** (성능) — `MMO Server` 대시보드

| 패널 | 쿼리 | 의미 |
|---|---|---|
| **틱 p50/p95/p99** | `histogram_quantile(...)` | 최댓값 하나에 좌우되지 않는 실제 분포 |
| **예산 초과 비율** | `1 - rate(bucket{le="0.0333"}) / rate(count)` | **이 값이 유의미해지는 CCU 가 수용 한계** |
| 실측 틱 Hz | `rate(game_ticks_total)` | 목표 30Hz. 떨어지면 게임이 실제로 느려진 것 |
| 동시 접속자 | `game_players_connected` | CCU |
| 패킷 처리량 | `rate(game_packets_total)` by direction | 송신이 수신보다 가파르면 브로드캐스트 팬아웃 |
| 검증 거부 | `rate(game_validation_rejected_total)` by kind | **급증 = 핵 유행 또는 오탐** |
| 세션 종료 사유 | `rate(game_sessions_closed_total)` by reason | Kicked / SlowClient 비중 |

**Kibana** (사건) — `MMO Server — 로그 (어뷰징 / 세션)` 대시보드, 패널 6개

| 패널 | 내용 |
|---|---|
| 검증 위반 추이 | `ViolationKind` 별 누적 막대 — 언제 무엇이 터졌나 |
| 상위 어뷰저 TOP 10 | PlayerDbId / 위반 건수 / 최고 누적점수 / 마지막 IP |
| 위반 종류 비율 | 도넛 — 어떤 종류가 지배적인가 |
| 세션 종료 사유 추이 | `CloseReason` 별 누적 막대 |
| 종료 사유별 유지 시간 | 중앙값·p95 — 진입 직후 이탈인지 장시간 플레이인지 |
| 이벤트 종류별 로그량 | 파이프라인이 살아있는지 한눈에 |

임시 조회는 Discover 에서:

| 질문 | 쿼리 |
|---|---|
| 이 유저 왜 튕겼나 | `EventType: Session AND AccountDbId: 1234` |
| 조작 패킷이 들어왔나 | `EventType: Abuse AND PacketSize > 10240` |
| 서버에 오류가 있었나 | `@l: Warning` 이상 |

역할 구분이 핵심이다 — **"몇 %인가"는 Grafana, "누가 언제 무엇을"은 Kibana.**
검증 위반은 양쪽 다 간다: 비율은 메트릭, 개별 건은 로그.

## 5. 남은 것

- **ILM** — 로컬은 `setup.ilm.enabled: false`로 껐다. 장기 운영하려면 hot 7d → delete 30d 정책 필요
- **보안** — `xpack.security.enabled=false`는 로컬 전용. 외부 노출 시 반드시 켤 것
- **게임 로그(LogDb)** — 로그인/보상 로그는 감사 목적이라 MariaDB에 그대로 둔다.
  ES로 복제하면 분석은 편해지지만 정합성 기준은 DB가 유지해야 한다
- **AccountServer** — 현재 파이프라인은 GameServer만 수집한다. ASP.NET Core 쪽도
  같은 방식(Serilog CLEF → 같은 볼륨)으로 붙일 수 있다
- **알럿** — Alertmanager 미구성. 예산 초과 비율이나 검증 거부율이 임계를 넘으면
  알림이 가야 한다
- **Grafana 익명 접근** — `GF_AUTH_ANONYMOUS_ENABLED=true` 는 로컬 전용

---

## 부록 — 오탐이 실제로 어떻게 생기나

검증을 켠 뒤 실측에서 나온 오탐 사례들. 전부 임계값이 아니라 **설계 누락**이 원인이었다.

| 사례 | 증상 | 원인 | 대응 |
|---|---|---|---|
| DummyClient 축 불일치 | 정상 더미 292세션 킥 | Up/Down 이 PosY 를 움직였는데 맵은 단일 Y 평면 (MaxY=MinY) 이라 서버가 100% 거부 → 좌표 격차 누적 | 더미를 x/z 축으로 교정 |
| 보정 미반영 | 위 격차가 계속 벌어짐 | `S_MoveHandler` 가 빈 껍데기라 서버 권위 좌표를 무시 | 자기 오브젝트의 S_Move 를 반영 |
| 사망 리스폰 | 800 CCU 에서 텔레포트 위반 360건 | `OnDead` → `EnterGame(randomPos:true)` 로 서버가 맵 임의 위치로 옮김. 그 순간 in-flight 이동은 옛 좌표 기준 | `PositionEpochTick`/`PrePosition` 으로 유예 |
| 유예가 너무 헐거움 | 텔레포트 핵이 통과 | 유예를 **시간만으로** 판정 → 리스폰 직후 1초 동안 어디로든 점프 가능 | 옛 좌표 기준으로도 정상 거리일 때만 유예 |

교훈이 두 가지다.

**1. 거부율 모니터링이 임계값 튜닝보다 먼저다.** 위 네 건 중 임계값 문제는 하나도 없었다.
`game_validation_rejected_total` 이 튀는 걸 보고 파고들어야 발견된다.

**2. "거부"와 "어뷰징"은 다른 개념이다.** 서버가 스스로 상태를 바꾼 직후의 불일치는
거부하되 처벌하면 안 된다. 그래서 `game_validation_forgiven_total` 을 따로 센다 —
이 값이 비정상적으로 크면 유예 창이 넓거나, 위치를 바꾸는 경로가 epoch 을 안 찍고 있다는 신호다.
