$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dragon-animation.ps1')
$root=Join-Path $PSScriptRoot 'qa-appearance-ready'
$null=New-Item -ItemType Directory -Path $root -Force
$theme=Join-Path $root 'theme.json';$report=Join-Path $root 'verify.log'
[IO.File]::WriteAllText($theme,'{"id":"selected-theme"}')
$valid=@{targets=@(@{result=@{installed=$true;stylePresent=$true;themeId='selected-theme';documentHidden=$false;mediaReady=$true;readiness=@{documentPass=$true;viewportPass=$true;structurePass=$true}}})}
foreach($case in @('ready','loading-shell','wrong-theme','wallpaper-loading','hidden','invalid-report')) {
    $data=($valid|ConvertTo-Json -Depth 8)|ConvertFrom-Json
    switch($case) {
        'loading-shell' {$data.targets[0].result.readiness.structurePass=$false}
        'wrong-theme' {$data.targets[0].result.themeId='old-theme'}
        'wallpaper-loading' {$data.targets[0].result.mediaReady=$false}
        'hidden' {$data.targets[0].result.documentHidden=$true}
    }
    $text="[diagnostic prefix]`n"+($data|ConvertTo-Json -Depth 8)
    if($case -eq 'invalid-report'){$text='not JSON'}
    [IO.File]::WriteAllText($report,$text)
    $actual=Test-CodexAppearanceReady -VerificationPath $report -ThemePath $theme
    if($actual -ne ($case -eq 'ready')){throw "Incorrect readiness: $case"}
}
'PASS: only a visible, complete interface with the selected wallpaper can release the transition.'
[IO.File]::WriteAllText($theme,'{"id":"selected-theme","media":{"type":"scene"}}')
foreach($case in @('streaming','dead-stream','no-chunks','stream-error')){
 $data=($valid|ConvertTo-Json -Depth 8)|ConvertFrom-Json
 $data.targets[0].result|Add-Member mediaStream $true
 $stats=[pscustomobject]@{status='streaming';chunks=4;error=$null}
 switch($case){'dead-stream'{$stats.status='error'};'no-chunks'{$stats.chunks=0};'stream-error'{$stats.error='Failed to fetch'}}
 $data.targets[0].result|Add-Member streamStats $stats
 [IO.File]::WriteAllText($report,($data|ConvertTo-Json -Depth 8))
 if((Test-CodexAppearanceReady -VerificationPath $report -ThemePath $theme)-ne($case-eq'streaming')){throw ('Scene transport readiness incorrect: '+$case)}
}
'PASS: scene handoff requires a live streaming transport with received chunks and no error.'
foreach($case in @('old-revision','wrong-version','media-element-missing','transparent-media')){
 $data=($valid|ConvertTo-Json -Depth 8)|ConvertFrom-Json
 $result=$data.targets[0].result
 switch($case){
  'old-revision'{$result|Add-Member expectedRevision 'current';$result|Add-Member revision 'previous'}
  'wrong-version'{$result|Add-Member expectedVersion '1.7.3';$result|Add-Member version 'old'}
  'media-element-missing'{$result|Add-Member mediaPresent $false}
  'transparent-media'{$result|Add-Member mediaElementOpacity '0'}
 }
 if(Test-CodexAppearanceReport -Report $data -Theme @{id='selected-theme'}){throw ('Incomplete skin incorrectly accepted: '+$case)}
}
'PASS: stale revisions, mismatched versions, missing media and invisible media are rejected.'
