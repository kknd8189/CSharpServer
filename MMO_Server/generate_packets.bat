@echo off
setlocal

REM ============================================================
REM PacketGenerator 실행 스크립트
REM - 스프레드시트에서 Enum/Struct/Packet 정의를 다운받아
REM   Server/Packet/ 와 Client/Assets/Script/Packet/ 에
REM   Protocol.cs + PacketManager.cs 를 동시 생성한다.
REM ============================================================

cd /d "%~dp0"

echo [1/2] Building PacketGenerator...
dotnet build PacketGenerator\PacketGenerator.csproj -c Debug --nologo -v minimal
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed. Aborting.
    pause
    exit /b 1
)

echo.
echo [2/2] Running PacketGenerator...
cd /d "%~dp0PacketGenerator\bin"
PacketGenerator.exe
set EXITCODE=%errorlevel%

cd /d "%~dp0"
echo.
if %EXITCODE% neq 0 (
    echo [ERROR] Generator exited with code %EXITCODE%.
) else (
    echo [Done] Packet generation finished.
)
pause
endlocal
