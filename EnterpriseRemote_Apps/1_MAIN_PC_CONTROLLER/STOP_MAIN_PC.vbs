' ================================================================================
' Enterprise Remote Access - Main PC Silent 1-Click Stopper
' ================================================================================
' Double-click to instantly terminate all Main PC processes with ZERO windows:
'   1. EnterpriseRemote.Controller.exe  (Operator GUI)
'   2. EnterpriseRemote.ControlPlane.exe (Background Server, releases Port 5293)
'   3. cloudflared.exe                   (Active Cloudflare Tunnels)
' ================================================================================

Set WshShell = CreateObject("WScript.Shell")

On Error Resume Next
WshShell.Run "taskkill /F /IM EnterpriseRemote.Controller.exe /T", 0, True
WshShell.Run "taskkill /F /IM EnterpriseRemote.ControlPlane.exe /T", 0, True
WshShell.Run "taskkill /F /IM cloudflared.exe /T", 0, True
On Error GoTo 0

