param([string]$AssemblyPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'CodexCinematicBoot.exe'),[string]$ScenePosters)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$qa=Join-Path $root 'qa\aesthetics';New-Item -ItemType Directory -Path $qa -Force|Out-Null
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll',($framework+'\WPF\WindowsBase.dll'),($framework+'\WPF\PresentationCore.dll'),($framework+'\WPF\PresentationFramework.dll'),$AssemblyPath)
$arguments=@('/nologo','/target:exe','/platform:x64',('/out:'+(Join-Path $qa 'AestheticTests.exe')))
foreach($reference in $references){$arguments+=('/reference:'+$reference)}
$arguments+=(Join-Path $PSScriptRoot 'AestheticTests.cs')
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE-ne 0){throw 'Aesthetic tests did not compile.'}
Copy-Item -LiteralPath $AssemblyPath -Destination (Join-Path $qa ([IO.Path]::GetFileName($AssemblyPath))) -Force
$parameters=@($qa);if($ScenePosters){$parameters+=$ScenePosters}
& (Join-Path $qa 'AestheticTests.exe') @parameters
if($LASTEXITCODE-ne 0){throw 'Aesthetic checks or render failed.'}
