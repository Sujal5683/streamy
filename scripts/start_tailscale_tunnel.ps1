# ================================================================================
# Enterprise Remote Access - Tailscale Low-Latency Mode Setup Helper
# ================================================================================
# Checks Tailscale status on this Main PC, shows the Tailscale IP address to use
# on the Remote PC, and verifies the Control Plane is reachable over the tailnet.
#
# WHY TAILSCALE?
#   - Uses WireGuard UDP (not TCP) - eliminates TCP-over-TCP latency spikes
#   - Direct peer-to-peer connection between your two PCs (no cloud relay hop)
#   - Near-LAN latency even over mobile data or across the world
#   - Free forever: 3 users, 100 devices, unlimited bandwidth
#   - Compare: Cloudflare Quick Tunnel = TCP relay via US/EU PoP (+50-200ms RTT)
#              Tailscale = WireGuard UDP direct P2P (<5ms overhead)
# ================================================================================

param(
    [int]$ControlPlanePort = 5293
)

$ErrorActionPreference = "Continue"

Clear-Host
Write-Host ""
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "   ENTERPRISE REMOTE ACCESS - TAILSCALE LOW-LATENCY MODE                       " -ForegroundColor White
Write-Host "   Ultra-Low WAN Latency via WireGuard UDP (Direct Peer-to-Peer)               " -ForegroundColor Gray
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check if Tailscale is installed
$tailscaleCandidates = @(
    "C:\Program Files\Tailscale\tailscale.exe",
    "C:\Program Files (x86)\Tailscale\tailscale.exe",
    (Get-Command "tailscale" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
)

$tailscaleExe = $null
foreach ($c in $tailscaleCandidates) {
    if ($c -and (Test-Path $c -ErrorAction SilentlyContinue)) {
        $tailscaleExe = $c
        break
    }
}

if (-not $tailscaleExe) {
    Write-Host "  [!] Tailscale is NOT installed on this PC." -ForegroundColor Red
    Write-Host "" 
    Write-Host "  +---------------------------------------------------------------------+" -ForegroundColor Yellow
    Write-Host "  |  HOW TO INSTALL TAILSCALE (Free, 2 minutes):                        |" -ForegroundColor Yellow
    Write-Host "  |                                                                     |" -ForegroundColor Yellow
    Write-Host "  |  1. Go to:  https://tailscale.com/download                          |" -ForegroundColor Yellow
    Write-Host "  |  2. Download and install Tailscale for Windows (free account)       |" -ForegroundColor Yellow
    Write-Host "  |  3. Sign in with Google, Microsoft, or GitHub                       |" -ForegroundColor Yellow
    Write-Host "  |  4. Do the SAME on your Remote PC                                   |" -ForegroundColor Yellow
    Write-Host "  |  5. Run this script again - it will show your Tailscale IP          |" -ForegroundColor Yellow
    Write-Host "  +---------------------------------------------------------------------+" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Opening Tailscale download page in your browser..." -ForegroundColor Cyan
    try { Start-Process "https://tailscale.com/download" } catch { }
    Write-Host ""
    Write-Host "  After installing on BOTH PCs, re-run this script." -ForegroundColor White
    Write-Host ""
    pause
    exit 0
}

Write-Host "  [+] Tailscale found: $tailscaleExe" -ForegroundColor Green
Write-Host ""

# Step 2: Get Tailscale status and IP
$tailscaleIp = $null
$tailscaleName = $null
$tailscaleStatus = "unknown"

try {
    $statusOutput = & $tailscaleExe status --json 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
    if ($statusOutput) {
        $tailscaleStatus = $statusOutput.BackendState
        if ($statusOutput.Self) {
            $tailscaleName = $statusOutput.Self.HostName
            if ($statusOutput.Self.TailscaleIPs -and $statusOutput.Self.TailscaleIPs.Count -gt 0) {
                foreach ($ip in $statusOutput.Self.TailscaleIPs) {
                    if ($ip.ToString().StartsWith("100.")) {
                        $tailscaleIp = $ip.ToString()
                        break
                    }
                }
            }
        }
    }
} catch { }

# Fallback: scan network adapters for 100.x.x.x
if (-not $tailscaleIp) {
    try {
        $adapters = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue
        foreach ($a in $adapters) {
            if ($a.IPAddress -and $a.IPAddress.StartsWith("100.")) {
                $tailscaleIp = $a.IPAddress
                break
            }
        }
    } catch { }
}

if (-not $tailscaleIp) {
    Write-Host "  [!] Tailscale is installed but NOT connected / not signed in." -ForegroundColor Red
    Write-Host ""
    Write-Host "  To connect:" -ForegroundColor Yellow
    Write-Host "    1. Look for the Tailscale icon in your System Tray (bottom-right notification area)" -ForegroundColor White
    Write-Host "    2. Click it and sign in with your account" -ForegroundColor White
    Write-Host "    3. Make sure 'Connected' appears in the tray menu" -ForegroundColor White
    Write-Host "    4. Re-run this script" -ForegroundColor White
    Write-Host ""
    Write-Host "  Or run in command prompt:  tailscale up" -ForegroundColor Cyan
    Write-Host ""
    pause
    exit 0
}

# Step 3: Check if Control Plane is running
$cpRunning = $false
try {
    $client = New-Object System.Net.Sockets.TcpClient
    $iar = $client.BeginConnect("127.0.0.1", $ControlPlanePort, $null, $null)
    if ($iar.AsyncWaitHandle.WaitOne(800, $false)) {
        try { $client.EndConnect($iar); $cpRunning = $true } catch { }
    }
    try { $client.Close() } catch { }
} catch { }

# Step 4: Save Tailscale IP to file
$projectRoot = Split-Path -Parent $PSScriptRoot
$tailscaleIpFile = Join-Path $projectRoot "tailscale_ip.txt"
$appTailscaleIpFile = Join-Path $projectRoot "EnterpriseRemote_Apps\1_MAIN_PC_CONTROLLER\tailscale_ip.txt"

try { Set-Content -Path $tailscaleIpFile -Value $tailscaleIp -Force } catch { }
try {
    $appDir = Split-Path $appTailscaleIpFile
    if (Test-Path $appDir) { Set-Content -Path $appTailscaleIpFile -Value $tailscaleIp -Force }
} catch { }

# Copy to clipboard
try { Set-Clipboard -Value "http://${tailscaleIp}:${ControlPlanePort}" } catch { }

# Step 5: Display results
Write-Host "================================================================================" -ForegroundColor Green
Write-Host "   TAILSCALE CONNECTED - LOW-LATENCY MODE READY                                " -ForegroundColor White
Write-Host "================================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "   This PC Tailscale IP:      " -NoNewline -ForegroundColor White
Write-Host "$tailscaleIp" -ForegroundColor Cyan -NoNewline
if ($tailscaleName) { Write-Host "  ($tailscaleName)" -ForegroundColor DarkCyan } else { Write-Host "" }
Write-Host ""
Write-Host "   Control Plane URL:         " -NoNewline -ForegroundColor White
Write-Host "http://${tailscaleIp}:${ControlPlanePort}" -ForegroundColor Green -NoNewline
Write-Host "  [COPIED TO CLIPBOARD]" -ForegroundColor DarkGreen
Write-Host ""

if ($cpRunning) {
    Write-Host "   Control Plane Status:      " -NoNewline -ForegroundColor White
    Write-Host "RUNNING on port $ControlPlanePort" -ForegroundColor Green
} else {
    Write-Host "   Control Plane Status:      " -NoNewline -ForegroundColor White
    Write-Host "NOT RUNNING - start it first with START_MAIN_PC.vbs or START_MAIN_PC.bat" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "   SETUP INSTRUCTIONS FOR YOUR REMOTE PC                                       " -ForegroundColor White
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "   STEP 1 - On Remote PC: Install Tailscale and sign in to the SAME account"   -ForegroundColor Yellow
Write-Host "            Download: https://tailscale.com/download"                           -ForegroundColor Gray
Write-Host ""
Write-Host "   STEP 2 - On Remote PC: Run SETUP_TAILSCALE_REMOTE.bat"                      -ForegroundColor Yellow
Write-Host "            (In the 2_REMOTE_PC_STREAMER folder)"                               -ForegroundColor Gray
Write-Host "            It will confirm connectivity and show your Remote PC Tailscale IP." -ForegroundColor Gray
Write-Host ""
Write-Host "   STEP 3 - On Remote PC: Run 1_CLICK_ENROLL_REMOTE_PC.bat"                    -ForegroundColor Yellow
Write-Host "            When asked for the server URL, enter:"                               -ForegroundColor Gray
Write-Host "            >> http://${tailscaleIp}:${ControlPlanePort} <<" -ForegroundColor Cyan
Write-Host "            (It is already in your clipboard!)"                                  -ForegroundColor Gray
Write-Host ""
Write-Host "   STEP 4 - Stream! Near-LAN latency over any network."                        -ForegroundColor Yellow
Write-Host ""
Write-Host "   NOTE: Both PCs MUST be signed in to Tailscale (same account or shared)"     -ForegroundColor DarkYellow
Write-Host "         Tailscale runs silently in the background - no window needed."         -ForegroundColor DarkYellow
Write-Host ""
Write-Host "   HOW IT COMPARES TO CLOUDFLARE TUNNEL:" -ForegroundColor White
Write-Host "   +---------------------+--------------------------+------------------------+" -ForegroundColor DarkCyan
Write-Host "   | Feature             | Cloudflare Quick Tunnel  | Tailscale (WireGuard)  |" -ForegroundColor DarkCyan
Write-Host "   +---------------------+--------------------------+------------------------+" -ForegroundColor DarkCyan
Write-Host "   | Protocol            | TCP (WebSocket relay)    | UDP (direct P2P)       |" -ForegroundColor White
Write-Host "   | Latency overhead    | 50-200ms extra RTT       | Under 5ms overhead     |" -ForegroundColor White
Write-Host "   | Bandwidth throttle  | Yes (free tier)          | None                   |" -ForegroundColor White
Write-Host "   | URL stability       | Changes every restart    | Permanent IP           |" -ForegroundColor White
Write-Host "   | Setup required      | None (runs anywhere)     | Install on both PCs    |" -ForegroundColor White
Write-Host "   | Free tier           | Yes (anonymous)          | Yes (3 users)          |" -ForegroundColor White
Write-Host "   +---------------------+--------------------------+------------------------+" -ForegroundColor DarkCyan
Write-Host ""
Write-Host "   Keep this window open (or close it - Tailscale runs in the background)."    -ForegroundColor Gray
Write-Host "   Press any key to exit."                                                      -ForegroundColor Gray
Write-Host ""
try { $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") } catch { pause }
