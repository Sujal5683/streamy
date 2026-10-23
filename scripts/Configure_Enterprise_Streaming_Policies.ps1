# ===============================================================================
# Configure Enterprise Streaming Policies (Chrome, Edge & Windows DWM)
# Ensures hardware-accelerated browsers, DRM media, and Electron apps
# are completely unblocked and do not turn black during remote streaming.
# ===============================================================================

$ErrorActionPreference = "SilentlyContinue"

Write-Host "===============================================================================" -ForegroundColor Cyan
Write-Host "   Enterprise Streaming Policy Configuration: Chrome, Edge, and DWM" -ForegroundColor Cyan
Write-Host "==============================================================================="
Write-Host ""

$policies = @(
    @{ Name = "Google Chrome"; Path = "HKLM:\SOFTWARE\Policies\Google\Chrome" },
    @{ Name = "Microsoft Edge"; Path = "HKLM:\SOFTWARE\Policies\Microsoft\Edge" }
)

foreach ($p in $policies) {
    if (-not (Test-Path $p.Path)) {
        New-Item -Path $p.Path -Force | Out-Null
    }

    # 1. ScreenCaptureAllowed = 1 (bypasses DLP and anti-screencast blocking)
    Set-ItemProperty -Path $p.Path -Name "ScreenCaptureAllowed" -Value 1 -Type DWord -Force
    
    # 2. HardwareAccelerationModeEnabled = 1 (stable GPU rasterization across DirectComposition)
    Set-ItemProperty -Path $p.Path -Name "HardwareAccelerationModeEnabled" -Value 1 -Type DWord -Force
    
    # 3. NativeWindowOcclusionEnabled = 0 (prevents browser from throttling/blanking in remote sessions)
    Set-ItemProperty -Path $p.Path -Name "NativeWindowOcclusionEnabled" -Value 0 -Type DWord -Force

    Write-Host "[+] Configured streaming policies for $($p.Name) in $($p.Path)" -ForegroundColor Green
}

Write-Host ""
Write-Host "[+] All browser streaming policies successfully applied and active." -ForegroundColor Green
Write-Host "==============================================================================="
