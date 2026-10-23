@echo off
title Enterprise Remote Access — 1-Click Enrollment Wizard
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
echo     2. Both PCs are on the same Wi-Fi or LAN network
echo     3. You have generated an Invitation Code from the Main PC
echo        (Click "Connect Other PC (Enroll Host)" in the Controller UI)
echo.
echo  ================================================================
echo.

:: ── Step 1: Main PC IP ──────────────────────────────────────────────────────
set /p MAIN_IP="Enter your Main PC IP address (e.g. 192.168.1.10) [default: 127.0.0.1]: "
if "%MAIN_IP%"=="" set MAIN_IP=127.0.0.1

:: Clean and format entered URL/IP
set SERVER_URL=%MAIN_IP%
echo %SERVER_URL% | findstr /i "trycloudflare.com" >nul
if %ERRORLEVEL% equ 0 (
    echo %SERVER_URL% | findstr /i "http" >nul
    if %ERRORLEVEL% neq 0 (
        set SERVER_URL=https://%MAIN_IP%
    )
) else (
    echo %SERVER_URL% | findstr /i "http" >nul
    if %ERRORLEVEL% neq 0 (
        set SERVER_URL=http://%MAIN_IP%:5293
    )
)

echo.
echo  Target Control Plane: %SERVER_URL%
echo.

:: ── Step 2: Invitation Code ─────────────────────────────────────────────────
set /p INVITATION_CODE="Enter the Invitation Code from your Main PC Controller: "
if "%INVITATION_CODE%"=="" (
    echo.
    echo  [ERROR] An Invitation Code is required for secure enrollment.
    echo          Generate one from the Main PC Controller app and try again.
    echo.
    pause
    exit /b 1
)

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
    echo  [ERROR] Enrollment failed ^(exit code: %ENROLL_RESULT%^).
    echo.
    echo  Troubleshooting checklist:
    echo    - Is START_MAIN_PC running on the Main PC?
    echo    - Can you ping %MAIN_IP% from this PC?
    echo    - Has the Invitation Code expired? ^(codes expire in 2 hours^)
    echo    - Is Windows Firewall blocking port 5293?
    echo      If so: netsh advfirewall firewall add rule name="EnterpriseRemote"
    echo             protocol=TCP dir=in localport=5293 action=allow
    echo.
    pause
    exit /b %ENROLL_RESULT%
)

echo.
echo  [SUCCESS] This device has been enrolled and paired!
echo.
echo  [*] Starting Enterprise Remote streaming services now...
echo.
timeout /T 2 /NOBREAK >nul

call START_REMOTE_STREAMING.bat
exit /b 0
