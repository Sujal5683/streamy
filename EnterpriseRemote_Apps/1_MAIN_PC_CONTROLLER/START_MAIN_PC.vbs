' ================================================================================
' Enterprise Remote Access - Main PC Silent Launcher
' ================================================================================
' Double-click this .vbs file to start EVERYTHING with ZERO console windows.
'
' What this does:
'   1. Silently starts the Control Plane background API server (port 5293)
'   2. Launches the Operator Controller GUI (modern dark theme UI)
'
' The Control Plane runs as a hidden background process.
' The Controller GUI appears in your taskbar.
' ================================================================================

Set WshShell = CreateObject("WScript.Shell")
Set fso      = CreateObject("Scripting.FileSystemObject")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
binDir    = scriptDir & "\Bin"

' ── Start the Control Plane (ASP.NET API backend) silently ──────────────────
' Run = 0 means completely hidden window, False means don't wait
WshShell.CurrentDirectory = binDir
WshShell.Run """" & binDir & "\EnterpriseRemote.ControlPlane.exe""", 0, False

' Short delay so the Control Plane socket is ready before the Controller connects
WScript.Sleep 1200

' ── Launch the Operator Controller GUI ─────────────────────────────────────
' Run = 1 means normal window (the GUI itself needs to be visible)
WshShell.Run """" & binDir & "\EnterpriseRemote.Controller.exe""", 1, False

' Done — both processes are now running independently.
