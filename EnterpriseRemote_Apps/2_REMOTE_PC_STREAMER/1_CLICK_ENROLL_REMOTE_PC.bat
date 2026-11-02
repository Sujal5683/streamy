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

:: ── Step 2: Invitation Code or 4-Digit Quick PIN ─────────────────────────────
set /p INVITATION_CODE="Enter the Invitation Code or 4-Digit Quick PIN from your Main PC: "
if "%INVITATION_CODE%"=="" (
    echo.
    echo  [ERROR] An Invitation Code or PIN is required for secure enrollment.
    echo          Generate one from the Main PC Controller app and try again.
    echo.
    pause
    exit /b 1
)

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
    echo     -^> Run this on your Main PC as Administrator:
    echo        netsh advfirewall firewall add rule name="EnterpriseRemote" protocol=TCP dir=in localport=5293 action=allow
    echo.
    pause
    exit /b %ENROLL_RESULT%
)

echo.
echo  [SUCCESS] This device has been enrolled and paired!
echo.
echo  [*] Registering and starting background service (EnterpriseRemoteHostSvc)...
EnterpriseRemote.Host.Service.exe --install >nul 2>&1
sc.exe start EnterpriseRemoteHostSvc >nul 2>&1
echo.
echo  [*] Starting Enterprise Remote streaming services now...
echo.
timeout /T 2 /NOBREAK >nul

call START_REMOTE_STREAMING.bat
exit /b 0
