$base = $PSScriptRoot | Split-Path -Parent
$exe = Join-Path $base "dist\Controller\EnterpriseRemote.Controller.exe"
$workDir = Join-Path $base "dist\Controller"

if (-not (Test-Path $exe)) {
    Write-Host "[!] Executable not found at $exe" -ForegroundColor Red
    exit 1
}

Start-Process -FilePath $exe -WorkingDirectory $workDir
Write-Host "[+] Successfully launched Controller GUI on desktop!" -ForegroundColor Green
