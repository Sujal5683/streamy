import os

def update_main_pc_bat():
    path = 'EnterpriseRemote_Apps/1_MAIN_PC_CONTROLLER/START_MAIN_PC.bat'
    content = '''@echo off
title Enterprise Remote Access — Main PC Launcher
setlocal

:: Change into the Bin directory (where all self-contained executables live)
cd /d "%~dp0Bin"

echo.
echo ================================================================================
echo   ENTERPRISE REMOTE ACCESS — MAIN OPERATOR CONTROLLER (STANDALONE V2)
echo ================================================================================
echo.

:: ── Terminate any rogue or older background processes holding port 5293 ────────
tasklist /FI "IMAGENAME eq EnterpriseRemote.ControlPlane.exe" 2>NUL | find /I /N "EnterpriseRemote.ControlPlane.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo [*] Refreshing background gateway service to ensure latest standalone version is active...
    taskkill /F /IM EnterpriseRemote.ControlPlane.exe >nul 2>&1
    timeout /T 1 /NOBREAK >nul 2>&1
)

:: ── Start the standalone Control Plane background service on port 5293 ─────────
echo [*] Starting Control Plane gateway background service on port 5293 (all interfaces)...
start "" EnterpriseRemote.ControlPlane.exe --urls=http://0.0.0.0:5293

:: ── Verify port 5293 is listening ───────────────────────────────────────────
powershell -NoProfile -Command "$s = New-Object System.Net.Sockets.TcpClient; for($i=0; $i -lt 25; $i++){ try{ $s.Connect('127.0.0.1', 5293); $s.Close(); exit 0 }catch{ Start-Sleep -Milliseconds 200 } }; exit 1" >nul 2>&1

:: ── Launch the Operator Controller GUI ─────────────────────────────────────
echo [*] Launching Enterprise Remote Controller GUI...
start "" EnterpriseRemote.Controller.exe

timeout /T 1 /NOBREAK >nul 2>&1
exit /b 0
'''
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content.replace('\n', '\r\n'))
    print('[+] Updated START_MAIN_PC.bat')

def update_main_pc_vbs():
    path = 'EnterpriseRemote_Apps/1_MAIN_PC_CONTROLLER/START_MAIN_PC.vbs'
    content = '''Set WshShell = CreateObject("WScript.Shell")
Set fso      = CreateObject("Scripting.FileSystemObject")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
binDir    = scriptDir & "\\Bin"

' Stop any old background instances to prevent port 5293 conflict
WshShell.Run "taskkill /F /IM EnterpriseRemote.ControlPlane.exe", 0, True
WScript.Sleep 500

' Start Control Plane silently in Bin
WshShell.CurrentDirectory = binDir
WshShell.Run """" & binDir & "\\EnterpriseRemote.ControlPlane.exe""", 0, False

WScript.Sleep 1200

' Launch Controller GUI
WshShell.Run """" & binDir & "\\EnterpriseRemote.Controller.exe""", 1, False
'''
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content.replace('\n', '\r\n'))
    print('[+] Updated START_MAIN_PC.vbs')

