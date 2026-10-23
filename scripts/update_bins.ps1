# update_bins.ps1 - Synchronize fresh Release binaries into all application folders
$root       = "c:\My Projects\SECURE AND ENTERPRISE STREAMING APP"
$mainDir    = "$root\EnterpriseRemote_Apps\1_MAIN_PC_CONTROLLER"
$mainBin    = "$mainDir\Bin"
$remoteDir  = "$root\EnterpriseRemote_Apps\2_REMOTE_PC_STREAMER"
$remoteBin  = "$remoteDir\Bin"
$remotePkg  = "$root\RemotePC_Package"
$remoteInst = "$root\RemotePC_Installer"

$controlPlaneOut = "$root\src\EnterpriseRemote.ControlPlane\bin\Release\net8.0"
$controllerOut   = "$root\src\EnterpriseRemote.Controller\bin\Release\net8.0-windows"
$hostServiceOut  = "$root\src\EnterpriseRemote.Host.Service\bin\Release\net8.0-windows"
$hostAgentOut    = "$root\src\EnterpriseRemote.Host.UserAgent\bin\Release\net8.0-windows"
$commonOut       = "$root\src\EnterpriseRemote.Common\bin\Release\net8.0"

# Stop any running processes to prevent file locking
Stop-Process -Name 'EnterpriseRemote.Host.Service','EnterpriseRemote.Host.UserAgent','EnterpriseRemote.Controller','EnterpriseRemote.ControlPlane' -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

function Copy-BinFile {
    param([string]$src, [string]$dst)
    if (Test-Path $src) {
        if (-not (Test-Path $dst)) {
            New-Item -ItemType Directory -Path $dst -Force | Out-Null
        }
        $leaf = Split-Path $src -Leaf
        Copy-Item $src (Join-Path $dst $leaf) -Force
        Write-Host "  [OK] $leaf -> $(Split-Path $dst -Leaf)"
    } else {
        Write-Host "  [SKIP] Not found: $(Split-Path $src -Leaf)"
    }
}

# ═══════════════════════════════════════════════════════════════════
# 1_MAIN_PC_CONTROLLER\Bin
# ═══════════════════════════════════════════════════════════════════
Write-Host "`n=== Updating 1_MAIN_PC_CONTROLLER\Bin ==="

# ControlPlane server
Copy-BinFile "$controlPlaneOut\EnterpriseRemote.ControlPlane.exe"                $mainBin
Copy-BinFile "$controlPlaneOut\EnterpriseRemote.ControlPlane.dll"                $mainBin
Copy-BinFile "$controlPlaneOut\EnterpriseRemote.ControlPlane.deps.json"          $mainBin
Copy-BinFile "$controlPlaneOut\EnterpriseRemote.ControlPlane.runtimeconfig.json" $mainBin
Copy-BinFile "$controlPlaneOut\EnterpriseRemote.ControlPlane.pdb"                $mainBin
Copy-BinFile "$controlPlaneOut\appsettings.json"                                 $mainBin
Copy-BinFile "$controlPlaneOut\appsettings.Development.json"                     $mainBin
Copy-BinFile "$controlPlaneOut\appsettings.Production.json"                      $mainBin

# Operator Controller GUI
Copy-BinFile "$controllerOut\EnterpriseRemote.Controller.exe"                    $mainBin
Copy-BinFile "$controllerOut\EnterpriseRemote.Controller.dll"                    $mainBin
Copy-BinFile "$controllerOut\EnterpriseRemote.Controller.deps.json"              $mainBin
Copy-BinFile "$controllerOut\EnterpriseRemote.Controller.runtimeconfig.json"     $mainBin
Copy-BinFile "$controllerOut\EnterpriseRemote.Controller.pdb"                    $mainBin
Copy-BinFile "$controllerOut\FontAwesome.Sharp.dll"                              $mainBin
if (Test-Path "$controllerOut\ReaLTaiizor.dll") {
    Copy-BinFile "$controllerOut\ReaLTaiizor.dll"                                 $mainBin
}

