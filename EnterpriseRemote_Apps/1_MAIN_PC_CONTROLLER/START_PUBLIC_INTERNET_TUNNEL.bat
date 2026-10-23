@echo off
title Enterprise Remote Access - 1-Click Internet Tunnel (Connect From Anywhere)
cd /d "%~dp0"

echo.
echo  ================================================================
echo    ENTERPRISE REMOTE ACCESS - 1-CLICK PUBLIC INTERNET TUNNEL
echo  ================================================================
echo.
echo   Starting secure zero-config Cloudflare edge tunnel...
echo   This allows you to stream between PCs across DIFFERENT networks
echo   (e.g., mobile hotspot, remote home, coffee shop, another city).
echo.
echo  ================================================================
echo.

set SCRIPT_PATH=%~dp0start_tunnel.ps1
if not exist "%SCRIPT_PATH%" set SCRIPT_PATH=%~dp0Bin\start_tunnel.ps1
if not exist "%SCRIPT_PATH%" set SCRIPT_PATH=%~dp0..\..\scripts\start_tunnel.ps1

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_PATH%"
if %ERRORLEVEL% neq 0 (
    echo.
    echo  [!] Tunnel closed or exited with code %ERRORLEVEL%.
    pause
)
