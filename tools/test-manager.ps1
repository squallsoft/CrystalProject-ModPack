param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Crystal Project',
    [string]$PristineExecutable,
    [string]$FFmpeg,
    [string]$Sdk
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$Sdk) { $Sdk = Join-Path $repo 'artifacts\dotnet\dotnet.exe'; if (!(Test-Path -LiteralPath $Sdk)) { $Sdk = 'dotnet' } }
if (!$PristineExecutable) { $PristineExecutable = Join-Path $env:LOCALAPPDATA 'CrystalProjectModInstaller\backups\36f7d413160a4deee36b47fc6ac534e87cadb6f23f57337d4630ec99cedb14e6\Crystal Project.exe' }
if ((Get-FileHash -LiteralPath $PristineExecutable).Hash.ToLowerInvariant() -ne '36f7d413160a4deee36b47fc6ac534e87cadb6f23f57337d4630ec99cedb14e6') { throw 'Tests require the supported pristine executable.' }
if (!$FFmpeg) { $FFmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source }
if (!$FFmpeg) { throw 'Pass -FFmpeg with a developer FFmpeg executable to generate original test tones.' }
Push-Location $repo
try {
    New-Item -ItemType Directory -Path artifacts\baseline,artifacts\fixtures,artifacts\music-runtime-fixture -Force | Out-Null
    Copy-Item -LiteralPath $PristineExecutable -Destination 'artifacts\baseline\Crystal Project.exe'
    & $FFmpeg -hide_banner -loglevel error -f lavfi -i 'sine=frequency=440:duration=2' -c:a libvorbis -q:a 5 -y artifacts\fixtures\sine-a.ogg
    if ($LASTEXITCODE) { throw 'Fixture generation failed' }
    & $FFmpeg -hide_banner -loglevel error -f lavfi -i 'sine=frequency=660:duration=3' -metadata LOOPSTART=0.5 -metadata LOOPEND=2.5 -c:a libvorbis -q:a 5 -y artifacts\fixtures\sine-b.ogg
    if ($LASTEXITCODE) { throw 'Fixture generation failed' }
    & $Sdk build src\CrystalProjectModManager -c Release
    if ($LASTEXITCODE) { throw 'Manager build failed' }
    & $Sdk run --project tests -c Release -- $repo
    if ($LASTEXITCODE) { throw 'Installer regression tests failed' }
    & $Sdk run --project tests -c Release -- $repo failure-extra
    if ($LASTEXITCODE) { throw 'Failure tests failed' }
    & $Sdk run --project tests\Manager -c Release -- $repo
    if ($LASTEXITCODE) { throw 'Manager tests failed' }
    & $Sdk build tests\Runtime -c Release "-p:GameDir=$GameDirectory"
    if ($LASTEXITCODE) { throw 'Home Point test build failed' }
    $fixture = Get-Content -LiteralPath artifacts\test-location.txt
    & tests\Runtime\bin\Release\net462\RuntimeTests.exe (Join-Path $fixture 'game\Crystal Project.exe') $GameDirectory (Join-Path $fixture 'game') | Tee-Object -FilePath artifacts\manager-home-tests.txt
    if ($LASTEXITCODE) { throw 'Home Point runtime tests failed' }
    & $Sdk build tests\MusicRuntime -c Release
    if ($LASTEXITCODE) { throw 'Music runtime test build failed' }
    Copy-Item tests\MusicRuntime\bin\Release\net462\MusicRuntimeTests.exe,src\Runtime\bin\Release\net462\CrystalProjectRandomMusic.dll artifacts\music-runtime-fixture
    & artifacts\music-runtime-fixture\MusicRuntimeTests.exe (Join-Path $fixture 'game\Crystal Project.exe') $GameDirectory (Join-Path $repo 'artifacts\fixtures\sine-a.ogg') (Join-Path $repo 'artifacts\fixtures\sine-b.ogg') | Tee-Object -FilePath artifacts\manager-music-tests.txt
    if ($LASTEXITCODE) { throw 'Music runtime tests failed' }
} finally { Pop-Location }
