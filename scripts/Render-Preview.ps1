[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$FfmpegPath)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Paths.ps1')
Assert-OrdinaryPath $FfmpegPath
$assembly=Join-Path $root 'dragon-codex-boot\CodexCinematicBoot.exe'
if (-not(Test-Path -LiteralPath $assembly)) { & (Join-Path $root 'Build.ps1') }
$work=Join-Path $root '.test-output\preview-render'
New-Item -ItemType Directory -Path $work -Force | Out-Null
Copy-Item -LiteralPath $assembly -Destination (Join-Path $work 'CodexCinematicBoot.exe') -Force
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll',($framework+'\WPF\WindowsBase.dll'),($framework+'\WPF\PresentationCore.dll'),($framework+'\WPF\PresentationFramework.dll'),$assembly)
$compilerArgs=@('/nologo','/target:exe','/platform:x64',('/out:'+(Join-Path $work 'PreviewRenderer.exe')))
foreach($reference in $references){$compilerArgs+=('/reference:'+$reference)}
$compilerArgs+=(Join-Path $PSScriptRoot 'PreviewRenderer.cs')
& (Join-Path $framework 'csc.exe') @compilerArgs
if($LASTEXITCODE -ne 0){throw 'Preview renderer did not compile.'}
& (Join-Path $work 'PreviewRenderer.exe') $work (Join-Path $root 'dragon-codex-boot\launcher.example.json')
if($LASTEXITCODE -ne 0){throw 'Preview render failed.'}
$output=Join-Path $root 'docs\preview'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $work 'animation-overview.png') -Destination $output -Force
& $FfmpegPath -v error -y -framerate 12 -i (Join-Path $work 'frames\frame-%03d.png') -filter_complex '[0:v]scale=640:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=80:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle' -loop 0 (Join-Path $output 'animation-preview.gif')
if($LASTEXITCODE -ne 0){throw 'GIF encode failed.'}
'Rendered original sample artwork using the actual CinematicScene. No application was opened.'
