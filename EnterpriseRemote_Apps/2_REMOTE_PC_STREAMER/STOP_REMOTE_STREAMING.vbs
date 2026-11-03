' ================================================================================
' Enterprise Remote Access - Remote PC Silent 1-Click Stream Kill Switch
' ================================================================================
' Double-click to instantly terminate all Remote PC streaming processes:
'   1. Activates Kill Switch flag so processes CANNOT respawn
'   2. Stops Windows Service "EnterpriseRemoteHostSvc"
'   3. Terminates EnterpriseRemote.Host.UserAgent.exe (Capture Agent)
'   4. Terminates EnterpriseRemote.Host.Service.exe   (Background Host Service)
' ================================================================================

Set WshShell = CreateObject("WScript.Shell")
Set fso      = CreateObject("Scripting.FileSystemObject")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
binDir    = scriptDir & "\Bin"
appData   = WshShell.ExpandEnvironmentStrings("%ProgramData%") & "\EnterpriseRemote"

' ── 1. Drop the permanent Kill Switch flag in all known locations ────────────
On Error Resume Next
If Not fso.FolderExists(appData) Then fso.CreateFolder(appData)
Set f1 = fso.CreateTextFile(appData & "\kill_switch.flag", True)
f1.WriteLine Now: f1.Close
Set f2 = fso.CreateTextFile(scriptDir & "\kill_switch.flag", True)
f2.WriteLine Now: f2.Close
If fso.FolderExists(binDir) Then
    Set f3 = fso.CreateTextFile(binDir & "\kill_switch.flag", True)
    f3.WriteLine Now: f3.Close
End If

' ── 2. Stop Service and Terminate Processes ──────────────────────────────────
WshShell.Run "sc.exe config EnterpriseRemoteHostSvc start= demand", 0, True
WshShell.Run "sc.exe stop EnterpriseRemoteHostSvc", 0, True
WshShell.Run "net stop EnterpriseRemoteHostSvc", 0, True
WshShell.Run "taskkill /F /IM EnterpriseRemote.Host.UserAgent.exe /T", 0, True
WshShell.Run "taskkill /F /IM EnterpriseRemote.Host.Service.exe /T", 0, True
WshShell.Run "powershell -NoProfile -Command ""Get-Process -Name 'EnterpriseRemote.Host.*' -ErrorAction SilentlyContinue | Stop-Process -Force""", 0, True
On Error GoTo 0
