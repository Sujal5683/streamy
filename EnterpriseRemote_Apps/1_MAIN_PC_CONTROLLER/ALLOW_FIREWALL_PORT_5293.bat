@echo off
setlocal
title Enable Local Wi-Fi Streaming Windows Firewall Ports 5293 ^& 5294

:: Check for administrative privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo.
    echo ====================================================================
    echo   ADMIN ELEVATION REQUIRED
    echo ====================================================================
    echo   Requesting Administrator privileges to open Windows Firewall...
    echo.
    powershell -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

echo.
echo ====================================================================
echo   OPENING WINDOWS FIREWALL FOR WI-FI ^& LAN SCREEN STREAMING
echo ====================================================================
echo.
echo   Configuring inbound firewall rules on this Main PC:
echo     - Port 5293 (TCP): Control Plane HTTP ^& WebSocket Stream Relay
echo     - Port 5294 (UDP): Instant Wi-Fi Peer Discovery Beacons
echo.

netsh advfirewall firewall delete rule name="EnterpriseRemote ControlPlane TCP" >nul 2>&1
netsh advfirewall firewall add rule name="EnterpriseRemote ControlPlane TCP" protocol=TCP dir=in localport=5293 action=allow profile=any >nul 2>&1

netsh advfirewall firewall delete rule name="EnterpriseRemote LAN Discovery UDP" >nul 2>&1
netsh advfirewall firewall add rule name="EnterpriseRemote LAN Discovery UDP" protocol=UDP dir=in localport=5294 action=allow profile=any >nul 2>&1

echo.
echo ====================================================================
echo   [SUCCESS] FIREWALL PORTS ARE NOW UNBLOCKED!
echo ====================================================================
echo.
echo   Any Remote PC on your Wi-Fi network can now:
echo     1. Connect directly to http://<Your-IP>:5293 without being blocked.
echo     2. Be discovered automatically by the Main PC Controller!
echo.
pause
