[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ManagerPath,
    [string]$FfmpegPath = (Join-Path $PSScriptRoot 'dependencies\ffmpeg\ffmpeg.exe'),
    [switch]$NoShortcuts,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\Paths.ps1')
Assert-OrdinaryPath $PSScriptRoot
Assert-OrdinaryPath $ManagerPath
Assert-OrdinaryPath $FfmpegPath
$managerHash = '46FC0B8D8367AC02CE9A3FEC37BA34B182D0482EAEEBC5AE2CD56903C128E936'
$ffmpegHash = '3256173F3F8BFFD7DF12227C68ADF68025EDB1832273A9530688A7BB1ED8EDEC'
if ((Get-FileHash -LiteralPath $ManagerPath -Algorithm SHA256).Hash -cne $managerHash) { throw 'Expected the verified Codex Dynamic Skin 1.7.3 manager. No files were installed.' }
if ((Get-FileHash -LiteralPath $FfmpegPath -Algorithm SHA256).Hash -cne $ffmpegHash) { throw 'Expected the pinned FFmpeg 9.0.2 essentials executable. No files were installed.' }
$owned = @{
    'codex-dynamic-skin-trial\CodexDreamSkinManager.exe' = $ManagerPath
    'codex-dynamic-skin-trial\scene-runtime\SceneViewer.exe' = (Join-Path $PSScriptRoot 'codex-scene-bridge\SceneViewer.exe')
    'codex-dynamic-skin-trial\scene-runtime\scene-relay.mjs' = (Join-Path $PSScriptRoot 'codex-scene-bridge\src\scene-relay.mjs')
    'codex-dynamic-skin-trial\scene-runtime\LICENSE-bridge.txt' = (Join-Path $PSScriptRoot 'codex-scene-bridge\LICENSE')
    'codex-dynamic-skin-trial\scene-runtime\tools\ffmpeg.exe' = $FfmpegPath
    'dragon-codex-boot\tools\ffmpeg.exe' = $FfmpegPath
}
foreach ($relative in $owned.Keys) {
    $source = [IO.Path]::GetFullPath($owned[$relative]); $target = Join-Path $PSScriptRoot $relative
    Assert-OrdinaryPath $source; Assert-OrdinaryPath $target
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw ('Missing build input: '+$relative+'. Run Build.ps1 first.') }
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -cne (Get-FileHash -LiteralPath $source).Hash) { throw ('Existing different file preserved: '+$relative) }
}
foreach ($relative in @('dragon-codex-boot\CodexCinematicBoot.exe','dragon-codex-boot\DragonCodexBoot.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $relative))) { throw 'Build the animation before installing.' }
}
if(-not(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'codex-proxy\CodexStartupNetwork.dll'))){throw 'Build the native startup helper before installing.'}
if ($CheckOnly) { 'PASS: installation inputs and destination paths validated; nothing written.'; return }
foreach ($relative in $owned.Keys) {
    $target = Join-Path $PSScriptRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    if ([IO.Path]::GetFullPath($owned[$relative]) -ine [IO.Path]::GetFullPath($target)) { Copy-Item -LiteralPath $owned[$relative] -Destination $target -Force }
}
foreach ($dir in @('codex-dynamic-skin-trial\scene-runtime\tools','dragon-codex-boot\tools')) {
    foreach ($name in @('LICENSE','README.txt')) {
        $notice = Join-Path (Split-Path -Parent $FfmpegPath) $name
        if (Test-Path -LiteralPath $notice -PathType Leaf) { Copy-Item -LiteralPath $notice -Destination (Join-Path (Join-Path $PSScriptRoot $dir) $name) -Force }
    }
}
$config = Join-Path $PSScriptRoot 'dragon-codex-boot\launcher.json'
if (-not (Test-Path -LiteralPath $config)) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dragon-codex-boot\launcher.example.json') -Destination $config }
if (-not $NoShortcuts) {
    $shell = New-Object -ComObject WScript.Shell
    $desktop = [Environment]::GetFolderPath('Desktop')
    foreach ($item in @(
        @{Name='Codex Startup Suite.lnk'; Target=(Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'); Args=('-NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File "'+(Join-Path $PSScriptRoot 'codex-proxy\start-codex-with-skin.ps1')+'"')},
        @{Name='Codex Startup Settings.lnk'; Target=(Join-Path $PSScriptRoot 'dragon-codex-boot\CodexCinematicBoot.exe'); Args='--settings'}
    )) {
        $path = Join-Path $desktop $item.Name
        if (Test-Path -LiteralPath $path) {
            $old = $shell.CreateShortcut($path)
            if ($old.TargetPath -ine $item.Target -or $old.Arguments -cne $item.Args) { Write-Warning ('Existing shortcut preserved: '+$item.Name); continue }
        }
        $link = $shell.CreateShortcut($path); $link.TargetPath=$item.Target; $link.Arguments=$item.Args; $link.WorkingDirectory=$PSScriptRoot; $link.Save()
    }
}
'Installed into this directory. No application was restarted and no existing startup entry was changed.'
