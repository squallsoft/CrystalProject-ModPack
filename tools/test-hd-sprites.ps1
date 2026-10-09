param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Crystal Project',
    [string]$PristineExecutable,
    [string]$PreviousRuntimeDirectory,
    [string]$SpriteArchive,
    [string]$Sdk
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$Sdk) { $Sdk = Join-Path $repo 'artifacts\dotnet\dotnet.exe'; if (!(Test-Path -LiteralPath $Sdk)) { $Sdk = 'dotnet' } }
if (!$PristineExecutable) { $PristineExecutable = Join-Path $repo 'artifacts\baseline\Crystal Project.exe' }
if ((Get-FileHash -LiteralPath $PristineExecutable).Hash.ToLowerInvariant() -ne '36f7d413160a4deee36b47fc6ac534e87cadb6f23f57337d4630ec99cedb14e6') { throw 'HD tests require the supported pristine executable.' }
$baseline = Join-Path $repo 'artifacts\baseline\Crystal Project.exe'
New-Item -ItemType Directory -Path (Split-Path $baseline -Parent) -Force | Out-Null
if ([System.IO.Path]::GetFullPath($PristineExecutable) -ne [System.IO.Path]::GetFullPath($baseline)) { Copy-Item -LiteralPath $PristineExecutable -Destination $baseline -Force }
Push-Location $repo
try {
    & $Sdk build src\CrystalProjectModManager -c Release
    if ($LASTEXITCODE) { throw 'Manager build failed' }
    $liveArchive = Join-Path $GameDirectory 'Content\Textures\Monster.dat'
    $liveArchiveHash = (Get-FileHash -LiteralPath $liveArchive).Hash
    if (!$SpriteArchive) { $SpriteArchive = $liveArchive }
    $testArguments = @($repo, $SpriteArchive)
    if ($PreviousRuntimeDirectory) { $testArguments += $PreviousRuntimeDirectory }
    & $Sdk run --project tests\HDSprites -c Release -- @testArguments
    if ($LASTEXITCODE) { throw 'HD integration tests failed' }
    & $Sdk build tests\HDRuntime -c Release
    if ($LASTEXITCODE) { throw 'HD runtime test build failed' }
    $runtimeFixture = Get-Content -LiteralPath artifacts\hd-runtime-location.txt
    & tests\HDRuntime\bin\Release\net462\HDRuntimeTests.exe $runtimeFixture $GameDirectory | Tee-Object -FilePath artifacts\hd-runtime-tests.txt
    if ($LASTEXITCODE) { throw 'HD runtime tests failed' }
    if ((Get-FileHash -LiteralPath $liveArchive).Hash -ne $liveArchiveHash) { throw 'Live game sprite archive changed during testing.' }
} finally { Pop-Location }
