@echo off
title Enterprise Remote Access - Tailscale Remote PC Setup
cd /d "%~dp0"

echo.
echo  ================================================================
echo    ENTERPRISE REMOTE ACCESS - TAILSCALE REMOTE PC SETUP
echo    Check Tailscale connectivity from THIS Remote PC
echo  ================================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
"$ip = $null; ^
try { ^
    $tailscale = Get-Command tailscale -ErrorAction SilentlyContinue; ^
    if ($tailscale) { ^
        $st = & tailscale status --json 2>$null | ConvertFrom-Json; ^
        if ($st.Self.TailscaleIPs) { ^
            foreach ($a in $st.Self.TailscaleIPs) { if ($a -match '^100\.') { $ip = $a; break } } ^
        } ^
    } ^
} catch {} ^
if (-not $ip) { ^
    try { ^
        $addrs = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue; ^
        foreach ($a in $addrs) { ^
            if ($a.IPAddress -match '^100\.(6[4-9]|[7-9]\d|1[01]\d|12[0-7])\.') { $ip = $a.IPAddress; break } ^
        } ^
    } catch {} ^
} ^
if ($ip) { ^
    Write-Host '' -ForegroundColor Green; ^
    Write-Host '  [OK] Tailscale is CONNECTED on this Remote PC!' -ForegroundColor Green; ^
    Write-Host '' ; ^
    Write-Host '  This Remote PC Tailscale IP:  ' -NoNewline -ForegroundColor White; ^
    Write-Host $ip -ForegroundColor Cyan; ^
    Write-Host ''; ^
    Write-Host '  NEXT STEP:' -ForegroundColor Yellow; ^
    Write-Host '  Run 1_CLICK_ENROLL_REMOTE_PC.bat and enter the Main PC Tailscale IP' -ForegroundColor White; ^
    Write-Host '  (e.g.  http://100.x.x.x:5293 )  as the server URL.' -ForegroundColor White; ^
    Write-Host '  Get the Main PCs Tailscale IP by running START_TAILSCALE_MODE.bat there.' -ForegroundColor Gray; ^
} else { ^
    Write-Host '' -ForegroundColor Red; ^
    Write-Host '  [!] Tailscale is NOT connected on this Remote PC.' -ForegroundColor Red; ^
    Write-Host '' ; ^
    Write-Host '  Install Tailscale: https://tailscale.com/download' -ForegroundColor Yellow; ^
    Write-Host '  Sign in with the SAME account as the Main PC.' -ForegroundColor Yellow; ^
    Write-Host '  Then re-run this script.' -ForegroundColor Yellow; ^
    Write-Host '' ; ^
    Write-Host '  Opening download page...' -ForegroundColor Cyan; ^
    Start-Process 'https://tailscale.com/download'; ^
}"

echo.
echo  ================================================================
echo.
pause
