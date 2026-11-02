param(
    [string]$SourceDir = "C:\My Projects\SECURE AND ENTERPRISE STREAMING APP\EnterpriseRemote_Apps\2_REMOTE_PC_STREAMER",
    [string]$ZipPath = "C:\My Projects\SECURE AND ENTERPRISE STREAMING APP\EnterpriseRemote_Apps\2_REMOTE_PC_STREAMER.zip"
)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

$zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $files = Get-ChildItem -Path $SourceDir -Recurse -File
    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($SourceDir.Length).TrimStart('\', '/')
        $entryName = $relativePath.Replace('\', '/')
        
        $entry = $zip.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
        $entryStream = $entry.Open()
        try {
            $fileStream = [System.IO.File]::Open($file.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
            try {
                $fileStream.CopyTo($entryStream)
            } finally {
                $fileStream.Close()
            }
        } finally {
            $entryStream.Close()
        }
    }
    Write-Output "Zip successfully created at: $ZipPath"
} finally {
    $zip.Dispose()
}

$info = Get-Item $ZipPath
Write-Output "Size: $($info.Length) bytes, LastWriteTime: $($info.LastWriteTime)"
