# ================================================================================
# Enterprise Remote Access - Tailscale Remote PC Setup Helper
# ================================================================================

$ErrorActionPreference = "Continue"

Clear-Host
Write-Host ""
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "   ENTERPRISE REMOTE ACCESS - TAILSCALE REMOTE PC SETUP                        " -ForegroundColor White
Write-Host "   Check Tailscale Connectivity on THIS Remote PC                               " -ForegroundColor Gray
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""

$ip = $null

try {
    $tailscaleCandidates = @(
        "C:\Program Files\Tailscale\tailscale.exe",
        "C:\Program Files (x86)\Tailscale\tailscale.exe",
        (Get-Command "tailscale" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
    )
    $tsExe = $null
    foreach ($c in $tailscaleCandidates) {
        if ($c -and (Test-Path $c -ErrorAction SilentlyContinue)) { $tsExe = $c; break }
    }

    if ($tsExe) {
        $st = & $tsExe status --json 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($st -and $st.Self -and $st.Self.TailscaleIPs) {
            foreach ($a in $st.Self.TailscaleIPs) {
                if ($a.ToString().StartsWith("100.")) {
                    $ip = $a.ToString()
                    break
                }
            }
        }
    }
} catch { }

if (-not $ip) {
    try {
        $addrs = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue
        foreach ($a in $addrs) {
            if ($a.IPAddress -and $a.IPAddress.StartsWith("100.")) {
                $ip = $a.IPAddress
                break
            }
        }
    } catch { }
}

if ($ip) {
    Write-Host "  [OK] Tailscale is CONNECTED on this Remote PC!" -ForegroundColor Green
    Write-Host ""
    Write-Host "  This Remote PC Tailscale IP:  " -NoNewline -ForegroundColor White
    Write-Host "$ip" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  NEXT STEPS:" -ForegroundColor Yellow
    Write-Host "  1. On Main PC: Run START_TAILSCALE_MODE.bat to get the Main PC Tailscale IP." -ForegroundColor White
    Write-Host "  2. On this Remote PC: Run 1_CLICK_ENROLL_REMOTE_PC.bat" -ForegroundColor White
    Write-Host "     When asked for Server URL, enter:" -ForegroundColor White
    Write-Host "     http://<Main-PC-Tailscale-IP>:5293" -ForegroundColor Cyan
    Write-Host ""
} else {
    Write-Host "  [!] Tailscale is NOT connected on this Remote PC." -ForegroundColor Red
    Write-Host ""
    Write-Host "  To connect:" -ForegroundColor Yellow
    Write-Host "  1. Install Tailscale: https://tailscale.com/download" -ForegroundColor White
    Write-Host "  2. Sign in with the SAME account as your Main PC" -ForegroundColor White
    Write-Host "  3. Re-run this script to confirm connection" -ForegroundColor White
    Write-Host ""
    Write-Host "  Opening download page..." -ForegroundColor Cyan
    try { Start-Process "https://tailscale.com/download" } catch { }
}

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host ""
try { $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") } catch { pause }
