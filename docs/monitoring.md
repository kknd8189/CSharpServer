# 모니터링 — Serilog / Filebeat / Elasticsearch / Kibana

> 게임서버가 남긴 구조화 로그를 Elasticsearch로 모아 Kibana에서 보는 파이프라인.
> 부하 테스트 결과를 눈으로 확인하는 용도가 1순위다. 부하 생성은 [load-test.md](load-test.md) 참고.

---

## 1. 왜 이 구조인가

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

## 2. 서버 쪽 — Serilog 싱크 3개

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

### 메트릭 이벤트 분리

5초 주기 메트릭은 일반 로그와 섞이면 대시보드를 만들기 어렵다. 태그로 구분한다.

```csharp
Log.ForContext("EventType", "Metrics").Information("[Metrics] ...", ...);
```

Kibana에서 `EventType: Metrics` 로 필터링해 차트를 그린다.

---

## 3. Filebeat 설정 (`CICD/filebeat.yml`)

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

### 메트릭 필드 타입 명시

```yaml
setup.template.append_fields:
  - { name: PacketsRecvPerSec, type: double }
  - { name: TickMaxUs,         type: long }
  ...
```

> **겪은 문제**: 동적 매핑에 맡기면 **첫 문서의 값**으로 타입이 굳는다.
> `PacketsRecvPerSec`는 `recv / 5.0`이라 소수인데, 서버가 유휴일 때 첫 값이 `0`이면
> `long`으로 잡히고 이후 `1234.6`이 들어와도 소수점이 잘린다. 부하 테스트 그래프가
> 왜곡되므로 타입을 명시한다.

### 타임스탬프

```yaml
processors:
  - timestamp: { field: "@t", layouts: ["2006-01-02T15:04:05.999999999Z07:00"] }
```

이걸 안 하면 `@timestamp`에 **수집 시각**이 찍힌다. Filebeat가 밀리거나 재시작 후
밀린 로그를 몰아 읽으면 시간축이 실제 발생 시각과 어긋나 그래프를 믿을 수 없게 된다.

---

## 4. 실행

```bash
docker compose -f CICD/docker-compose.yml up -d --build
```

| 서비스 | 포트 | 확인 |
|---|---|---|
| Elasticsearch | 9200 | `curl localhost:9200/_cat/indices/*mmo-server*?v` |
| Kibana | 5601 | 브라우저에서 열기 |
| Filebeat | — | `docker logs mmo-filebeat` |

Kibana 데이터 뷰(최초 1회):

```bash
curl -X POST "http://localhost:5601/api/data_views/data_view" \
  -H 'kbn-xsrf: true' -H 'Content-Type: application/json' \
  -d '{"data_view":{"title":"mmo-server","name":"MMO Server Logs","timeFieldName":"@timestamp"}}'
```

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

## 5. 대시보드에서 볼 것

| 패널 | 필드 | 의미 |
|---|---|---|
| CCU | `Players` | 동시 접속자 추이 |
| **틱 최대 시간** | `TickMaxUs` | 30Hz = 33,000us 예산. 초과하면 프레임이 밀린 것 |
| 틱 평균 | `TickAvgUs` | 여유율. 평균이 낮아도 Max가 튀면 스파이크 존재 |
| 패킷 처리량 | `PacketsRecvPerSec` / `PacketsSentPerSec` | 부하 대비 처리량 |
| Idle/Work 비율 | `IdleTicks` vs `WorkTicks` | 서버가 놀고 있는지 |
| 경고 이상 | `@l: Warning` | DLQ 덤프, DB 스레드 종료 실패 등 |

부하 테스트 시 **`TickMaxUs`가 33,000을 넘는 시점의 `Players` 값**이 사실상
이 서버의 수용 한계다. 이 그래프가 CCU 주장의 근거가 된다.

---

## 6. 남은 것

- **ILM** — 로컬은 `setup.ilm.enabled: false`로 껐다. 장기 운영하려면 hot 7d → delete 30d 정책 필요
- **보안** — `xpack.security.enabled=false`는 로컬 전용. 외부 노출 시 반드시 켤 것
- **게임 로그(LogDb)** — 로그인/보상 로그는 감사 목적이라 MariaDB에 그대로 둔다.
  ES로 복제하면 분석은 편해지지만 정합성 기준은 DB가 유지해야 한다
- **AccountServer** — 현재 파이프라인은 GameServer만 수집한다. ASP.NET Core 쪽도
  같은 방식(Serilog CLEF → 같은 볼륨)으로 붙일 수 있다
