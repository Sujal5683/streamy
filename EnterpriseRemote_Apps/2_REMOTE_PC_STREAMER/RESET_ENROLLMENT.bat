@echo off
setlocal
title Reset Remote PC Enrollment Identity
cd /d "%~dp0"

echo.
echo ====================================================================
echo   RESET ENTERPRISE REMOTE ENROLLMENT IDENTITY
echo ====================================================================
echo.
echo   Stopping running streamer processes...
taskkill /f /im EnterpriseRemote.Host.Service.exe >nul 2>&1
taskkill /f /im EnterpriseRemote.Host.UserAgent.exe >nul 2>&1

echo   Removing previous device identities...
if exist "%~dp0device_identity.json" del /f /q "%~dp0device_identity.json" >nul 2>&1
if exist "%~dp0Bin\device_identity.json" del /f /q "%~dp0Bin\device_identity.json" >nul 2>&1
if exist "%ProgramData%\EnterpriseRemote\device_identity.json" del /f /q "%ProgramData%\EnterpriseRemote\device_identity.json" >nul 2>&1
if exist "%~dp0server_url.txt" del /f /q "%~dp0server_url.txt" >nul 2>&1
if exist "%~dp0Bin\server_url.txt" del /f /q "%~dp0Bin\server_url.txt" >nul 2>&1

echo.
echo   [SUCCESS] Device identity reset cleanly.
echo   You can now run "1_CLICK_ENROLL_REMOTE_PC.bat" to pair with your Main PC.
echo.
pause
