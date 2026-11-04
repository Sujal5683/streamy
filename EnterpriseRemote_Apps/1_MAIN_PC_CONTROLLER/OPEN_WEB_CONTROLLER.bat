@echo off
title Enterprise Remote Access ? Zero-Install Web Controller
setlocal

:: Change into the Bin directory where ControlPlane is installed
cd /d "%~dp0Bin"

echo.
echo ================================================================================
echo   ENTERPRISE REMOTE ACCESS ? ZERO-INSTALL WEB CONTROLLER
echo ================================================================================
echo.

:: Check if EnterpriseRemote.ControlPlane is already running
tasklist /FI "IMAGENAME eq EnterpriseRemote.ControlPlane.exe" 2>NUL | find /I /N "EnterpriseRemote.ControlPlane.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo [*] Control Plane gateway service is already active.
) else (
    echo [*] Starting Control Plane gateway service on http://127.0.0.1:5293...
    start "" EnterpriseRemote.ControlPlane.exe --urls=http://0.0.0.0:5293
)

:: Check if public tunnel URL exists for WAN access
set "TARGET_URL=http://localhost:5293"
if exist "%~dp0public_tunnel_url.txt" (
    set /p TUNNEL_URL=<"%~dp0public_tunnel_url.txt"
    if not "%TUNNEL_URL%"=="" (
        echo [+] Public Cloudflare Edge Tunnel detected: %TUNNEL_URL%
    )
)

:: Wait for port 5293 to be listening before launching browser (prevents ERR_CONNECTION_REFUSED)
echo [*] Verifying local gateway connection...
powershell -NoProfile -Command "$s = New-Object System.Net.Sockets.TcpClient; for($i=0; $i -lt 20; $i++){ try{ $s.Connect('127.0.0.1', 5293); $s.Close(); exit 0 }catch{ Start-Sleep -Milliseconds 250 } }; exit 1" >nul 2>&1

:: Launch the Web Controller in the default browser (Chrome, Edge, etc.)
echo [*] Launching Web Controller in browser (%TARGET_URL%)...
start "" "%TARGET_URL%"

timeout /T 1 /NOBREAK >nul 2>&1
exit /b 0
