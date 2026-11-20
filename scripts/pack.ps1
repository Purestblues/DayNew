param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Csproj   = Join-Path $RepoRoot 'src\DayNew.App\DayNew.App.csproj'
$Icon     = Join-Path $RepoRoot 'src\DayNew.App\Assets\DayNew.ico'
$PubDir   = Join-Path $RepoRoot 'artifacts\publish'
$RelDir   = Join-Path $RepoRoot 'releases\win-x64'

if (-not $Version) {
    [xml]$xml = Get-Content $Csproj -Encoding UTF8
    $Version = ($xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if (-not $Version) { throw "未在 $Csproj 中找到 <Version>。" }
}

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "未找到 vpk 命令，请先运行: dotnet tool install -g vpk"
}

Write-Host "==> 发布 DayNew v$Version (framework-dependent, win-x64)..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $PubDir | Out-Null

dotnet publish $Csproj `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:Version=$Version `
    -o $PubDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> 使用 Velopack 打包安装程序..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $RelDir | Out-Null

vpk pack `
    --packId 'DayNew.App' `
    --packVersion $Version `
    --packTitle '日新' `
    --packAuthors 'DayNew' `
    --runtime 'win-x64' `
    --packDir $PubDir `
    --mainExe 'DayNew.exe' `
    --icon $Icon `
    --framework 'net10.0-x64-desktop' `
    --shortcuts 'Desktop,StartMenuRoot' `
    --instLocation 'PerUser' `
    --outputDir $RelDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "`n打包完成，输出目录: $RelDir" -ForegroundColor Green
Get-ChildItem $RelDir | Select-Object Name, Length, LastWriteTime
