$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sdk = Join-Path $repo 'artifacts\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
Push-Location $repo
try {
    & $sdk build src\Runtime\Music.csproj -c Release
    if ($LASTEXITCODE) { throw 'Music build failed' }
    & $sdk build src\Runtime\HomePoints.csproj -c Release
    if ($LASTEXITCODE) { throw 'Home Points build failed' }
    & $sdk publish src\Installer\Installer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\release\v0.1.0-rc1
    if ($LASTEXITCODE) { throw 'Installer publish failed' }
    Copy-Item README.md,CHANGELOG.md,NOTICE.md artifacts\release\v0.1.0-rc1
} finally { Pop-Location }
