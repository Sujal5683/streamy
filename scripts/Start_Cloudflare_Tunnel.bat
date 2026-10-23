@echo off
title Enterprise Remote Access - 1-Click Internet Tunnel
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start_tunnel.ps1"
