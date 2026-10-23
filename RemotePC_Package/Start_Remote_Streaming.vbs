' Enterprise Remote Access - Remote PC Silent Streamer Launcher
' Runs desktop streamer directly in the hidden section of the taskbar (Windows System Tray).
' ZERO command prompt / terminal windows are opened.

Set WshShell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
appData = WshShell.ExpandEnvironmentStrings("%ProgramData%") & "\EnterpriseRemote"

hasIdentity = False
If fso.FileExists(scriptDir & "\device_identity.json") Then hasIdentity = True
If fso.FileExists(appData & "\device_identity.json") Then hasIdentity = True

If Not hasIdentity Then
    WshShell.Run """" & scriptDir & "\1-Click_Enroll_And_Start.bat""", 1, True
Else
    WshShell.CurrentDirectory = scriptDir
    WshShell.Run """" & scriptDir & "\EnterpriseRemote.Host.Service.exe"" --run", 0, False
    WshShell.Run """" & scriptDir & "\EnterpriseRemote.Host.UserAgent.exe""", 0, False
End If
