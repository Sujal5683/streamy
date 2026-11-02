# ================================================================================
# Enterprise Remote Access - 1-Click Cloudflare Tunnel (Connect from Anywhere)
# ================================================================================
# Creates a secure, encrypted HTTPS/WSS public tunnel to your Control Plane on port 5293.
# Allows operators and remote workstations to connect from ANY network (different Wi-Fi,
# 4G/5G mobile hotspot, across the world) without configuring router ports or firewall NAT.
# ================================================================================

param(
    [string]$TargetPort = "5293"
)

$ErrorActionPreference = "Continue"
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $projectRoot) { $projectRoot = "c:\My Projects\SECURE AND ENTERPRISE STREAMING APP" }

Clear-Host
Write-Host ""
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "   ENTERPRISE REMOTE ACCESS - 1-CLICK INTERNET TUNNEL (CONNECT ANYWHERE)         " -ForegroundColor White
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Locate cloudflared.exe
$cloudflaredCandidates = @(
    (Join-Path $PSScriptRoot "Bin\cloudflared.exe"),
    (Join-Path $PSScriptRoot "bin\cloudflared.exe"),
    (Join-Path $PSScriptRoot "cloudflared.exe"),
    (Join-Path $projectRoot "EnterpriseRemote_Apps\1_MAIN_PC_CONTROLLER\Bin\cloudflared.exe"),
    (Join-Path $projectRoot "scripts\bin\cloudflared.exe"),
    (Get-Command "cloudflared" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1)
)

$cfExe = $null
foreach ($c in $cloudflaredCandidates) {
    if ($c -and (Test-Path $c)) {
        $cfExe = $c
        break
    }
}

if (-not $cfExe) {
    Write-Host "[*] cloudflared.exe not found locally. Downloading official portable binary..." -ForegroundColor Yellow
    $binDir = Join-Path $PSScriptRoot "bin"
    if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir -Force | Out-Null }
    $cfExe = Join-Path $binDir "cloudflared.exe"
    
    try {
        & curl.exe -L -s -o $cfExe "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"
        if (Test-Path $cfExe) {
            Write-Host "[+] Downloaded portable cloudflared.exe successfully!" -ForegroundColor Green
        } else {
            throw "File download did not complete."
        }
    } catch {
        Write-Host "[!] Error downloading cloudflared: $_" -ForegroundColor Red
        Write-Host "    Please place cloudflared.exe in $binDir manually." -ForegroundColor Yellow
        pause
        exit 1
    }
}

Write-Host "[+] Cloudflare Tunnel Engine: $cfExe" -ForegroundColor Green

# 2. Verify Control Plane is running on port 5293
$isPortListening = $false
try {
    $client = New-Object System.Net.Sockets.TcpClient
    $iar = $client.BeginConnect("127.0.0.1", [int]$TargetPort, $null, $null)
    if ($iar.AsyncWaitHandle.WaitOne(800, $false)) {
        $client.EndConnect($iar)
        $isPortListening = $true
    }
    $client.Close()
} catch {
    $isPortListening = $false
}

if (-not $isPortListening) {
    Write-Host "[*] Control Plane is not yet running on port $TargetPort." -ForegroundColor Yellow
    Write-Host "    Starting background Control Plane service..." -ForegroundColor Cyan
    
    $cpExeCandidates = @(
        (Join-Path $PSScriptRoot "Bin\EnterpriseRemote.ControlPlane.exe"),
        (Join-Path $PSScriptRoot "EnterpriseRemote.ControlPlane.exe"),
        (Join-Path $projectRoot "EnterpriseRemote_Apps\1_MAIN_PC_CONTROLLER\Bin\EnterpriseRemote.ControlPlane.exe"),
        (Join-Path $projectRoot "src\EnterpriseRemote.ControlPlane\bin\Release\net8.0\EnterpriseRemote.ControlPlane.exe"),
        (Join-Path $projectRoot "dist\ControlPlane\EnterpriseRemote.ControlPlane.exe")
    )
    
    $cpExe = $null
    foreach ($p in $cpExeCandidates) {
        if ($p -and (Test-Path $p)) { $cpExe = $p; break }
    }
    
    if ($cpExe) {
        Start-Process -FilePath $cpExe -ArgumentList "--urls=http://0.0.0.0:$TargetPort" -WindowStyle Hidden
        Start-Sleep -Seconds 2
        Write-Host "[+] Control Plane started on http://127.0.0.1:$TargetPort" -ForegroundColor Green
    } else {
        Write-Host "[!] Could not locate EnterpriseRemote.ControlPlane.exe. Please start START_MAIN_PC.vbs first." -ForegroundColor Red
    }
} else {
    Write-Host "[+] Control Plane is active and listening on port $TargetPort." -ForegroundColor Green
}

