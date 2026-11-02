' ================================================================================
' Enterprise Remote Access - Remote PC Silent Streamer Launcher
' ================================================================================
' Double-click this .vbs file on your REMOTE / SECOND laptop to start streaming.
'
' What this does:
'   1. Checks if this PC has already been enrolled (paired) with the Main PC.
'   2. If NOT enrolled → opens the 1_CLICK_ENROLL_REMOTE_PC.bat enrollment wizard.
'   3. If enrolled     → silently starts the streaming service + tray agent.
'
' The streaming agent runs EXCLUSIVELY in the hidden system tray (bottom-right ^).
' NO windows or taskbar items appear. Look for the shield icon in the tray!
' ================================================================================

' ── Ensure Administrator privileges for UIPI bypass across elevated and lockdown apps ──
Function IsAdmin()
    On Error Resume Next
    Dim sh
    Set sh = CreateObject("WScript.Shell")
    sh.RegRead("HKEY_USERS\S-1-5-19\Environment\TEMP")
    If Err.Number = 0 Then
        IsAdmin = True
    Else
        IsAdmin = False
    End If
    Err.Clear
End Function

If Not IsAdmin() And WScript.Arguments.Count = 0 Then
    On Error Resume Next
    Dim app
    Set app = CreateObject("Shell.Application")
    app.ShellExecute "wscript.exe", """" & WScript.ScriptFullName & """ --elevated", "", "runas", 1
    If Err.Number = 0 Then
        WScript.Quit
    End If
    Err.Clear
End If
Set WshShell = CreateObject("WScript.Shell")
Set fso      = CreateObject("Scripting.FileSystemObject")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
binDir    = scriptDir & "\Bin"
appData   = WshShell.ExpandEnvironmentStrings("%ProgramData%") & "\EnterpriseRemote"

' ── Check for enrollment identity in any of the known locations ─────────────
hasIdentity = False
If fso.FileExists(binDir    & "\device_identity.json") Then hasIdentity = True
If fso.FileExists(scriptDir & "\device_identity.json") Then hasIdentity = True
If fso.FileExists(appData   & "\device_identity.json") Then hasIdentity = True

If Not hasIdentity Then
    ' ── First-time setup: run the enrollment wizard (console window shown) ──
    WshShell.Run """" & scriptDir & "\1_CLICK_ENROLL_REMOTE_PC.bat""", 1, True
Else
    ' ── Already enrolled: start everything silently (Run=0 = hidden) ────────
    WshShell.CurrentDirectory = binDir

    On Error Resume Next
    ' Start the background streaming service
    WshShell.Run """" & binDir & "\EnterpriseRemote.Host.Service.exe"" --run", 0, False

    ' Give the service ~600ms to bind before the tray agent connects
    WScript.Sleep 600

    ' Start the system-tray notification & streaming agent
    WshShell.Run """" & binDir & "\EnterpriseRemote.Host.UserAgent.exe""", 0, False
End If
