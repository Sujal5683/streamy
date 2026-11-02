' ==============================================================================
' Enterprise Remote Access — Silent Web Controller Launcher
' Launches Control Plane silently and opens Web Controller in default browser
' ==============================================================================
Set WshShell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

strCurDir = fso.GetParentFolderName(WScript.ScriptFullName)
strBinDir = strCurDir & "\Bin"

' 1. Check if ControlPlane is running; start silently if needed
Set objWMIService = GetObject("winmgmts:\\.\root\cimv2")
Set colProcesses = objWMIService.ExecQuery("Select * from Win32_Process Where Name = 'EnterpriseRemote.ControlPlane.exe'")

If colProcesses.Count = 0 Then
    WshShell.CurrentDirectory = strBinDir
    WshShell.Run "EnterpriseRemote.ControlPlane.exe", 0, False
    WScript.Sleep 2500
End If

' 2. Determine target URL (localhost:5293)
strTargetUrl = "http://localhost:5293"

' 3. Open in default browser (Chrome, Edge, etc.)
WshShell.Run strTargetUrl, 1, False
