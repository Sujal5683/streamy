@echo off
title Enterprise Remote Access — Main PC Launcher
setlocal

:: Launch both the Control Plane background server and Controller GUI silently
wscript.exe "%~dp0START_MAIN_PC.vbs"
exit /b 0
