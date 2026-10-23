@echo off
title Enterprise Remote Access - Start Remote Streaming
cd /d "%~dp0"

set HAS_IDENTITY=0
if exist "device_identity.json" set HAS_IDENTITY=1
if exist "%ProgramData%\EnterpriseRemote\device_identity.json" set HAS_IDENTITY=1

if "%HAS_IDENTITY%"=="0" (
    echo [!] This PC has not been enrolled yet.
    echo [*] Running 1-Click Enrollment setup...
    call 1-Click_Enroll_And_Start.bat
    exit /b 0
)

start "" EnterpriseRemote.Host.Service.exe --run
start "" EnterpriseRemote.Host.UserAgent.exe
exit /b 0
