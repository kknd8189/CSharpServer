@echo off
REM ⚠️ DB 볼륨까지 삭제 후 재기동 (init.sql 다시 실행됨)
echo.
echo ============================================================
echo  WARNING: this will DELETE all DB data (mariadb + redis).
echo  init.sql will run again on fresh start.
echo ============================================================
echo.
set /p CONFIRM=Type "yes" to proceed:
if /i not "%CONFIRM%"=="yes" (
    echo aborted.
    pause
    exit /b 1
)

pushd "%~dp0..\.."
docker compose -f CICD\docker-compose.yml down -v
docker compose -f CICD\docker-compose.yml up -d --build
set ERR=%ERRORLEVEL%
popd
if %ERR% neq 0 (
    echo.
    echo [ERROR] reset failed with exit code %ERR%
    pause
    exit /b %ERR%
)
echo.
echo [OK] stack reset and started.
pause
