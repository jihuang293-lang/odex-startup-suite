[CmdletBinding()]
param([string]$OutputPath=(Join-Path $PSScriptRoot 'CodexStartupNetwork.dll'))
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:library /platform:x64 /optimize+ /reference:System.dll ('/out:'+$OutputPath) (Join-Path $PSScriptRoot 'StartupTcpTables.cs')
if($LASTEXITCODE-ne 0){throw 'Native TCP evidence helper compilation failed.'}
"Built $OutputPath"
