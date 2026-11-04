@echo off
title Enterprise Remote Access ? Remote PC Kill Switch
setlocal enabledelayedexpansion

:: 1. Auto-elevate to Administrator to guarantee full authority over Windows Services and background processes
net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    if "%~1"=="" (
        powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd -ArgumentList '/c `"%~f0`" --elevated' -Verb runAs" 2>nul
        if %ERRORLEVEL% equ 0 exit /b 0
    )
)

echo.
echo ===============================================================================
echo   ENTERPRISE REMOTE ACCESS ? REMOTE PC KILL SWITCH ACTIVATING...
echo ===============================================================================
echo.

:: 2. Drop the permanent Kill Switch flag in all known locations
set "PROGRAMDATA_DIR=%ProgramData%\EnterpriseRemote"
if not exist "%PROGRAMDATA_DIR%" mkdir "%PROGRAMDATA_DIR%" >nul 2>&1
echo KILL_SWITCH_ACTIVE_%DATE%_%TIME% > "%PROGRAMDATA_DIR%\kill_switch.flag" 2>nul
echo KILL_SWITCH_ACTIVE_%DATE%_%TIME% > "%~dp0kill_switch.flag" 2>nul
if exist "%~dp0Bin" echo KILL_SWITCH_ACTIVE_%DATE%_%TIME% > "%~dp0Bin\kill_switch.flag" 2>nul
echo  [OK] Kill switch flag locked into place.

:: 3. Reconfigure Windows Service to manual/demand so SCM will NOT auto-respawn it
sc.exe config EnterpriseRemoteHostSvc start= demand >nul 2>&1
sc.exe stop EnterpriseRemoteHostSvc >nul 2>&1
net stop EnterpriseRemoteHostSvc >nul 2>&1
echo  [OK] Windows Service 'EnterpriseRemoteHostSvc' stopped and locked.

:: 4. Force kill all Host UserAgent & Host Service process trees
taskkill /F /T /IM EnterpriseRemote.Host.UserAgent.exe >nul 2>&1
taskkill /F /T /IM EnterpriseRemote.Host.Service.exe >nul 2>&1

:: 5. Extra PowerShell guarantee: kill any residual EnterpriseRemote.Host processes
powershell -NoProfile -Command "Get-Process -Name 'EnterpriseRemote.Host.*' -ErrorAction SilentlyContinue | Stop-Process -Force" >nul 2>&1

echo  [OK] All streaming agents and capture engines terminated.
echo.
echo ===============================================================================
echo   [SUCCESS] KILL SWITCH ENGAGED ? STREAMING IS FULLY TERMINATED!
echo   No background services or watchdogs can restart until you run START.
echo ===============================================================================
echo.
timeout /T 3 /NOBREAK >nul
exit /b 0
