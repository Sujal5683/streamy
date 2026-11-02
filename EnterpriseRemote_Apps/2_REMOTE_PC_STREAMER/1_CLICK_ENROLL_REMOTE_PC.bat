@echo off
title Enterprise Remote Access — 1-Click Enrollment Wizard
:: Ensure Administrator permissions for service registration and UIPI bypass
net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    if "%~1"=="" (
        powershell -Command "Start-Process cmd -ArgumentList '/c "%~f0" --elevated' -Verb runAs" 2>nul
        if %ERRORLEVEL% equ 0 exit /b 0
    )
)

setlocal
cd /d "%~dp0"

echo.
echo  ================================================================
echo    ENTERPRISE REMOTE ACCESS - DEVICE ENROLLMENT WIZARD
echo  ================================================================
echo.
echo   Perform this step ONCE on the Remote PC to pair it with
echo   your Main PC (operator/controller).
echo.
echo   BEFORE YOU CONTINUE — ensure the following:
echo     1. The Main PC is running START_MAIN_PC.vbs (or .bat)
echo     2. Both PCs are on the same Wi-Fi, LAN, or using Cloudflare Tunnel/Tailscale
echo     3. You have generated an Invitation Code or 4-Digit PIN from the Main PC
echo        (Click "Connect Other PC (Enroll Host)" in the Controller UI)
echo.
echo  ================================================================
echo.

:: ── Step 1: Main PC IP or Gateway URL ────────────────────────────────────────
set /p MAIN_IP="Enter your Main PC Gateway URL or IP (e.g. 10.10.79.117, 192.168.1.5, or trycloudflare URL) [default: 127.0.0.1]: "
if "%MAIN_IP%"=="" set MAIN_IP=127.0.0.1

:: Strip quotes and spaces
set SERVER_URL=%MAIN_IP:"=%
set SERVER_URL=%SERVER_URL: =%

echo.
echo  Target Control Plane: %SERVER_URL%
echo.

:: ── Step 2: Invitation Code, PIN, or Passkey ─────────────────────────────────
set /p INVITATION_CODE="Enter Invitation Code, 4-Digit Quick PIN (1234), or Permanent Master Key [default: 1234]: "
if "%INVITATION_CODE%"=="" set INVITATION_CODE=1234

:: Strip quotes and spaces
set INVITATION_CODE=%INVITATION_CODE:"=%
set INVITATION_CODE=%INVITATION_CODE: =%

echo.
echo  [*] Enrolling this device with %SERVER_URL%...
echo      Please wait — this may take a few seconds.
echo.

cd /d "%~dp0Bin"
EnterpriseRemote.Host.Service.exe --enroll "%INVITATION_CODE%" --server "%SERVER_URL%"
set ENROLL_RESULT=%ERRORLEVEL%
cd /d "%~dp0"

:: If enroll failed, check if device was already enrolled previously
if %ENROLL_RESULT% neq 0 (
    if exist "%~dp0Bin\device_identity.json" (
        echo.
        echo  [+] Existing pairing identity found (device_identity.json).
        echo      Proceeding to launch streaming services with existing identity...
        set ENROLL_RESULT=0
    )
)

if %ENROLL_RESULT% neq 0 (
    echo.
    echo  ================================================================
    echo  [ERROR] Enrollment failed (exit code: %ENROLL_RESULT%).
    echo  ================================================================
    echo.
    echo  Common causes and solutions:
    echo.
    echo  1. CAMPUS / PUBLIC WI-FI ISOLATION (Most common!):
    echo     Many university/office Wi-Fi networks block direct device-to-device communication.
    echo     -^> Solution A: Turn on your phone's Mobile Hotspot and connect BOTH PCs to it.
    echo     -^> Solution B: On the Main PC, click "Launch Terminal Tunnel" (Cloudflare)
    echo        and enter the https://...trycloudflare.com URL here!
    echo     -^> Solution C: Use Tailscale (connect both PCs with 100.x.y.z IP).
    echo.
    echo  2. CONTROLLER NOT RUNNING:
    echo     -^> Ensure START_MAIN_PC is running on your Main PC.
    echo.
    echo  3. WRONG IP OR CODE:
    echo     -^> Double-check the Gateway URL shown in your Main PC's top bar.
    echo     -^> Generate a fresh 4-digit PIN or invitation code in the Controller.
    echo.
    echo  4. WINDOWS FIREWALL:
    echo     -^> Run ALLOW_FIREWALL_PORT_5293.bat on your Main PC as Administrator.
    echo.
    pause
    exit /b %ENROLL_RESULT%
)

echo.
echo  ================================================================
echo  [SUCCESS] Device Enrolled & Paired Successfully!
echo  ================================================================
echo.
echo  [*] Registering and starting background service...
cd /d "%~dp0Bin"
EnterpriseRemote.Host.Service.exe --install >nul 2>&1
sc.exe start EnterpriseRemoteHostSvc >nul 2>&1

:: Also ensure host service runner is active
start "" "%~dp0Bin\EnterpriseRemote.Host.Service.exe" --run

echo  [*] Starting real-time Desktop Screen Streamer...
start "" "%~dp0Bin\EnterpriseRemote.Host.UserAgent.exe" --server "%SERVER_URL%"
cd /d "%~dp0"

echo.
echo  ================================================================
echo   [LIVE] STREAMING IS NOW ACTIVE!
echo  ================================================================
echo   Screen capture is running and broadcasting to your Main PC.
echo   Return to your Main PC Controller and click "Connect".
echo.
echo   Look for the green shield icon in the taskbar notification tray (^).
echo.
timeout /T 4 >nul
exit /b 0
