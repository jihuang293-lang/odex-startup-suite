param([string]$OutputName='CodexCinematicBoot.exe')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll')
foreach ($name in @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')) {
    $references += Join-Path $framework ('WPF\' + $name)
}
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')),
    ('/out:' + (Join-Path $PSScriptRoot $OutputName)))
foreach ($reference in $references) { $arguments += '/reference:' + $reference }
$arguments += Join-Path $PSScriptRoot 'src\DragonCodexBoot.cs'
$arguments += Join-Path $PSScriptRoot 'src\SettingsWindow.cs'
$arguments += Join-Path $PSScriptRoot 'src\MediaResolver.cs'
$arguments += Join-Path $PSScriptRoot 'src\CinematicScene.cs'
$arguments += Join-Path $PSScriptRoot 'src\AestheticAnalyzer.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Dragon local integration build failed.' }
('Built ' + $OutputName + ' with external startup and skin-readiness gate.')
