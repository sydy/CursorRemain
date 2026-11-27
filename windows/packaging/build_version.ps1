# Release version = commit timestamp in Asia/Shanghai (YYYY.MM.DD.HHMM).
# Usage:
#   powershell -File build_version.ps1                      # print the stamp
#   powershell -File build_version.ps1 -OutputFile x.g.cs   # also emit assembly metadata
param(
    [string]$OutputFile = ""
)

$ErrorActionPreference = "SilentlyContinue"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

$stamp = ""
try {
    $iso = (& git -C $RepoRoot log -1 --date=iso-strict --format="%cd") | Select-Object -First 1
    if ($iso) {
        $date = [DateTimeOffset]::Parse($iso.Trim(), [System.Globalization.CultureInfo]::InvariantCulture)
        $beijing = $date.ToOffset([TimeSpan]::FromHours(8))
        $stamp = $beijing.ToString("yyyy.MM.dd.HHmm", [System.Globalization.CultureInfo]::InvariantCulture)
    }
} catch {
    $stamp = ""
}

if (-not $stamp) {
    # No git history: fall back to VERSION so a build never ships without a version.
    $versionFile = Join-Path $RepoRoot "VERSION"
    if (Test-Path -LiteralPath $versionFile) {
        $stamp = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    }
}
if (-not $stamp) { $stamp = "0.0.0" }

if ($OutputFile) {
    $line = 'using System.Reflection;' + [Environment]::NewLine + '[assembly: AssemblyMetadata("BuildVersion", "' + $stamp + '")]'
    $dir = Split-Path -Parent $OutputFile
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    [System.IO.File]::WriteAllText($OutputFile, $line, (New-Object System.Text.UTF8Encoding($false)))
}

Write-Output $stamp
