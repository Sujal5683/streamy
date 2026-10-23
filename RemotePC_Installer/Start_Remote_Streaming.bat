@echo off
title Enterprise Remote Access - Start Remote Streaming
cd /d "%~dp0"

echo [*] Starting Enterprise Remote Host Service in background...
start "" EnterpriseRemote.Host.Service.exe --run --server http://172.18.11.122:5293
start "" EnterpriseRemote.Host.UserAgent.exe --server http://172.18.11.122:5293

echo [+] Remote desktop streaming is active!
timeout /t 5
