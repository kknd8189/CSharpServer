@echo off
REM 컨테이너 정지/제거 (DB 볼륨은 유지)
pushd "%~dp0..\.."
docker compose -f CICD\docker-compose.yml down
popd
echo.
echo [OK] stack stopped. DB data preserved.
pause
