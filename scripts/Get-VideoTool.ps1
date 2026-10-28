[CmdletBinding()]
param([string]$Proxy)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Paths.ps1')
$root=Split-Path -Parent $PSScriptRoot
$dir=Join-Path $root 'dependencies\ffmpeg'
Assert-OrdinaryPath $dir
New-Item -ItemType Directory -Path $dir -Force | Out-Null
$archive=Join-Path $dir 'ffmpeg-9.0.2-essentials_build.7z'
$url='https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.7z'
if (-not $Proxy) {
    . (Join-Path $root 'codex-proxy\proxy-network.ps1')
    $settings=Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
    if ($settings.ProxyEnable -eq 1) { $Proxy=(ConvertTo-CodexLocalProxy -ProxyServer $settings.ProxyServer -ProxyEnabled 1).Proxy }
}
$downloadArgs=@('--location','--fail','--silent','--show-error','--proto','=https','--proto-redir','=https','--connect-timeout','15','--max-time','180','--output',$archive)
if ($Proxy) { $downloadArgs+=@('--proxy',$Proxy) }
$downloadArgs+=$url
& (Join-Path $env:WINDIR 'System32\curl.exe') @downloadArgs
if ($LASTEXITCODE -ne 0) { throw 'Dependency download failed or timed out. Retry later or provide the verified executable using -FfmpegPath.' }
if ((Get-FileHash -LiteralPath $archive).Hash -ine '4705843ccaaf54257c16ad90f3e952ece33c17df964ecf7bfdbb0f49c7171077') { throw 'Archive digest mismatch; no downloaded executable was run.' }
$prefix='ffmpeg-9.0.2-essentials_build'
$extract=Join-Path $dir ('extract-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $extract | Out-Null
& (Join-Path $env:WINDIR 'System32\tar.exe') -xf $archive -C $extract ($prefix+'/bin/ffmpeg.exe') ($prefix+'/LICENSE') ($prefix+'/README.txt')
if ($LASTEXITCODE -ne 0) { throw 'Windows tar could not extract the verified 7z. Extract those three files manually into dependencies/ffmpeg.' }
$exe=Join-Path $extract ($prefix+'\bin\ffmpeg.exe')
if ((Get-FileHash -LiteralPath $exe).Hash -ine '3256173f3f8bffd7df12227c68adf68025edb1832273a9530688a7bb1ed8edec') { throw 'Executable digest mismatch.' }
Copy-Item -LiteralPath $exe -Destination (Join-Path $dir 'ffmpeg.exe') -Force
foreach ($name in @('LICENSE','README.txt')) { Copy-Item -LiteralPath (Join-Path $extract ($prefix+'\'+$name)) -Destination (Join-Path $dir $name) -Force }
Remove-Item -LiteralPath (Assert-ChildPath $extract $dir) -Recurse -Force
Remove-Item -LiteralPath (Assert-ChildPath $archive $dir) -Force
'Verified FFmpeg and its upstream notices are ready. No executable was run.'
