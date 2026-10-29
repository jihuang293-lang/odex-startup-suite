[CmdletBinding()]
param([string]$SceneFrame)
$ErrorActionPreference='Stop'
$qa=Join-Path $PSScriptRoot ('qa\native-poster-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $qa -Force|Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$test=Join-Path $qa 'NativePosterTests.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /main:NativePosterTests /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$test" (Join-Path $PSScriptRoot 'src\SceneViewer.cs') (Join-Path $PSScriptRoot 'NativePosterTests.cs')
if($LASTEXITCODE-ne 0){throw 'Native poster tests did not compile.'}
$arguments=@($qa);if($SceneFrame){$arguments+=[IO.Path]::GetFullPath($SceneFrame)}
& $test @arguments
if($LASTEXITCODE-ne 0){throw 'Native poster cache validation failed.'}
