@echo off
title Enroll Remote PC - Enterprise Remote Access
cd /d "%~dp0"

echo ===============================================================================
echo    Enterprise Remote Access: 1-Click Remote Host Endpoint Setup
echo ===============================================================================
echo.
echo [*] Enrolling this computer with Enterprise Remote Control Plane:
echo     Server URL:       http://172.18.11.122:5293
echo     Invitation Code:  INV-53A2-D016-D8DB
echo.

EnterpriseRemote.Host.Service.exe --enroll INV-53A2-D016-D8DB --server http://172.18.11.122:5293
if %ERRORLEVEL% neq 0 (
    echo [!] Enrollment failed. Please check network connectivity to http://172.18.11.122:5293.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [+] Device successfully enrolled!
echo [*] Starting Enterprise Remote Host Service in background...
start " EnterpriseRemote.Host.Service.exe --run --server http://172.18.11.122:5293
start " EnterpriseRemote.Host.UserAgent.exe --server http://172.18.11.122:5293

echo.
echo ===============================================================================
echo [+] Setup complete! This PC is now active and ready for remote access.
echo     You can now connect to this PC from the Controller app on your other computer.
echo ===============================================================================
echo.
pause
