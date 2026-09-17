# 把已发布的 CursorRemain.exe 打成用户级 Inno Setup 安装包。
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
    throw "找不到 $exe，请先 dotnet publish 到 $PublishDir"
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
            Write-Host "下载 Inno Setup 失败: $url ($($_.Exception.Message))"
        }
    }
    if (-not $downloaded) {
        if (Get-Command choco -ErrorAction SilentlyContinue) {
            choco install innosetup --no-progress -y
            return
        }
        throw "无法下载或安装 Inno Setup"
    }
    $proc = Start-Process -FilePath $setup -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-" -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        throw "Inno Setup 安装失败，退出码 $($proc.ExitCode)"
    }
}

$iscc = Find-ISCC
if (-not $iscc) {
    Install-InnoSetup
    $iscc = Find-ISCC
}
if (-not $iscc) {
    throw "未找到 Inno Setup 编译器 ISCC.exe。请安装 https://jrsoftware.org/isinfo.php"
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
    throw "Inno Setup 编译失败: $LASTEXITCODE"
}

$out = Join-Path $OutputDir "CursorRemain-windows-setup.exe"
if (-not (Test-Path -LiteralPath $out)) {
    throw "未生成 $out"
}
$item = Get-Item -LiteralPath $out
if ($item.Length -lt 100KB) {
    throw "安装包过小: $($item.Length) bytes"
}
if ($item.Length -gt 30MB) {
    throw ("安装包过大: {0:N2} MB" -f ($item.Length / 1MB))
}
"{0} {1:N2} MB" -f $item.Name, ($item.Length / 1MB)
