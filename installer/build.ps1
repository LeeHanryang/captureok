# CaptureOK 설치 파일을 만든다: .NET 런타임을 함께 넣어 게시(self-contained)한 뒤 Inno Setup으로 묶는다.
# 결과: installer\output\CaptureOK-Setup-<버전>.exe
# 필요: .NET 10 SDK, Inno Setup 6 (winget install JRSoftware.InnoSetup)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $PSScriptRoot 'publish'

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props') -Encoding UTF8
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Directory.Build.props에서 버전을 찾지 못했습니다.' }

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6을 찾지 못했습니다: winget install JRSoftware.InnoSetup' }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\CaptureOk\CaptureOk.csproj') -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw "게시 실패 ($LASTEXITCODE)" }

& $iscc "/DAppVersion=$version" "/DPublishDir=$publish" (Join-Path $PSScriptRoot 'CaptureOk.iss')
if ($LASTEXITCODE -ne 0) { throw "설치 파일 만들기 실패 ($LASTEXITCODE)" }
Get-Item (Join-Path $PSScriptRoot "output\CaptureOK-Setup-$version.exe")
