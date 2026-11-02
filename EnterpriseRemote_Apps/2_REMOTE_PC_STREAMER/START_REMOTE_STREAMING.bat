@echo off
title Enterprise Remote Streamer
cd /d "%~dp0"

:: Check for Administrator permissions to ensure UIPI bypass across elevated windows
net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    if "%~1"=="" (
        powershell -Command "Start-Process cmd -ArgumentList '/c `"%~f0`" --elevated' -Verb runAs" 2>nul
        if %ERRORLEVEL% equ 0 exit /b 0
    )
)

wscript.exe "%~dp0START_REMOTE_STREAMING.vbs"
exit /b 0
