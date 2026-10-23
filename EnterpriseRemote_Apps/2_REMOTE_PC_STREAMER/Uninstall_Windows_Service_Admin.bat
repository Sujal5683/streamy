@echo off
title Uninstall Enterprise Remote Host Service

net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [*] Elevating to Administrator to remove Windows Service...
    powershell -Command "Start-Process cmd -ArgumentList '/c `"%~f0`"' -Verb runAs"
    exit /b 0
)

echo ===============================================================================
echo    Enterprise Remote Access: Service Uninstaller
echo ===============================================================================
echo.

echo [*] Stopping service 'EnterpriseRemoteHostSvc'...
sc.exe stop EnterpriseRemoteHostSvc >nul 2>&1

echo [*] Deleting service...
sc.exe delete EnterpriseRemoteHostSvc

echo.
echo ===============================================================================
echo [+] Service removed cleanly from Windows Service Control Manager.
echo ===============================================================================
echo.
pause
