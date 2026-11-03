@echo off
title Enterprise Remote Streamer
cd /d "%~dp0"

:: 1. Check for Administrator permissions to ensure UIPI bypass across elevated windows
net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    if "%~1"=="" (
        powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd -ArgumentList '/c `"%~f0`" --elevated' -Verb runAs" 2>nul
        if %ERRORLEVEL% equ 0 exit /b 0
    )
)

:: 2. Clear Kill Switch flags so processes can launch and stream normally
del /f /q "%ProgramData%\EnterpriseRemote\kill_switch.flag" >nul 2>&1
del /f /q "%~dp0kill_switch.flag" >nul 2>&1
del /f /q "%~dp0Bin\kill_switch.flag" >nul 2>&1

:: 3. Launch the silent streamer
wscript.exe "%~dp0START_REMOTE_STREAMING.vbs"
exit /b 0
