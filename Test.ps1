[CmdletBinding()]
param([string]$NodePath = 'node')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$out=Join-Path $root '.test-output'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$syntax=0
foreach ($file in @(Get-ChildItem -LiteralPath $root -File -Recurse -Filter '*.ps1' | Where-Object {$_.FullName -notmatch '\\(dist|dependencies|\.test-output)\\'})) {
    $tokens=$null; $errors=$null
    $null=[Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    if ($errors.Count) { throw ($file.Name+': '+($errors.Message -join '; ')) }; $syntax++
}
& (Join-Path $root 'Build.ps1')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$assembly=Join-Path $root 'dragon-codex-boot\CodexCinematicBoot.exe'
$config=Join-Path $root 'dragon-codex-boot\launcher.example.json'
foreach ($name in @('GeometryTests','MediaResolverTests','CinematicTests')) {
    $exe=Join-Path $out ($name+'.exe')
    & $compiler /nologo /target:exe /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll ("/reference:"+$assembly) ("/out:"+$exe) (Join-Path $root ('dragon-codex-boot\tests\'+$name+'.cs'))
    if ($LASTEXITCODE -ne 0) { throw ('Test compilation failed: '+$name) }
    Copy-Item -LiteralPath $assembly -Destination (Join-Path $out 'CodexCinematicBoot.exe') -Force
    $arg=if($name -eq 'MediaResolverTests'){Join-Path $out 'media-fixture'}else{$config}
    & $exe $arg
    if ($LASTEXITCODE -ne 0) { throw ('Test failed: '+$name) }
}
& (Join-Path $root 'codex-scene-bridge\SceneViewer.exe') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Scene bridge self-test failed.' }
$ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
foreach ($relative in @('codex-proxy\test-combined-launch.ps1','codex-proxy\test-restart-decision.ps1','codex-proxy\test-animation-entry.ps1','codex-proxy\test-appearance-readiness.ps1','codex-proxy\test-wallpaper-resolution.ps1','codex-scene-bridge\test-scene-library.ps1','tests\test-portable-config.ps1')) {
    & $ps -NoProfile -ExecutionPolicy RemoteSigned -File (Join-Path $root $relative)
    if ($LASTEXITCODE -ne 0) { throw ('Test failed: '+$relative) }
}
& $NodePath --check (Join-Path $root 'codex-scene-bridge\src\scene-relay.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Node syntax failed.' }
& $NodePath (Join-Path $root 'codex-scene-bridge\test-scene-relay.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Scene relay tests failed.' }
[pscustomobject]@{ok=$true;powerShellSyntaxFiles=$syntax;desktopUiOpened=$false;liveAppRestarted=$false;scope='Offline fixtures and build; not a clean-machine end-to-end launch'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding UTF8
'PASS: build, configuration, media resolution, proxy integration, scene alias guards and scoped transport.'
