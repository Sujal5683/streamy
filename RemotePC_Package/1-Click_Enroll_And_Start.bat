@echo off
title Enterprise Remote Access - Remote PC Setup
cd /d "%~dp0"

echo ===============================================================================
echo    Enterprise Remote Access: Setup Remote PC (Host Endpoint)
echo ===============================================================================
echo.

set /p SERVER_URL="Enter Control Plane Server URL (e.g. http://192.168.1.50:5293): "
if "%SERVER_URL%"=="" (
    echo [!] Server URL cannot be empty.
    pause
    exit /b 1
)

set /p INVITATION_CODE="Enter Single-Use Invitation Code (e.g. INV-XXXX-XXXX-XXXX): "
if "%INVITATION_CODE%"=="" (
    echo [!] Invitation Code cannot be empty.
    pause
    exit /b 1
)

echo.
echo [*] Enrolling this computer with Enterprise Remote Control Plane...
echo     Server: %SERVER_URL%
echo     Code:   %INVITATION_CODE%
echo.

EnterpriseRemote.Host.Service.exe --enroll %INVITATION_CODE% --server %SERVER_URL%
if %ERRORLEVEL% neq 0 (
    echo.
    echo [!] Enrollment failed. Please verify:
    echo     1. Is the Control Plane PC running and reachable at %SERVER_URL%?
    echo     2. Is port 5293 allowed through the firewall on the host PC?
    echo     3. Is the invitation code valid and not expired?
    echo.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [+] Device enrollment SUCCESSFUL!
echo [*] Starting Enterprise Remote Host Service in background...
start "Enterprise Remote Host Service" /min EnterpriseRemote.Host.Service.exe --run --server %SERVER_URL%

echo [*] Starting Enterprise Remote Desktop User Agent...
start "Enterprise Remote Desktop Agent" EnterpriseRemote.Host.UserAgent.exe --server %SERVER_URL%

echo.
echo ===============================================================================
echo [+] This PC is now ONLINE and ready to be controlled!
echo     Go to your Operator Controller computer, click Refresh (🔄),
echo     and select this computer from the device list.
echo ===============================================================================
echo.
pause
