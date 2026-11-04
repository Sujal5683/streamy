# update_bins.ps1 - Compile, Publish & Synchronize Self-Contained Release Binaries
param(
    [switch]$SkipZip = $false
)

$root       = "c:\My Projects\SECURE AND ENTERPRISE STREAMING APP"
$mainDir    = "$root\EnterpriseRemote_Apps\1_MAIN_PC_CONTROLLER"
$mainBin    = "$mainDir\Bin"
$remoteDir  = "$root\EnterpriseRemote_Apps\2_REMOTE_PC_STREAMER"
$remoteBin  = "$remoteDir\Bin"
$remotePkg  = "$root\RemotePC_Package"
$remoteInst = "$root\RemotePC_Installer"

Write-Host "==============================================================================="
Write-Host " Enterprise Remote: Universal Binary Build & Distribution Synchronizer"
Write-Host " Target Runtime: win-x64 (100% Self-Contained • Zero .NET Installation Needed)"
Write-Host "===============================================================================`n"

# 1. Stop any running processes to prevent file locking
Write-Host "[1/5] Stopping running instances to release file locks..."
Stop-Process -Name 'EnterpriseRemote.Host.Service','EnterpriseRemote.Host.UserAgent','EnterpriseRemote.Controller','EnterpriseRemote.ControlPlane','cloudflared' -Force -ErrorAction SilentlyContinue
taskkill /F /IM EnterpriseRemote.Host.Service.exe /IM EnterpriseRemote.Host.UserAgent.exe /IM EnterpriseRemote.Controller.exe /IM EnterpriseRemote.ControlPlane.exe /IM cloudflared.exe 2>$null
Start-Sleep -Milliseconds 800

# 2. Publish 1_MAIN_PC_CONTROLLER (ControlPlane + Controller)
Write-Host "`n[2/5] Publishing 1_MAIN_PC_CONTROLLER binaries..."
New-Item -ItemType Directory -Path $mainBin -Force | Out-Null
dotnet publish "$root\src\EnterpriseRemote.ControlPlane\EnterpriseRemote.ControlPlane.csproj" -c Release -r win-x64 --self-contained true -o "$mainBin"
dotnet publish "$root\src\EnterpriseRemote.Controller\EnterpriseRemote.Controller.csproj" -c Release -r win-x64 --self-contained true -o "$mainBin"

# Copy brand assets, scripts, and tunnel binary to 1_MAIN_PC_CONTROLLER
Copy-Item "$root\src\EnterpriseRemote.Controller\app.ico" "$mainBin\app.ico" -Force -ErrorAction SilentlyContinue
Copy-Item "$root\src\EnterpriseRemote.Controller\app_logo.png" "$mainBin\app_logo.png" -Force -ErrorAction SilentlyContinue
Copy-Item "$root\scripts\bin\cloudflared.exe" "$mainBin\cloudflared.exe" -Force -ErrorAction SilentlyContinue
Copy-Item "$root\scripts\start_tunnel.ps1" "$mainBin\start_tunnel.ps1" -Force -ErrorAction SilentlyContinue
Copy-Item "$root\scripts\start_tunnel.ps1" "$mainDir\start_tunnel.ps1" -Force -ErrorAction SilentlyContinue

# Ensure wwwroot is fully copied
if (Test-Path "$root\src\EnterpriseRemote.ControlPlane\wwwroot") {
    Copy-Item -Path "$root\src\EnterpriseRemote.ControlPlane\wwwroot\*" -Destination "$mainBin\wwwroot" -Recurse -Force
    Write-Host "  [OK] wwwroot -> $mainBin\wwwroot"
}

# 3. Publish 2_REMOTE_PC_STREAMER (Host.Service + UserAgent) to staging folder
Write-Host "`n[3/5] Publishing 2_REMOTE_PC_STREAMER binaries..."
$remoteStagingBin = "$remoteDir\Bin_Staging"
if (Test-Path $remoteStagingBin) { Remove-Item $remoteStagingBin -Recurse -Force }
New-Item -ItemType Directory -Path $remoteStagingBin -Force | Out-Null

