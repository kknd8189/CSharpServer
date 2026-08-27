// Kibana 데이터 뷰 + 대시보드 등록 (최초 1회).
// Grafana 는 프로비저닝 파일로 자동 등록되지만 Kibana 는 API 호출이 필요하다.
//
// saved_objects/_import 를 쓰지 않는 이유:
// 마이그레이션 버전이 없는 Lens 객체를 구버전으로 간주해 변환하다 500 이 난다.
// create API 는 현재 버전으로 바로 저장하므로 그 문제가 없다.
//
// 사용법:  node CICD/kibana/setup.js  [KIBANA_URL]
const fs = require("fs");
const path = require("path");

const KIBANA = process.argv[2] || process.env.KIBANA || "http://localhost:5601";
const NDJSON = path.join(__dirname, "dashboard.ndjson");

const post = (url, body) =>
  fetch(url, {
    method: "POST",
    headers: { "kbn-xsrf": "true", "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

(async () => {
  process.stdout.write("데이터 뷰... ");
  const dv = await post(`${KIBANA}/api/data_views/data_view`, {
    data_view: {
      id: "mmo-server-dv",
      title: "mmo-server",
      name: "MMO Server Logs",
      timeFieldName: "@timestamp",
    },
  });
  // 이미 있으면 400 이 온다 — 재실행 가능해야 하므로 실패로 보지 않는다.
  console.log(dv.status === 200 ? "생성" : `이미 있음 (${dv.status})`);

  const lines = fs.readFileSync(NDJSON, "utf8").trim().split("\n");
  let ok = 0;
  for (const line of lines) {
    if (!line.trim()) continue;
    const o = JSON.parse(line);
    const res = await post(
      `${KIBANA}/api/saved_objects/${o.type}/${o.id}?overwrite=true`,
      { attributes: o.attributes, references: o.references }
    );
    const body = res.ok ? "OK" : (await res.text()).slice(0, 200);
    console.log(`  ${o.type}/${o.id} → ${res.status} ${body}`);
    if (res.ok) ok++;
  }

  console.log(`\n${ok}/${lines.length} 등록 완료`);
  console.log(`${KIBANA}/app/dashboards#/view/mmo-server-dashboard`);
})();
