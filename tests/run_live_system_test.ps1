# Enterprise Remote Access: Live End-to-End System Test Runner
$ErrorActionPreference = "Stop"

Write-Host "===============================================================================" -ForegroundColor Cyan
Write-Host "   Enterprise Windows Remote Access: Live System End-to-End Verification" -ForegroundColor Cyan
Write-Host "===============================================================================" -ForegroundColor Cyan

$baseDir = "C:\My Projects\SECURE AND ENTERPRISE STREAMING APP"
$serverUrl = "http://127.0.0.1:5293"
$controlPlaneDll = "$baseDir\src\EnterpriseRemote.ControlPlane\bin\Debug\net8.0\EnterpriseRemote.ControlPlane.dll"
$hostServiceExe = "$baseDir\src\EnterpriseRemote.Host.Service\bin\Debug\net8.0-windows\EnterpriseRemote.Host.Service.exe"
$userAgentExe = "$baseDir\src\EnterpriseRemote.Host.UserAgent\bin\Debug\net8.0-windows\EnterpriseRemote.Host.UserAgent.exe"
$controllerExe = "$baseDir\src\EnterpriseRemote.Controller\bin\Debug\net8.0-windows\EnterpriseRemote.Controller.exe"

# 1. Clean up any previous test state
Write-Host "`n[Step 1] Cleaning up temporary test state..." -ForegroundColor Yellow
Remove-Item -Force -ErrorAction SilentlyContinue "device_identity.json"
Remove-Item -Force -ErrorAction SilentlyContinue "$env:ProgramData\EnterpriseRemote\device_identity.json"

# 2. Start Control Plane in background process
Write-Host "`n[Step 2] Launching Enterprise Control Plane on $serverUrl..." -ForegroundColor Yellow
$serverProc = Start-Process dotnet -ArgumentList "`"$controlPlaneDll`" --urls=$serverUrl" -PassThru -NoNewWindow
Start-Sleep -Seconds 3

try {
    # 3. Verify Health Endpoint
    Write-Host "`n[Step 3] Checking Control Plane health endpoint..." -ForegroundColor Yellow
    $health = Invoke-RestMethod -Uri "$serverUrl/health" -Method Get
    Write-Host "[+] Control Plane is HEALTHY! Status: $($health.status)" -ForegroundColor Green

    # 4. Admin Generates Invitation Token
    Write-Host "`n[Step 4] Requesting Single-Use Enrollment Invitation (Admin)..." -ForegroundColor Yellow
    $invReq = @{
        AdminUserId = "USR-ADMIN-01"
        GroupId = "GRP-CORP-DESKTOPS"
        TtlMinutes = 15
        AttendedRequired = $false
    } | ConvertTo-Json

    $invResp = Invoke-RestMethod -Uri "$serverUrl/api/v1/invitations/create" -Method Post -Body $invReq -ContentType "application/json"
    $invCode = $invResp.invitationCode
    Write-Host "[+] Generated Invitation Code: $invCode (Expires: $($invResp.expiresAtUtc))" -ForegroundColor Green

    # 5. Enroll Endpoint using Host Service CLI
    Write-Host "`n[Step 5] Enrolling local Windows endpoint via Host Service..." -ForegroundColor Yellow
    & $hostServiceExe --enroll $invCode --server $serverUrl
    if ($LASTEXITCODE -ne 0) {
        throw "Host Service enrollment failed with exit code $LASTEXITCODE"
    }

    # 6. Verify Device Identity Created
    $progDataIdent = "$env:ProgramData\EnterpriseRemote\device_identity.json"
    $identFile = if (Test-Path "device_identity.json") { "device_identity.json" } elseif (Test-Path $progDataIdent) { $progDataIdent } else { $null }
    if (-not $identFile) {
        throw "device_identity.json was not created after enrollment."
    }
    $ident = Get-Content $identFile | ConvertFrom-Json
    $enrolledDeviceId = $ident.DeviceId
    Write-Host "[+] Verified Enrolled Device on Disk: $enrolledDeviceId ($($ident.Hostname))" -ForegroundColor Green

    # 7. Test User Session Agent Execution (One-shot display capture & input sink check)
    Write-Host "`n[Step 6] Verifying Interactive Desktop Agent (DXGI / GDI)..." -ForegroundColor Yellow
    & $userAgentExe --oneshot
    if ($LASTEXITCODE -ne 0) {
        throw "User Agent execution failed with exit code $LASTEXITCODE"
    }
    Write-Host "[+] Interactive User Agent verified successfully." -ForegroundColor Green

    # 8. Run Operator Controller Application (Workflow A)
    Write-Host "`n[Step 7] Launching Controller Application to Connect to Enrolled Endpoint..." -ForegroundColor Yellow
    & $controllerExe --server $serverUrl --email "alice@corp.internal" --device $enrolledDeviceId
    if ($LASTEXITCODE -ne 0) {
        throw "Controller execution failed with exit code $LASTEXITCODE"
    }
    Write-Host "[+] Remote Desktop session connected, streamed, and terminated cleanly." -ForegroundColor Green

    # 9. Verify Audit Trail in Control Plane
    Write-Host "`n[Step 8] Verifying SIEM-Compliant Audit Trail in Control Plane..." -ForegroundColor Yellow
    $audits = Invoke-RestMethod -Uri "$serverUrl/api/v1/audit/events?userId=USR-ADMIN-01" -Method Get
    Write-Host "[+] Found $($audits.Count) audit event(s):" -ForegroundColor Green
    foreach ($a in $audits) {
        Write-Host "    -> [$($a.timestampUtc)] Event: $($a.eventType) | Operator: $($a.operatorId) | Device: $($a.deviceId) | Details: $($a.details)" -ForegroundColor Gray
    }

    # Verify key audit milestones were captured
    $hasEnroll = $audits | Where-Object { $_.eventType -eq "DeviceEnrolled" }
    $hasSession = $audits | Where-Object { $_.eventType -eq "SessionAuthorized" }

    if (-not $hasEnroll -or -not $hasSession) {
        throw "Audit log is missing required security events."
    }

    Write-Host "`n===============================================================================" -ForegroundColor Green
    Write-Host "   LIVE END-TO-END SYSTEM TEST PASSED WITH ZERO ERRORS!" -ForegroundColor Green
    Write-Host "===============================================================================" -ForegroundColor Green

} finally {
    # 10. Clean up background Control Plane process
    Write-Host "`n[*] Stopping background Control Plane process..." -ForegroundColor Gray
    if ($serverProc -and -not $serverProc.HasExited) {
        Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue
    }
}
