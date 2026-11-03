@echo off
title Enterprise Remote Access — Stop Remote PC Streaming
setlocal enabledelayedexpansion

echo.
echo ===============================================================================
echo   Enterprise Remote Access: Terminating Remote PC Streaming...
echo ===============================================================================
echo.

:: Stop Windows Service if installed and active
net stop EnterpriseRemoteHostSvc >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  [OK] Stopped Windows Service 'EnterpriseRemoteHostSvc'.
)

:: Terminate desktop tray and capture agent
taskkill /F /IM EnterpriseRemote.Host.UserAgent.exe /T >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  [OK] Terminated EnterpriseRemote.Host.UserAgent.exe (Capture Agent)
) else (
    echo  [--] EnterpriseRemote.Host.UserAgent.exe was not running.
)

:: Terminate host service process
taskkill /F /IM EnterpriseRemote.Host.Service.exe /T >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  [OK] Terminated EnterpriseRemote.Host.Service.exe (Streaming Engine)
) else (
    echo  [--] EnterpriseRemote.Host.Service.exe was not running.
)

echo.
echo ===============================================================================
echo  [SUCCESS] Remote PC streaming has stopped completely.
echo ===============================================================================
echo.
timeout /T 3 /NOBREAK >nul
exit /b 0
