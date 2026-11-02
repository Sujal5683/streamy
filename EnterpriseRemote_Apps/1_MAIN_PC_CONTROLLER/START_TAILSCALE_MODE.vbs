Set WshShell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

' Resolve the PowerShell script path
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
projectRoot = fso.GetParentFolderName(fso.GetParentFolderName(scriptDir))

Dim ps1Path
ps1Path = projectRoot & "\scripts\start_tailscale_tunnel.ps1"
If Not fso.FileExists(ps1Path) Then
    ps1Path = scriptDir & "\start_tailscale_tunnel.ps1"
End If
If Not fso.FileExists(ps1Path) Then
    ps1Path = scriptDir & "\Bin\start_tailscale_tunnel.ps1"
End If

If Not fso.FileExists(ps1Path) Then
    MsgBox "Could not find start_tailscale_tunnel.ps1." & vbCrLf & _
           "Expected at: " & projectRoot & "\scripts\start_tailscale_tunnel.ps1", _
           vbExclamation, "Enterprise Remote Access - Tailscale"
    WScript.Quit 1
End If

' Run with a visible PowerShell window so the user can see the Tailscale IP
cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File """ & ps1Path & """"
WshShell.Run cmd, 1, False
