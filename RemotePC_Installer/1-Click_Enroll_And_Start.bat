@echo off
title Enterprise Remote Access ? 1-Click Enrollment Wizard
:: Ensure Administrator permissions for service registration and UIPI bypass
net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    if "%~1"=="" (
        powershell -Command "Start-Process cmd -ArgumentList '/k \"\"%~f0\"\" --elevated' -Verb runAs" 2>nul
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
echo   BEFORE YOU CONTINUE ? ensure the following:
echo     1. The Main PC is running START_MAIN_PC.vbs (or .bat)
echo     2. Both PCs are on the same Wi-Fi, LAN, or using Cloudflare Tunnel/Tailscale
echo     3. You have the Main PC Gateway URL or IP
echo.
echo  ================================================================
echo.

:: ?? Step 1: Main PC IP or Gateway URL ????????????????????????????????????????
set /p MAIN_IP="Enter your Main PC Gateway URL or IP (e.g. 10.10.79.117, 192.168.1.5, or trycloudflare URL) [default: 127.0.0.1]: "
if "%MAIN_IP%"=="" set MAIN_IP=127.0.0.1

:: Strip quotes and spaces
set SERVER_URL=%MAIN_IP:"=%
set SERVER_URL=%SERVER_URL: =%

echo.
echo  Target Control Plane: %SERVER_URL%
echo.

:: ?? Step 2: Invitation Code, PIN, or Passkey ?????????????????????????????????
set /p INVITATION_CODE="Enter Invitation Code, 4-Digit Quick PIN (1234), or Permanent Master Key [default: 1234]: "
if "%INVITATION_CODE%"=="" set INVITATION_CODE=1234

:: Strip quotes and spaces
set INVITATION_CODE=%INVITATION_CODE:"=%
set INVITATION_CODE=%INVITATION_CODE: =%

echo.
echo  [*] Releasing any previous streamer locks and closing old instances...
del /f /q "%ProgramData%\EnterpriseRemote\kill_switch.flag" >nul 2>&1
del /f /q "%~dp0kill_switch.flag" >nul 2>&1
del /f /q "%~dp0Bin\kill_switch.flag" >nul 2>&1
taskkill /F /IM EnterpriseRemote.Host.UserAgent.exe >nul 2>&1
taskkill /F /IM EnterpriseRemote.Host.Service.exe >nul 2>&1

echo  [*] Enrolling this device with %SERVER_URL%...
echo      Please wait -- verifying cryptographic identity...
echo.

cd /d "%~dp0Bin"
EnterpriseRemote.Host.Service.exe --enroll "%INVITATION_CODE%" --server "%SERVER_URL%"
set ENROLL_RESULT=%ERRORLEVEL%
cd /d "%~dp0"

:: Check enrollment outcome
if %ENROLL_RESULT% equ 0 goto :ENROLL_SUCCESS

:: If fresh enroll failed, check if device was already enrolled previously
if exist "%~dp0Bin\device_identity.json" (
    echo.
    echo  [+] Existing pairing identity found: device_identity.json
    echo      Proceeding to launch streaming services with existing identity...
    goto :ENROLL_SUCCESS
)

:ENROLL_FAILED
echo.
echo  ================================================================
echo  [ERROR] Enrollment failed with exit code: %ENROLL_RESULT%
echo  ================================================================
echo.
echo  Common causes and solutions:
echo.
echo  1. CLOUDFLARE EDGE WARMUP OR TUNNEL NOT RUNNING:
echo     -^> Ensure START_PUBLIC_INTERNET_TUNNEL.bat is active on Main PC.
echo     -^> If you just launched the tunnel, wait a few seconds and re-run this script.
echo.
echo  2. CAMPUS OR PUBLIC WI-FI ISOLATION:
echo     Many university/office Wi-Fi networks block direct LAN connections.
echo     -^> Solution A: Turn on your phone Mobile Hotspot and connect BOTH PCs to it.
echo     -^> Solution B: Use the Cloudflare Tunnel URL from Main PC.
echo     -^> Solution C: Use Tailscale (connect both PCs with 100.x.y.z IP).
echo.
echo  3. WRONG IP OR CODE:
echo     -^> Double-check the Gateway URL shown in your Main PC top bar.
echo     -^> Use the 4-digit PIN (1234) or generate a fresh code in the Controller.
echo.
echo  4. WINDOWS FIREWALL:
echo     -^> Ensure port 5293 is allowed in Windows Firewall on Main PC.
echo.
pause
exit /b %ENROLL_RESULT%

:ENROLL_SUCCESS

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
start "" "%~dp0Bin\EnterpriseRemote.Host.UserAgent.exe" --server "%SERVER_URL%" --clear-kill-switch
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
echo  This window will automatically close in 5 seconds...
ping 127.0.0.1 -n 6 >nul
exit /b 0