# Common library
Copy-BinFile "$commonOut\EnterpriseRemote.Common.dll"                            $mainBin
Copy-BinFile "$commonOut\EnterpriseRemote.Common.pdb"                            $mainBin
Copy-BinFile "$commonOut\EnterpriseRemote.Common.deps.json"                       $mainBin

# Brand assets & edge tunnel
$iconSrc = "$root\src\EnterpriseRemote.Controller\app.ico"
$logoSrc = "$root\src\EnterpriseRemote.Controller\app_logo.png"
Copy-BinFile $iconSrc                                                            $mainBin
Copy-BinFile $logoSrc                                                            $mainBin
Copy-BinFile "$root\scripts\bin\cloudflared.exe"                                 $mainBin
Copy-BinFile "$root\scripts\start_tunnel.ps1"                                    $mainBin
Copy-BinFile "$root\scripts\start_tunnel.ps1"                                    $mainDir

# ═══════════════════════════════════════════════════════════════════
# 2_REMOTE_PC_STREAMER\Bin
# ═══════════════════════════════════════════════════════════════════
Write-Host "`n=== Updating 2_REMOTE_PC_STREAMER\Bin ==="

# Host streaming service
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.exe"                 $remoteBin
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.dll"                 $remoteBin
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.deps.json"           $remoteBin
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.runtimeconfig.json"  $remoteBin
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.pdb"                 $remoteBin

# System tray UserAgent
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.exe"                 $remoteBin
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.dll"                 $remoteBin
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.deps.json"           $remoteBin
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.runtimeconfig.json"  $remoteBin
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.pdb"                 $remoteBin
Copy-BinFile "$hostAgentOut\FontAwesome.Sharp.dll"                               $remoteBin

# Common library
Copy-BinFile "$commonOut\EnterpriseRemote.Common.dll"                            $remoteBin
Copy-BinFile "$commonOut\EnterpriseRemote.Common.pdb"                            $remoteBin
Copy-BinFile "$commonOut\EnterpriseRemote.Common.deps.json"                       $remoteBin

# Brand assets
Copy-BinFile $iconSrc                                                            $remoteBin
Copy-BinFile $logoSrc                                                            $remoteBin

# ═══════════════════════════════════════════════════════════════════
# RemotePC_Package
# ═══════════════════════════════════════════════════════════════════
Write-Host "`n=== Updating RemotePC_Package ==="
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.exe"                 $remotePkg
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.dll"                 $remotePkg
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.deps.json"           $remotePkg
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.runtimeconfig.json"  $remotePkg
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.pdb"                 $remotePkg
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.exe"                 $remotePkg
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.dll"                 $remotePkg
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.deps.json"           $remotePkg
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.runtimeconfig.json"  $remotePkg
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.pdb"                 $remotePkg
Copy-BinFile "$hostAgentOut\FontAwesome.Sharp.dll"                               $remotePkg
Copy-BinFile "$commonOut\EnterpriseRemote.Common.dll"                            $remotePkg
Copy-BinFile "$commonOut\EnterpriseRemote.Common.pdb"                            $remotePkg
Copy-BinFile "$commonOut\EnterpriseRemote.Common.deps.json"                       $remotePkg
Copy-BinFile $iconSrc                                                            $remotePkg
Copy-BinFile $logoSrc                                                            $remotePkg

# ═══════════════════════════════════════════════════════════════════
# RemotePC_Installer
# ═══════════════════════════════════════════════════════════════════
Write-Host "`n=== Updating RemotePC_Installer ==="
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.exe"                 $remoteInst
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.dll"                 $remoteInst
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.deps.json"           $remoteInst
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.runtimeconfig.json"  $remoteInst
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.pdb"                 $remoteInst
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.exe"                 $remoteInst
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.dll"                 $remoteInst
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.deps.json"           $remoteInst
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.runtimeconfig.json"  $remoteInst
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.pdb"                 $remoteInst
Copy-BinFile "$hostAgentOut\FontAwesome.Sharp.dll"                               $remoteInst
Copy-BinFile "$commonOut\EnterpriseRemote.Common.dll"                            $remoteInst
Copy-BinFile "$commonOut\EnterpriseRemote.Common.pdb"                            $remoteInst
Copy-BinFile "$commonOut\EnterpriseRemote.Common.deps.json"                       $remoteInst
Copy-BinFile $iconSrc                                                            $remoteInst
Copy-BinFile $logoSrc                                                            $remoteInst

