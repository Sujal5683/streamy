$serverUrl = "http://127.0.0.1:5293"
$hostServiceExe = "dist\HostService\EnterpriseRemote.Host.Service.exe"

$invBody = @{
    AdminUserId = "USR-ADMIN-01"
    GroupId = "GRP-CORP-DESKTOPS"
    TtlMinutes = 15
    AttendedRequired = $false
} | ConvertTo-Json

$invResp = Invoke-RestMethod -Uri "$serverUrl/api/v1/invitations/create" -Method Post -Body $invBody -ContentType "application/json"
$code = $invResp.invitationCode
Write-Host "[+] Created Invitation: $code" -ForegroundColor Green

& $hostServiceExe --enroll $code --server $serverUrl
