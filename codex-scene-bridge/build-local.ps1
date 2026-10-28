param([string]$OutputPath = (Join-Path $PSScriptRoot 'SceneViewer.exe'))
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sourcePath = Join-Path $PSScriptRoot 'src\SceneViewer.cs'
& $compilerPath /nologo /target:exe /platform:x64 /optimize+ /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$OutputPath" $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Scene bridge build failed.' }
Write-Output ('Built local Wallpaper Engine bridge: ' + $OutputPath)
