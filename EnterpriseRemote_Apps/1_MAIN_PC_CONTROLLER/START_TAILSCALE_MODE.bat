@echo off
title Enterprise Remote Access - Tailscale Low-Latency Mode
cd /d "%~dp0"

echo.
echo  ================================================================
echo    ENTERPRISE REMOTE ACCESS - TAILSCALE LOW-LATENCY MODE
echo    Ultra-Low WAN Latency via WireGuard UDP (P2P Direct)
echo  ================================================================
echo.
echo   Checking Tailscale status and showing your Tailscale IP...
echo   Use this IP on your Remote PC for near-LAN latency anywhere.
echo.
echo  ================================================================
echo.

set SCRIPT_PATH=%~dp0..\..\scripts\start_tailscale_tunnel.ps1
if not exist "%SCRIPT_PATH%" set SCRIPT_PATH=%~dp0start_tailscale_tunnel.ps1
if not exist "%SCRIPT_PATH%" set SCRIPT_PATH=%~dp0Bin\start_tailscale_tunnel.ps1

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_PATH%"
if %ERRORLEVEL% neq 0 (
    echo.
    echo  [!] Script exited with code %ERRORLEVEL%.
    pause
)
