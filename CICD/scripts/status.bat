@echo off
REM 컨테이너 상태 + 헬스체크 결과 요약
pushd "%~dp0..\.."
docker compose -f CICD\docker-compose.yml ps
popd
echo.
pause
