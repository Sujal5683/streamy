@echo off
setlocal
cd /d "%~dp0"
echo =======================================================
echo          Streamy Release & Version Push
echo =======================================================
echo.
set "VER=%~1"
if "%VER%"=="" (
    set /p VER="Enter Streamy Version (e.g. 1.0.1): "
)
if "%VER%"=="" (
    echo Error: No version provided. Exiting.
    pause
    exit /b 1
)

powershell -ExecutionPolicy Bypass -File "%~dp0scripts\push_streamy_version.ps1" -Version "%VER%"
if "%~1"=="" pause
