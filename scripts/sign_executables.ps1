# Create or get code signing certificate
$cert = Get-ChildItem -Path "Cert:\CurrentUser\My" -CodeSigningCert | Where-Object { $_.Subject -like "*EnterpriseRemote*" } | Select-Object -First 1

if (-not $cert) {
    Write-Host "[*] Generating new enterprise Authenticode signing certificate..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=EnterpriseRemote Platform, O=Enterprise Remote Access Corp" -CertStoreLocation "Cert:\CurrentUser\My" -FriendlyName "EnterpriseRemote Platform Signing Certificate"
}

Write-Host "[+] Using Signing Certificate Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green

$exeFiles = Get-ChildItem -Path "dist" -Recurse -Filter "*.exe"

foreach ($file in $exeFiles) {
    Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $cert | Out-Null
    $sig = Get-AuthenticodeSignature -FilePath $file.FullName
    Write-Host "[+] Signed: $($file.Name) | Status: $($sig.Status) | Signer: $($sig.SignerCertificate.Subject)" -ForegroundColor Cyan
}

Write-Host "`n[+] All executables signed with Authenticode signatures!" -ForegroundColor Green
