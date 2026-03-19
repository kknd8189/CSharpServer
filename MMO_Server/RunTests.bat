@echo off
echo ============================================
echo   Select Verbosity Level
echo ============================================
echo   1. quiet    (result only)
echo   2. minimal  (default, failures only)
echo   3. normal   (each test name)
echo   4. detailed (full log)
echo ============================================
set /p choice=Select (1-4):

if "%choice%"=="1" set VERB=quiet
if "%choice%"=="2" set VERB=minimal
if "%choice%"=="3" set VERB=normal
if "%choice%"=="4" set VERB=detailed
if not defined VERB set VERB=minimal

echo.
echo ============================================
echo   Running Unit Tests... [%VERB%]
echo ============================================

dotnet test Server.Tests/Server.Tests.csproj -v %VERB% --logger "trx;LogFileName=TestResults.trx" --logger "liquid.md;LogFileName=TestResults.md" --logger "html;LogFileName=TestResultsDefault.html" --results-directory TestResults

echo ^<!DOCTYPE html^>^<html^>^<head^>^<meta charset="utf-8"^>^<title^>Test Results^</title^>^<style^>body{font-family:"Segoe UI",sans-serif;margin:40px;background:#f5f5f5}h1{color:#333}table{border-collapse:collapse;width:100%%;background:white;border-radius:8px;box-shadow:0 2px 4px rgba(0,0,0,.1)}th{background:#333;color:white;padding:12px;text-align:left}td{border-bottom:1px solid #eee;padding:10px 12px}tr:hover{background:#f9f9f9}strong{color:#2e7d32}details{margin:10px 0}summary{cursor:pointer;font-weight:bold}^</style^>^</head^>^<body^> > TestResults\TestResults.html
type TestResults\TestResults.md >> TestResults\TestResults.html
echo ^</body^>^</html^> >> TestResults\TestResults.html

echo.
echo ============================================
echo   Test results saved to TestResults/
echo   - TestResults.trx
echo   - TestResults.md
echo   - TestResults.html
echo ============================================
pause
