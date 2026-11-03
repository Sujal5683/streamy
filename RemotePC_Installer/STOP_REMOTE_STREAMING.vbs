' ================================================================================
' Enterprise Remote Access - Remote PC Silent 1-Click Stream Stopper
' ================================================================================
' Double-click to instantly terminate all Remote PC streaming processes:
'   1. Stops Windows Service "EnterpriseRemoteHostSvc" (if running)
'   2. EnterpriseRemote.Host.UserAgent.exe (System Tray Agent & Capture Engine)
'   3. EnterpriseRemote.Host.Service.exe   (Background Host Service)
' ================================================================================

Set WshShell = CreateObject("WScript.Shell")

On Error Resume Next
WshShell.Run "net stop EnterpriseRemoteHostSvc", 0, True
WshShell.Run "taskkill /F /IM EnterpriseRemote.Host.UserAgent.exe /T", 0, True
WshShell.Run "taskkill /F /IM EnterpriseRemote.Host.Service.exe /T", 0, True
On Error GoTo 0
