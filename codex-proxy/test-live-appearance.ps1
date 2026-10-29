$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dragon-animation.ps1')
$fixture=Join-Path $PSScriptRoot 'qa-live-appearance'
$runtime=Join-Path $fixture 'runtime';$themeDir=Join-Path $fixture 'active-theme'
New-Item -ItemType Directory -Path $themeDir -Force|Out-Null
$state=[pscustomobject]@{port=9335;browserId=('a'*32);nodePath=(Join-Path $runtime 'node\node.exe');injectorPath=(Join-Path $runtime 'payload\scripts\injector.mjs');codexExe='C:\registered-package\app\ChatGPT.exe'}
$theme=[pscustomobject]@{id='scene-selected';media=@{type='scene';streamUrl='current-stream'}}
$valid=[pscustomobject]@{targets=@(@{result=@{installed=$true;stylePresent=$true;themeId='scene-selected';documentHidden=$false;mediaReady=$true;mediaStream=$true;streamStats=@{status='streaming';chunks=12;error=$null};readiness=@{documentPass=$true;viewportPass=$true;structurePass=$true}}})}
function Reset-Fixture {
 [IO.File]::WriteAllText((Join-Path $fixture 'state.json'),($state|ConvertTo-Json -Depth 5))
 [IO.File]::WriteAllText((Join-Path $themeDir 'theme.json'),($theme|ConvertTo-Json -Depth 5))
 [IO.File]::WriteAllText((Join-Path $fixture 'verify.log'),($valid|ConvertTo-Json -Depth 8))
 $script:rendererCalls=0;$script:windowCalls=0
}
function Get-CodexHandoffWindow {
 param($ExecutablePath,[switch]$TryActivate)
 $script:windowCalls++
 if($case-eq'no-native-window'){return $null}
 [pscustomobject]@{Handle=$(if($case-eq'window-replaced'-and$script:windowCalls-gt 1){999}else{123});ProcessId=456;Executable=$state.codexExe}
}
function Get-CodexStartupTcpListeners {param($Port) [pscustomobject]@{LocalAddress='127.0.0.1';OwningProcess=456}}
function Get-CimInstance {param($ClassName,$Filter) [pscustomobject]@{ExecutablePath=$(if($case-eq'foreign-listener'){'C:\other-app.exe'}else{$state.codexExe})}}
function Get-CodexLiveRendererReport {
 param($Node,$Injector,$BrowserId,$ThemeDirectory,$TimeoutMilliseconds)
 $script:rendererCalls++
 if($BrowserId-cne$state.browserId-or$Node-cne$state.nodePath-or$Injector-cne$state.injectorPath){throw 'Unsafe renderer probe arguments'}
 if($case-eq'changed-browser'){$changed=$state|ConvertTo-Json|ConvertFrom-Json;$changed.browserId=('b'*32);[IO.File]::WriteAllText((Join-Path $fixture 'state.json'),($changed|ConvertTo-Json))}
 if($case-eq'changed-theme'){$changed=$theme|ConvertTo-Json -Depth 5|ConvertFrom-Json;$changed.media.streamUrl='newer-user-selection';[IO.File]::WriteAllText((Join-Path $themeDir 'theme.json'),($changed|ConvertTo-Json -Depth 5))}
 if($case-eq'stale-cache-only'){return [pscustomobject]@{targets=@()}}
 return $valid
}
foreach($case in @('live-ready','stale-cache-only','no-native-window','foreign-listener','window-replaced','changed-browser','changed-theme')){
 Reset-Fixture
 $actual=Test-CodexLiveAppearanceReady -SkinRoot $fixture -RuntimeRoot $runtime
 if($actual-ne($case-eq'live-ready')){throw ('Unsafe live readiness result: '+$case)}
 if($case-eq'no-native-window'-and$script:rendererCalls-ne 0){throw 'Background-only CDP session was treated as an available handoff window'}
}
'PASS: fresh renderer and native handoff window required; stale cache, foreign listener, missing/replaced HWND, changed browser and changed wallpaper all rejected. No applications started or stopped.'
