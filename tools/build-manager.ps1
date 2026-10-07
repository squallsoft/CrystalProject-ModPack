param([string]$Sdk)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$Sdk) { $Sdk = Join-Path $repo 'artifacts\dotnet\dotnet.exe'; if (!(Test-Path -LiteralPath $Sdk)) { $Sdk = 'dotnet' } }
$output = Join-Path $repo 'artifacts\release\manager-0.1.0-rc1'
Push-Location $repo
try {
    & $Sdk publish src\CrystalProjectModManager -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
    if ($LASTEXITCODE) { throw 'Manager publish failed' }
    Copy-Item README.md,CHANGELOG.md,LICENSE,NOTICE.md $output
    foreach ($document in @('docs\MUSIC_CUE_AUDIT.md','docs\testing\manager-rc1-native.md','docs\NAudio-LICENSE.txt','docs\NVorbis-LICENSE.txt')) { Copy-Item -LiteralPath $document -Destination $output }
    # Only the manager and first-party metadata/third-party notices are packaged.
    $allowed = @('CrystalProjectModManager.exe','README.md','CHANGELOG.md','LICENSE','NOTICE.md','MUSIC_CUE_AUDIT.md','manager-rc1-native.md','NAudio-LICENSE.txt','NVorbis-LICENSE.txt')
    $unexpected = Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -notin $allowed -and $_.Extension -ne '.pdb' }
    if ($unexpected) { throw ('Unexpected package contents: ' + ($unexpected.Name -join ', ')) }
    $zip = Join-Path $repo 'artifacts\CrystalProjectModManager-0.1.0-rc1-win-x64.zip'
    $packageFiles = $allowed | ForEach-Object { Join-Path $output $_ }
    Compress-Archive -LiteralPath $packageFiles -DestinationPath $zip -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($zip + '.sha256') -Value ($hash + '  ' + (Split-Path $zip -Leaf)) -Encoding ascii
    Write-Host ('Development build: ' + $zip)
} finally { Pop-Location }
