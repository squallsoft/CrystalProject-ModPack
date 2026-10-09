param([string]$Sdk, [string]$Archive)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$Sdk) { $Sdk = Join-Path $repo 'artifacts\dotnet\dotnet.exe'; if (!(Test-Path -LiteralPath $Sdk)) { $Sdk = 'dotnet' } }
$testArgs = @($repo)
if ($Archive) { $testArgs += [System.IO.Path]::GetFullPath($Archive) }
Push-Location $repo
try {
    & $Sdk run --project tests\Sprites -c Release -- @testArgs
    if ($LASTEXITCODE) { throw 'Enemy sprite tests failed' }
} finally { Pop-Location }
