@echo off
REM 서버 로그 follow (Ctrl+C 로 빠져나오기 — 컨테이너는 계속 실행됨)
pushd "%~dp0..\.."
docker compose -f CICD\docker-compose.yml logs -f --tail=100 server accountserver
popd
