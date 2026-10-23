@echo off
title Enterprise Remote Access - Stop Streaming
taskkill /F /IM EnterpriseRemote.Host.UserAgent.exe /IM EnterpriseRemote.Host.Service.exe >nul 2>&1
echo [+] Enterprise Remote desktop streaming has stopped.
exit /b 0