# 3. Launch cloudflared tunnel
Write-Host ""
Get-Process -Name "cloudflared" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
Write-Host "[*] Creating secure public edge tunnel to http://127.0.0.1:$TargetPort..." -ForegroundColor Cyan
Write-Host "    Please wait 2-4 seconds for the public URL to be assigned..." -ForegroundColor Gray
Write-Host ""

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $cfExe
$psi.Arguments = "tunnel --protocol auto --edge-ip-version 4 --post-quantum=false --compression-quality 0 --url http://127.0.0.1:$TargetPort"
$psi.RedirectStandardError = $true
$psi.RedirectStandardOutput = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true

$proc = [System.Diagnostics.Process]::Start($psi)
$tunnelUrl = $null
$logFile = Join-Path $projectRoot "tunnel.log"
$publicUrlFile = Join-Path $projectRoot "public_tunnel_url.txt"
$mainAppUrlFile = Join-Path $projectRoot "EnterpriseRemote_Apps\1_MAIN_PC_CONTROLLER\public_tunnel_url.txt"
$localUrlFile = Join-Path $PSScriptRoot "public_tunnel_url.txt"

# Process stderr in real-time to extract the trycloudflare URL
$sb = New-Object System.Text.StringBuilder
while (-not $proc.HasExited -and -not $tunnelUrl) {
    $line = $proc.StandardError.ReadLine()
    if ($line) {
        [void]$sb.AppendLine($line)
        if ($line -match "(https:\/\/[a-zA-Z0-9-]+\.trycloudflare\.com)") {
            $tunnelUrl = $matches[1]
            break
        }
    }
}

if ($tunnelUrl) {
    # Save URL to all accessible target locations
    if (Test-Path (Split-Path $publicUrlFile)) { Set-Content -Path $publicUrlFile -Value $tunnelUrl -Force }
    if (Test-Path (Split-Path $mainAppUrlFile)) { Set-Content -Path $mainAppUrlFile -Value $tunnelUrl -Force }
    if (Test-Path (Split-Path $localUrlFile)) { Set-Content -Path $localUrlFile -Value $tunnelUrl -Force }
    
    # Copy to clipboard
    try { Set-Clipboard -Value $tunnelUrl } catch { }

    Write-Host "================================================================================" -ForegroundColor Green
    Write-Host "   PUBLIC GATEWAY URL ASSIGNED                                                  " -ForegroundColor White
    Write-Host "================================================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "   Public Gateway URL:  " -NoNewline -ForegroundColor White
    Write-Host "$tunnelUrl" -ForegroundColor Cyan -NoNewline
    Write-Host "   [COPIED TO CLIPBOARD]" -ForegroundColor Green
    Write-Host ""
    Write-Host "   HOW TO USE FROM ANY OTHER NETWORK (HOTSPOT, 4G/5G, DIFFERENT WI-FI):" -ForegroundColor Yellow
    Write-Host "   1. On your Remote PC (Remote Laptop):" -ForegroundColor White
    Write-Host "      Run '1_CLICK_ENROLL_REMOTE_PC.bat' and paste the URL above." -ForegroundColor Gray
    Write-Host "   2. On your Main PC (This Computer):" -ForegroundColor White
    Write-Host "      Keep Gateway URL as http://127.0.0.1:5293 (or paste $tunnelUrl)." -ForegroundColor Gray
    Write-Host "   3. Click 'Login' -> Select Remote PC -> Click 'START REMOTE SESSION'!" -ForegroundColor White
    Write-Host ""
    Write-Host "================================================================================" -ForegroundColor Green
    Write-Host "   Tunnel process is running. Keep this window open while streaming remotely.   " -ForegroundColor Gray
    Write-Host "   Press Ctrl+C at any time to close the tunnel.                               " -ForegroundColor Gray
    Write-Host "================================================================================" -ForegroundColor Green
    Write-Host ""

    # Stream remaining log asynchronously to prevent buffer deadlock
    [void][System.Threading.Tasks.Task]::Run([Action]{
        while (-not $proc.HasExited) {
            $l = $proc.StandardError.ReadLine()
            if ($l) {
                # Suppress verbose keep-alive pings
                if ($l -notmatch "connection.*ping|quic") {
                    # Write-Host $l -ForegroundColor DarkGray
                }
            }
        }
    })

    # Wait for process exit or user break
    try {
        $proc.WaitForExit()
    } finally {
        if (Test-Path $publicUrlFile) { Remove-Item $publicUrlFile -Force -ErrorAction SilentlyContinue }
        if (Test-Path $mainAppUrlFile) { Remove-Item $mainAppUrlFile -Force -ErrorAction SilentlyContinue }
        if (Test-Path $localUrlFile) { Remove-Item $localUrlFile -Force -ErrorAction SilentlyContinue }
    }
} else {
    Write-Host "[!] Could not extract public tunnel URL. Check Cloudflare log:" -ForegroundColor Red
    Write-Host $sb.ToString() -ForegroundColor Yellow
    $proc.Kill()
}
