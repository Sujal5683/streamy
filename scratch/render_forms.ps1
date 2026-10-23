Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$distDir = Resolve-Path "dist\Controller"
$controllerDll = Join-Path $distDir "EnterpriseRemote.Controller.dll"
$faDll = Join-Path $distDir "FontAwesome.Sharp.dll"
$commonDll = Join-Path $distDir "EnterpriseRemote.Common.dll"

[System.Reflection.Assembly]::LoadFrom($commonDll) | Out-Null
[System.Reflection.Assembly]::LoadFrom($faDll) | Out-Null
[System.Reflection.Assembly]::LoadFrom($controllerDll) | Out-Null

$outDir = Join-Path $PSScriptRoot "screenshots"
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

# 1. Render Main RemoteViewerForm
$viewerForm = New-Object EnterpriseRemote.Controller.UI.RemoteViewerForm
$viewerForm.Size = New-Object System.Drawing.Size 1440, 900
$viewerForm.CreateControl()

$bmpViewer = New-Object System.Drawing.Bitmap $viewerForm.Width, $viewerForm.Height
$viewerForm.DrawToBitmap($bmpViewer, (New-Object System.Drawing.Rectangle 0, 0, $viewerForm.Width, $viewerForm.Height))
$viewerPath = Join-Path $outDir "RemoteViewerForm_Render.png"
$bmpViewer.Save($viewerPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmpViewer.Dispose()
$viewerForm.Dispose()
Write-Host "[+] RemoteViewerForm rendered to: $viewerPath"

# 2. Render EnrollRemoteDeviceForm
$enrollForm = New-Object EnterpriseRemote.Controller.UI.EnrollRemoteDeviceForm "http://127.0.0.1:5293", "USR-ADMIN-01"
$enrollForm.Size = New-Object System.Drawing.Size 820, 740
$enrollForm.CreateControl()

$bmpEnroll = New-Object System.Drawing.Bitmap $enrollForm.Width, $enrollForm.Height
$enrollForm.DrawToBitmap($bmpEnroll, (New-Object System.Drawing.Rectangle 0, 0, $enrollForm.Width, $enrollForm.Height))
$enrollPath = Join-Path $outDir "EnrollRemoteDeviceForm_Render.png"
$bmpEnroll.Save($enrollPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmpEnroll.Dispose()
$enrollForm.Dispose()
Write-Host "[+] EnrollRemoteDeviceForm rendered to: $enrollPath"