dotnet publish "$root\src\EnterpriseRemote.Host.Service\EnterpriseRemote.Host.Service.csproj" -c Release -r win-x64 --self-contained true -o "$remoteStagingBin"
dotnet publish "$root\src\EnterpriseRemote.Host.UserAgent\EnterpriseRemote.Host.UserAgent.csproj" -c Release -r win-x64 --self-contained true -o "$remoteStagingBin"

Copy-Item "$root\src\EnterpriseRemote.Controller\app.ico" "$remoteStagingBin\app.ico" -Force -ErrorAction SilentlyContinue
Copy-Item "$root\src\EnterpriseRemote.Controller\app_logo.png" "$remoteStagingBin\app_logo.png" -Force -ErrorAction SilentlyContinue

# Attempt direct copy to local Bin if not locked
New-Item -ItemType Directory -Path $remoteBin -Force | Out-Null
Get-ChildItem -Path "$remoteStagingBin\*" | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $remoteBin -Recurse -Force -ErrorAction SilentlyContinue
}

# 4. Synchronize RemotePC_Package and RemotePC_Installer
Write-Host "`n[4/5] Synchronizing RemotePC_Package & RemotePC_Installer..."
New-Item -ItemType Directory -Path $remotePkg -Force | Out-Null
New-Item -ItemType Directory -Path $remoteInst -Force | Out-Null
Copy-Item -Path "$remoteStagingBin\*" -Destination "$remotePkg" -Recurse -Force
Copy-Item -Path "$remoteStagingBin\*" -Destination "$remoteInst" -Recurse -Force
Remove-Item $remoteStagingBin -Recurse -Force -ErrorAction SilentlyContinue

# Clean up any leftover test identity files from Remote PC distribution folders before packaging
@($remoteDir, $remoteBin, $remoteStagingBin, $remotePkg, $remoteInst) | ForEach-Object {
    Remove-Item (Join-Path $_ "device_identity.json") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $_ "server_url.txt") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $_ "kill_switch.flag") -Force -ErrorAction SilentlyContinue
}

# 5. Repackage Standalone Distribution ZIPs
if (-not $SkipZip) {
    Write-Host "`n[5/5] Re-packaging standalone distribution zip archives..."
    powershell -ExecutionPolicy Bypass -File "$root\scripts\create_zip.ps1" -SourceDir "$mainDir" -ZipPath "$root\EnterpriseRemote_Apps\1_MAIN_PC_CONTROLLER.zip"
    
    # Stage complete remote dir for zip creation
    $zipStaging = "$root\EnterpriseRemote_Apps\2_REMOTE_PC_STREAMER_ZIP_STAGING"
    if (Test-Path $zipStaging) { Remove-Item $zipStaging -Recurse -Force }
    New-Item -ItemType Directory -Path $zipStaging -Force | Out-Null
    Get-ChildItem -Path $remoteDir -Exclude "Bin","Bin_Staging","Bin_New" | Copy-Item -Destination $zipStaging -Recurse -Force
    Copy-Item -Path $remoteStagingBin -Destination "$zipStaging\Bin" -Recurse -Force
    
    powershell -ExecutionPolicy Bypass -File "$root\scripts\create_zip.ps1" -SourceDir "$zipStaging" -ZipPath "$root\EnterpriseRemote_Apps\2_REMOTE_PC_STREAMER.zip"
    Remove-Item $zipStaging -Recurse -Force
}

if (Test-Path "$remoteDir\Bin_New") { Remove-Item "$remoteDir\Bin_New" -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host "`n==============================================================================="
Write-Host " [SUCCESS] All self-contained binaries & ZIP distributions updated 100%!"
Write-Host "==============================================================================="
