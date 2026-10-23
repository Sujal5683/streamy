@echo off
title Enterprise Remote Access - One-Click Launcher
cd /d "%~dp0"

echo ===============================================================================
echo    Enterprise Windows Remote Access: Initializing Local Environment
echo ===============================================================================
echo.

set LISTEN_URL=http://0.0.0.0:5293
set SERVER_URL=http://127.0.0.1:5293
set DIST_DIR=%~dp0dist

:: 1. Start Control Plane in background if not already listening
curl -s %SERVER_URL%/health >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo [+] Control Plane is already active at %SERVER_URL% (Listening on all interfaces: %LISTEN_URL%)
) else (
    echo [*] Starting Enterprise Control Plane on %LISTEN_URL% (Listening on all network interfaces)...
    start "" "%DIST_DIR%\ControlPlane\EnterpriseRemote.ControlPlane.exe" --urls=%LISTEN_URL%
    ping 127.0.0.1 -n 2 >nul
)

:: 2. Check if this laptop is enrolled
if exist "%DIST_DIR%\HostService\device_identity.json" (
    echo [+] Local device identity verified.
) else if exist "%ProgramData%\EnterpriseRemote\device_identity.json" (
    echo [+] Local device identity verified in ProgramData.
) else (
    echo [*] Enrolling this laptop with the Control Plane...
    powershell -NoProfile -Command ^
        "$inv = Invoke-RestMethod -Uri '%SERVER_URL%/api/v1/invitations/create' -Method Post -Body '{\"AdminUserId\":\"USR-ADMIN-01\",\"GroupId\":\"GRP-CORP-DESKTOPS\",\"TtlMinutes\":15,\"AttendedRequired\":false}' -ContentType 'application/json';" ^
        "& '%DIST_DIR%\HostService\EnterpriseRemote.Host.Service.exe' --enroll $inv.invitationCode --server '%SERVER_URL%'"
)

echo.
echo ===============================================================================
echo    Launching Operator Controller GUI Viewer...
echo ===============================================================================
echo.

start "" "%DIST_DIR%\Controller\EnterpriseRemote.Controller.exe"

exit /b 0
