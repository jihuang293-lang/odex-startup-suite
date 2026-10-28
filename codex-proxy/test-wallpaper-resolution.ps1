$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dragon-animation.ps1')
$qa = Join-Path (Split-Path -Parent $PSScriptRoot) 'dragon-codex-boot\qa\media-resolution'
New-Item -ItemType Directory -Path $qa -Force | Out-Null
$themePath = Join-Path $qa 'theme.json'
$first = Join-Path $qa 'first wallpaper.mp4'
$second = Join-Path $qa 'second wallpaper.mp4'
[IO.File]::WriteAllText($first, 'fixture'); [IO.File]::WriteAllText($second, 'fixture')
$configuration = [pscustomobject]@{FollowSkin=$true;CustomVideo='';Video=''}
[IO.File]::WriteAllText($themePath, '{"image":"first wallpaper.mp4"}')
if ((Resolve-CodexAnimationMedia $configuration $themePath $qa) -ne $first) { throw 'Current theme was not resolved.' }
[IO.File]::WriteAllText($themePath, '{"image":"second wallpaper.mp4"}')
if ((Resolve-CodexAnimationMedia $configuration $themePath $qa) -ne $second) { throw 'A changed skin reused the old wallpaper.' }
$configuration.FollowSkin=$false;$configuration.CustomVideo=$first
if ((Resolve-CodexAnimationMedia $configuration $themePath $qa) -ne $first) { throw 'Custom file selection was ignored.' }
$configuration.CustomVideo=Join-Path $qa 'missing.mp4'
$rejected=$false
try { $null=Resolve-CodexAnimationMedia $configuration $themePath $qa } catch {$rejected=$true}
if (-not $rejected) { throw 'Missing media was accepted.' }
'PASS: current skin, changed skin, custom file, and missing-media handling; no applications started.'
