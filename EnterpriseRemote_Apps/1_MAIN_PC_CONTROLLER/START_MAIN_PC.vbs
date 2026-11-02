' ================================================================================
' Enterprise Remote Access - Main PC Silent Launcher
' ================================================================================
' Double-click this .vbs file to start EVERYTHING with ZERO console windows.
'
' What this does:
'   1. Cleans up any stale background processes to ensure fresh code and UI
'   2. Silently starts the Control Plane background API server (port 5293)
'   3. Launches the Operator Controller GUI (modern dark theme UI)
'
' The Control Plane runs as a hidden background process.
' The Controller GUI appears in your taskbar.
' ================================================================================

Set WshShell = CreateObject("WScript.Shell")
Set fso      = CreateObject("Scripting.FileSystemObject")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
binDir    = scriptDir & "\Bin"

' ── Terminate any stale existing instances to guarantee fresh code & UI ───────
On Error Resume Next
WshShell.Run "taskkill /F /IM EnterpriseRemote.Controller.exe /T", 0, True
WshShell.Run "taskkill /F /IM EnterpriseRemote.ControlPlane.exe /T", 0, True
On Error GoTo 0
WScript.Sleep 500

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
