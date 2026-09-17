# Build a per-user Inno Setup installer from a published CursorRemain.exe.
# ASCII-only so Windows PowerShell can parse this file without a UTF-8 BOM.
param(
    [string]$PublishDir = "",
    [string]$OutputDir = "",
    [string]$SourceRevisionId = "",
    [string]$AppVersion = "2.0.0"
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $PublishDir) { $PublishDir = Join-Path $RepoRoot "dist" }
if (-not $OutputDir) { $OutputDir = $RepoRoot }
$PublishDir = (Resolve-Path $PublishDir).Path
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

$exe = Join-Path $PublishDir "CursorRemain.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Missing $exe. Publish the app into $PublishDir first."
}

function Find-ISCC {
    foreach ($c in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )) {
        if ($c -and (Test-Path -LiteralPath $c)) { return $c }
    }
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Install-InnoSetup {
    $setup = Join-Path $env:TEMP "innosetup-install.exe"
    $urls = @(
        "https://jrsoftware.org/download.php/is.exe",
        "https://www.jrsoftware.org/download.php/is.exe"
    )
    $downloaded = $false
    foreach ($url in $urls) {
        try {
            Invoke-WebRequest -Uri $url -OutFile $setup -UseBasicParsing
            $downloaded = $true
            break
        } catch {
            Write-Host "Failed to download Inno Setup from $url ($($_.Exception.Message))"
        }
    }
    if (-not $downloaded) {
        if (Get-Command choco -ErrorAction SilentlyContinue) {
            choco install innosetup --no-progress -y
            return
        }
        throw "Could not download or install Inno Setup"
    }
    $proc = Start-Process -FilePath $setup -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-" -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        throw "Inno Setup installer exited with code $($proc.ExitCode)"
    }
}

$iscc = Find-ISCC
if (-not $iscc) {
    Install-InnoSetup
    $iscc = Find-ISCC
}
if (-not $iscc) {
    throw "ISCC.exe not found. Install Inno Setup from https://jrsoftware.org/isinfo.php"
}

$iss = Join-Path $PSScriptRoot "setup.iss"
$defines = @(
    "/DAppPublishDir=$PublishDir",
    "/DRepoRoot=$RepoRoot",
    "/DOutputDir=$OutputDir",
    "/DAppVersion=$AppVersion"
)
if ($SourceRevisionId) {
    $defines += "/DSourceRevisionId=$SourceRevisionId"
}

Write-Host "ISCC $iscc"
Write-Host ($defines -join " ")
& $iscc @defines $iss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compile failed: $LASTEXITCODE"
}

$out = Join-Path $OutputDir "CursorRemain-windows-setup.exe"
if (-not (Test-Path -LiteralPath $out)) {
    throw "Installer was not created: $out"
}
$item = Get-Item -LiteralPath $out
if ($item.Length -lt 100KB) {
    throw "Installer is unexpectedly small: $($item.Length) bytes"
}
if ($item.Length -gt 30MB) {
    throw ("Installer is unexpectedly large: {0:N2} MB" -f ($item.Length / 1MB))
}
"{0} {1:N2} MB" -f $item.Name, ($item.Length / 1MB)
