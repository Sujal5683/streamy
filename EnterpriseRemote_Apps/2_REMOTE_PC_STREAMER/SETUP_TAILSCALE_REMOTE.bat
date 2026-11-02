@echo off
title Enterprise Remote Access - Tailscale Remote PC Setup
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup_tailscale_remote.ps1"
if %ERRORLEVEL% neq 0 (
    echo.
    echo  [!] Script exited with code %ERRORLEVEL%.
    pause
)
