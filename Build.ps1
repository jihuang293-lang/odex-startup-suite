$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'dragon-codex-boot\build-local.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dragon-codex-boot\CodexCinematicBoot.exe') -Destination (Join-Path $PSScriptRoot 'dragon-codex-boot\DragonCodexBoot.exe') -Force
& (Join-Path $PSScriptRoot 'codex-scene-bridge\build-local.ps1')
'Build complete. No applications were started.'
