@echo off
chcp 65001 >nul
setlocal

echo Building DupFinder (net48, Release)...
dotnet build -c Release -v q --nologo
if errorlevel 1 goto :fail

if not exist dist mkdir dist
copy /Y "bin\Release\net48\DupFinder.exe" "dist\DupFinder.exe" >nul
if errorlevel 1 goto :fail

echo.
echo Done: dist\DupFinder.exe
echo.
pause
exit /b 0

:fail
echo.
echo Build FAILED.
pause
exit /b 1
