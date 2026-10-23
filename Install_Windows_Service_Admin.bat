@echo off
title Install Enterprise Remote Host Service
setlocal EnableDelayedExpansion

:: Check for Administrator permissions
net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [*] Elevating to Administrator to register Windows Service...
    powershell -Command "Start-Process cmd -ArgumentList '/c `"%~f0`"' -Verb runAs"
    exit /b 0
)

cd /d "%~dp0"
set BIN_PATH=%~dp0dist\HostService\EnterpriseRemote.Host.Service.exe

echo ===============================================================================
echo    Enterprise Remote Access: Service Installer (Session 0)
echo ===============================================================================
echo.

if not exist "%BIN_PATH%" (
    echo [!] Error: Executable not found at %BIN_PATH%
    echo     Please build or publish the project first.
    pause
    exit /b 1
)

echo [*] Registering service 'EnterpriseRemoteHostSvc'...
sc.exe create EnterpriseRemoteHostSvc binPath= "\"%BIN_PATH%\" --run" start= auto DisplayName= "Enterprise Remote Access Host Service"

echo [*] Configuring service description and automatic recovery...
sc.exe description EnterpriseRemoteHostSvc "Privileged Session 0 background service for authorized enterprise remote access, session negotiation, and zero-trust hardware identity management."
sc.exe failure EnterpriseRemoteHostSvc reset= 86400 actions= restart/5000/restart/10000/none/0

echo [*] Starting service...
sc.exe start EnterpriseRemoteHostSvc

echo.
echo ===============================================================================
echo [+] Installation Complete! The Host Service is running in Session 0.
echo ===============================================================================
echo.
pause