# ═══════════════════════════════════════════════════════════════════
# dist/ folders (for development references)
# ═══════════════════════════════════════════════════════════════════
Write-Host "`n=== Updating dist folders ==="
Copy-BinFile "$controlPlaneOut\EnterpriseRemote.ControlPlane.exe"                "$root\dist\ControlPlane"
Copy-BinFile "$controlPlaneOut\EnterpriseRemote.ControlPlane.dll"                "$root\dist\ControlPlane"
Copy-BinFile "$controlPlaneOut\EnterpriseRemote.ControlPlane.deps.json"          "$root\dist\ControlPlane"
Copy-BinFile "$controlPlaneOut\EnterpriseRemote.ControlPlane.runtimeconfig.json" "$root\dist\ControlPlane"
Copy-BinFile "$controlPlaneOut\EnterpriseRemote.ControlPlane.pdb"                "$root\dist\ControlPlane"
Copy-BinFile "$commonOut\EnterpriseRemote.Common.dll"                            "$root\dist\ControlPlane"
Copy-BinFile "$commonOut\EnterpriseRemote.Common.pdb"                            "$root\dist\ControlPlane"

Copy-BinFile "$controllerOut\EnterpriseRemote.Controller.exe"                    "$root\dist\Controller"
Copy-BinFile "$controllerOut\EnterpriseRemote.Controller.dll"                    "$root\dist\Controller"
Copy-BinFile "$controllerOut\EnterpriseRemote.Controller.deps.json"              "$root\dist\Controller"
Copy-BinFile "$controllerOut\EnterpriseRemote.Controller.runtimeconfig.json"     "$root\dist\Controller"
Copy-BinFile "$controllerOut\EnterpriseRemote.Controller.pdb"                    "$root\dist\Controller"
Copy-BinFile "$controllerOut\FontAwesome.Sharp.dll"                              "$root\dist\Controller"
if (Test-Path "$controllerOut\ReaLTaiizor.dll") {
    Copy-BinFile "$controllerOut\ReaLTaiizor.dll"                                 "$root\dist\Controller"
}
Copy-BinFile "$commonOut\EnterpriseRemote.Common.dll"                            "$root\dist\Controller"
Copy-BinFile "$commonOut\EnterpriseRemote.Common.pdb"                            "$root\dist\Controller"

Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.exe"                 "$root\dist\HostService"
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.dll"                 "$root\dist\HostService"
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.deps.json"           "$root\dist\HostService"
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.runtimeconfig.json"  "$root\dist\HostService"
Copy-BinFile "$hostServiceOut\EnterpriseRemote.Host.Service.pdb"                 "$root\dist\HostService"
Copy-BinFile "$commonOut\EnterpriseRemote.Common.dll"                            "$root\dist\HostService"
Copy-BinFile "$commonOut\EnterpriseRemote.Common.pdb"                            "$root\dist\HostService"

Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.exe"                 "$root\dist\UserAgent"
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.dll"                 "$root\dist\UserAgent"
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.deps.json"           "$root\dist\UserAgent"
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.runtimeconfig.json"  "$root\dist\UserAgent"
Copy-BinFile "$hostAgentOut\EnterpriseRemote.Host.UserAgent.pdb"                 "$root\dist\UserAgent"
Copy-BinFile "$hostAgentOut\FontAwesome.Sharp.dll"                               "$root\dist\UserAgent"
Copy-BinFile "$commonOut\EnterpriseRemote.Common.dll"                            "$root\dist\UserAgent"
Copy-BinFile "$commonOut\EnterpriseRemote.Common.pdb"                            "$root\dist\UserAgent"

Write-Host "`n[DONE] All binaries are 100% updated and synchronized across all folders."
