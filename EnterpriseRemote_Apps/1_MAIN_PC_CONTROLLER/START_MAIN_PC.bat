@echo off
title Enterprise Remote Access — Main PC Launcher
setlocal

:: Change into the Bin directory (where all executables live)
cd /d "%~dp0Bin"

:: ── Start the Control Plane API backend silently ────────────────────────────
::    /B = no new window, START returns immediately
start "" /B EnterpriseRemote.ControlPlane.exe

:: Give the API server ~1 second to bind the port before the Controller starts
timeout /T 1 /NOBREAK >nul 2>&1

:: ── Launch the Operator Controller GUI ─────────────────────────────────────
start "" EnterpriseRemote.Controller.exe

:: Exit immediately — no terminal window stays on the taskbar
exit /b 0
