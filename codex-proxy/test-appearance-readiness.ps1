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
