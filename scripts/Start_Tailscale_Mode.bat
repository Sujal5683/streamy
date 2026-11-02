@echo off
title Enterprise Remote Access - Tailscale Low-Latency Mode
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start_tailscale_tunnel.ps1"