def update_remote_pc_enroll():
    path = 'EnterpriseRemote_Apps/2_REMOTE_PC_STREAMER/1_CLICK_ENROLL_REMOTE_PC.bat'
    content = '''@echo off
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
echo     1. The Main PC is running START_MAIN_PC (or OPEN_WEB_CONTROLLER)
echo     2. Both PCs are on the same Wi-Fi, LAN, or Mobile Hotspot
echo     3. You have an Invitation Code or 4-digit PIN (default: 1234)
echo.
echo  ================================================================
echo.

:: ── Step 1: Main PC IP or Cloudflare URL ──────────────────────────────────────
set DEFAULT_SERVER=
if exist "%~dp0server_url.txt" set /p DEFAULT_SERVER=<"%~dp0server_url.txt"
if "%DEFAULT_SERVER%"=="" set DEFAULT_SERVER=http://172.20.10.4:5293

echo  [Tip] On the same Wi-Fi, use your Main PC's Wi-Fi IP (e.g. 172.20.10.4:5293).
echo        For connecting over the Internet, enter your https://*.trycloudflare.com URL.
echo.
set /p MAIN_IP="Enter Main PC IP or Server URL [Press ENTER to use %DEFAULT_SERVER%]: "
if "%MAIN_IP%"=="" set MAIN_IP=%DEFAULT_SERVER%

set SERVER_URL=%MAIN_IP%

echo.
echo  Target Control Plane: %SERVER_URL%
echo.

:: ── Step 2: 4-Digit Quick PIN or Invitation Code ───────────────────────────
set DEFAULT_CODE=
if exist "%~dp0quick_pin.txt" set /p DEFAULT_CODE=<"%~dp0quick_pin.txt"
if "%DEFAULT_CODE%"=="" (
    if exist "%~dp0invitation_code.txt" set /p DEFAULT_CODE=<"%~dp0invitation_code.txt"
)
if "%DEFAULT_CODE%"=="" set DEFAULT_CODE=1234

set /p INVITATION_CODE="Enter 4-Digit Quick PIN [Press ENTER to use %DEFAULT_CODE%]: "
if "%INVITATION_CODE%"=="" set INVITATION_CODE=%DEFAULT_CODE%

if "%INVITATION_CODE%"=="" (
    echo.
    echo  [ERROR] A 4-Digit Quick PIN or Invitation Token is required for secure enrollment.
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
    echo  [ERROR] Enrollment failed (exit code: %ENROLL_RESULT%).
    echo.
    echo  Troubleshooting checklist:
    echo    - Is START_MAIN_PC running on the Main PC?
    echo    - Are both devices connected to the same Wi-Fi router / Mobile Hotspot?
    echo    - Can you reach %SERVER_URL% from your browser on this PC?
    echo    - Is Windows Firewall blocking port 5293 on the Main PC?
    echo.
    pause
    exit /b %ENROLL_RESULT%
)

:: Save verified server_url for future streaming sessions
echo %SERVER_URL%> "%~dp0server_url.txt"
echo %SERVER_URL%> "%~dp0Bin\server_url.txt"

echo.
echo  [SUCCESS] This device has been enrolled and paired!
echo.
echo  [*] Starting Enterprise Remote streaming services now...
echo.
timeout /T 2 /NOBREAK >nul

call START_REMOTE_STREAMING.bat
exit /b 0
'''
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content.replace('\n', '\r\n'))
    print('[+] Updated 1_CLICK_ENROLL_REMOTE_PC.bat')

def update_remote_pc_start():
    path = 'EnterpriseRemote_Apps/2_REMOTE_PC_STREAMER/START_REMOTE_STREAMING.bat'
    content = '''@echo off
title Enterprise Remote Access — Remote PC Streamer Launcher
setlocal
cd /d "%~dp0"

echo.
echo ================================================================================
echo   ENTERPRISE REMOTE ACCESS — REMOTE STREAMING AGENT
echo ================================================================================
echo.

set "HAS_IDENTITY=0"
if exist "%~dp0Bin\device_identity.json" set "HAS_IDENTITY=1"
if exist "%~dp0device_identity.json" set "HAS_IDENTITY=1"

if "%HAS_IDENTITY%"=="0" (
    echo [*] This device is not yet paired or enrolled with your Main PC.
    echo     Opening 1-Click Enrollment Wizard now...
    echo.
    call "%~dp01_CLICK_ENROLL_REMOTE_PC.bat"
    exit /b %ERRORLEVEL%
)

echo [*] Enrolled device identity verified.
echo [*] Starting Enterprise Remote background streamer services...
echo.

cd /d "%~dp0Bin"

:: Refresh background service to ensure latest binaries run
taskkill /F /IM EnterpriseRemote.Host.UserAgent.exe >nul 2>&1
taskkill /F /IM EnterpriseRemote.Host.Service.exe   >nul 2>&1
timeout /T 1 /NOBREAK >nul 2>&1

start "" EnterpriseRemote.Host.Service.exe --run
timeout /T 1 /NOBREAK >nul 2>&1
start "" EnterpriseRemote.Host.UserAgent.exe

echo [+] Remote PC screen streaming is ACTIVE!
echo     The streamer runs in the system tray (bottom-right notification area).
echo.
timeout /T 3 /NOBREAK >nul 2>&1
exit /b 0
'''
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content.replace('\n', '\r\n'))
    print('[+] Updated START_REMOTE_STREAMING.bat')

def update_root_launchers():
    path = 'Launch_Remote_Access_App.bat'
    content = '''@echo off
title Enterprise Remote Access — Main Launcher
cd /d "%~dp0"

echo ===============================================================================
echo   Enterprise Windows Remote Access — Starting Main Controller Package
echo ===============================================================================
echo.

call "%~dp0EnterpriseRemote_Apps\\1_MAIN_PC_CONTROLLER\\START_MAIN_PC.bat"
exit /b %ERRORLEVEL%
'''
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content.replace('\n', '\r\n'))
    print('[+] Updated root Launch_Remote_Access_App.bat')

if __name__ == '__main__':
    update_main_pc_bat()
    update_main_pc_vbs()
    update_remote_pc_enroll()
    update_remote_pc_start()
    update_root_launchers()

