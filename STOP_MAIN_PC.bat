@echo off
title Enterprise Remote Access ? Stop Main PC Processes
setlocal enabledelayedexpansion

echo.
echo ===============================================================================
echo   Enterprise Remote Access: Terminating All Main PC Processes...
echo ===============================================================================
echo.

taskkill /F /IM EnterpriseRemote.Controller.exe /T >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  [OK] Terminated EnterpriseRemote.Controller.exe (Operator Console)
) else (
    echo  [--] EnterpriseRemote.Controller.exe was not running.
)

taskkill /F /IM EnterpriseRemote.ControlPlane.exe /T >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  [OK] Terminated EnterpriseRemote.ControlPlane.exe (API Server & Relay)
) else (
    echo  [--] EnterpriseRemote.ControlPlane.exe was not running.
)

taskkill /F /IM cloudflared.exe /T >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo  [OK] Terminated cloudflared.exe (Public Tunnel)
) else (
    echo  [--] No active cloudflared tunnel process found.
)

echo.
echo ===============================================================================
echo  [SUCCESS] All Main PC services stopped completely. Port 5293 released.
echo ===============================================================================
echo.
timeout /T 3 /NOBREAK >nul
exit /b 0
