$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$qa=Join-Path $root 'qa\poster-composition';New-Item -ItemType Directory -Path $qa -Force|Out-Null
$assembly=Join-Path $root 'CodexCinematicBoot.exe'
$references=@('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll',($framework+'\WPF\WindowsBase.dll'),($framework+'\WPF\PresentationCore.dll'),($framework+'\WPF\PresentationFramework.dll'),$assembly)
$arguments=@('/nologo','/target:exe','/platform:x64',('/out:'+(Join-Path $qa 'PosterCompositionTests.exe')))
foreach($reference in $references){$arguments+=('/reference:'+$reference)}
$arguments+=(Join-Path $PSScriptRoot 'PosterCompositionTests.cs')
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE-ne 0){throw 'Composition tests did not compile.'}
Copy-Item -LiteralPath $assembly -Destination (Join-Path $qa 'CodexCinematicBoot.exe') -Force
& (Join-Path $qa 'PosterCompositionTests.exe') $qa
if($LASTEXITCODE-ne 0){throw 'Composition render test failed.'}
