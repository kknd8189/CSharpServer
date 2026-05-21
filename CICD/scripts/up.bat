@echo off
REM 컨테이너 빌드 + 백그라운드 기동
pushd "%~dp0..\.."
docker compose -f CICD\docker-compose.yml up -d --build
set ERR=%ERRORLEVEL%
popd
if %ERR% neq 0 (
    echo.
    echo [ERROR] up failed with exit code %ERR%
    pause
    exit /b %ERR%
)
echo.
echo [OK] stack started. use status.bat / logs.bat to inspect.
pause
