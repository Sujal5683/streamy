<#
.SYNOPSIS
    Automated version bump, commit, tag, and push script for Streamy.
.DESCRIPTION
    Ensures that every release and update is pushed to GitHub strictly following the 
    official convention:
      Commit Message : "Streamy V<Major>.<Minor>.<Patch>"
      Git Tag        : "Streamy-V<Major>.<Minor>.<Patch>"
.PARAMETER Version
    The semver string, e.g. "1.0.0", "1.0.1", "1.1.0", or "2.0.0".
.PARAMETER Notes
    Optional release notes or changelog message.
.EXAMPLE
    .\scripts\push_streamy_version.ps1 -Version "1.0.1"
    .\scripts\push_streamy_version.ps1 -Version "1.1.0" -Notes "Multi-monitor hotkeys added"
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$Version = "",

    [Parameter(Mandatory=$false)]
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"

# 1. Resolve repository root
$repoRoot = (git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) {
    Write-Error "Not inside a git repository!"
    exit 1
}
Set-Location $repoRoot

# 2. Prompt for version if not supplied
if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "Current git status:" -ForegroundColor Cyan
    git status -s
    Write-Host ""
    $Version = Read-Host "Enter Streamy version to push (e.g. 1.0.1)"
}

# 3. Clean and normalize version format (e.g. "v1.0.0" -> "1.0.0")
$normalizedVersion = $Version.Trim()
if ($normalizedVersion.StartsWith("Streamy", [System.StringComparison]::OrdinalIgnoreCase)) {
    $normalizedVersion = $normalizedVersion -replace "(?i)^streamy\s*v?", ""
}
if ($normalizedVersion.StartsWith("V", [System.StringComparison]::OrdinalIgnoreCase)) {
    $normalizedVersion = $normalizedVersion.Substring(1)
}
$normalizedVersion = $normalizedVersion.Trim()

if (-not ($normalizedVersion -match '^\d+(\.\d+)+')) {
    Write-Error "Invalid version format '$Version'. Please supply semver like 1.0.0 or 1.0.1"
    exit 1
}

$commitMessage = "Streamy V$normalizedVersion"
$tagStreamy = "Streamy-V$normalizedVersion"
$tagSemver  = "v$normalizedVersion"

Write-Host "`n=======================================================" -ForegroundColor Cyan
Write-Host " Streamy Automated Release & Git Push" -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "Target Version : $normalizedVersion" -ForegroundColor Yellow
Write-Host "Commit Message : $commitMessage" -ForegroundColor Yellow
Write-Host "Tags Created   : $tagStreamy, $tagSemver" -ForegroundColor Yellow
Write-Host "Remote URL     : $(git remote get-url origin)" -ForegroundColor Yellow
Write-Host "=======================================================`n" -ForegroundColor Cyan

# 4. Stage files
Write-Host "[1/4] Staging modified and new files..." -ForegroundColor Gray
git add -A

# Check if there are changes to commit
$staged = git diff --cached --name-only
if (-not $staged) {
    Write-Host "Notice: No new file modifications detected in working tree." -ForegroundColor Yellow
    $ans = Read-Host "Would you like to amend the current HEAD or create an empty release commit? (amend/empty/cancel)"
    if ($ans -eq "amend") {
        git commit --amend -m $commitMessage
    } elseif ($ans -eq "empty") {
        git commit --allow-empty -m $commitMessage
    } else {
        Write-Host "Push cancelled." -ForegroundColor Yellow
        exit 0
    }
} else {
    Write-Host "[2/4] Committing staged files with convention: '$commitMessage'..." -ForegroundColor Gray
    if (-not [string]::IsNullOrWhiteSpace($Notes)) {
        git commit -m $commitMessage -m $Notes
    } else {
        git commit -m $commitMessage
    }
}

# 5. Tag release
Write-Host "[3/4] Creating release tags: $tagStreamy & $tagSemver..." -ForegroundColor Gray
# Remove local tag if re-tagging
git tag -d $tagStreamy 2>$null
git tag -d $tagSemver 2>$null
git tag -a $tagStreamy -m "Streamy Release V$normalizedVersion"
git tag -a $tagSemver -m "Streamy Release V$normalizedVersion"

# 6. Push to origin main with tags
Write-Host "[4/4] Pushing branch 'main' and tags to origin..." -ForegroundColor Cyan
git push origin main
git push origin --tags

Write-Host "`n=======================================================" -ForegroundColor Green
Write-Host " [SUCCESS] Streamy V$normalizedVersion successfully pushed to GitHub!" -ForegroundColor Green
Write-Host "=======================================================`n" -ForegroundColor Green
