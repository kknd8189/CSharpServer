@echo off
echo ============================================
echo   Select Benchmark
echo ============================================
echo   1. Vector3Int       (vector math)
echo   2. GameObject       (HP, direction)
echo   3. Inventory        (slot search)
echo   4. JobSerializer    (job queue)
echo   5. RecvBufferSpan   (network buffer)
echo   6. ALL
echo ============================================
set /p choice=Select (1-6):

if "%choice%"=="1" set FILTER=*VectorBenchmarks*
if "%choice%"=="2" set FILTER=*GameObjectBenchmarks*
if "%choice%"=="3" set FILTER=*InventoryBenchmarks*
if "%choice%"=="4" set FILTER=*JobSerializerBenchmarks*
if "%choice%"=="5" set FILTER=*RecvBufferBenchmarks*
if "%choice%"=="6" set FILTER=*
if not defined FILTER set FILTER=*

echo.
echo ============================================
echo   Running Benchmarks... [%FILTER%]
echo   (Release mode, this may take a few minutes)
echo ============================================

dotnet run --project Server.Benchmarks/Server.Benchmarks.csproj -c Release -- --filter "%FILTER%" --exporters html markdown

echo.
echo ============================================
echo   Results saved to:
echo   BenchmarkDotNet.Artifacts/results/
echo ============================================
pause
