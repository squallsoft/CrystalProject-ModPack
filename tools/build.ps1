param([string]$Sdk)
# Compatibility entry point. New builds produce the WPF manager, not another v0.1.0 installer.
& (Join-Path $PSScriptRoot 'build-manager.ps1') -Sdk $Sdk
