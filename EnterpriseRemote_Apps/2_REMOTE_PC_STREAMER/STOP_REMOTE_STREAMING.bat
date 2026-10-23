@echo off
title Enterprise Remote Access — Stop Streaming
setlocal

:: Kill all remote streaming processes cleanly
taskkill /F /IM EnterpriseRemote.Host.UserAgent.exe >nul 2>&1
taskkill /F /IM EnterpriseRemote.Host.Service.exe   >nul 2>&1

echo.
echo  [OK] Enterprise Remote streaming has stopped.
echo       You can close this window.
echo.
timeout /T 3 /NOBREAK >nul
exit /b 0
