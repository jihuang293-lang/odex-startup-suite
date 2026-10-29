# Version-bound change to the third-party skin watcher only. The official
# Codex package, selected wallpaper and readiness requirements are untouched.
$script:CodexColdStartOriginalHash='F2157C7C97C1BBAAB6036E78C59BB0EED40AE0062CDF11FA491B3A3677E2AD5E'
function ConvertTo-CodexColdStartInjector {
    param([Parameter(Mandatory=$true)][string]$Source)
    $edits=@(
        @('async function runWatch(options) {',@'
// CODEX_LOCAL_COLD_START_RETRY_V1
export function startupTargetRetryDelay(baseDelayMs, failures, elapsedMs, hasError) {
  if (!hasError && baseDelayMs === 5000 && elapsedMs >= 0 && elapsedMs < 45000) return 500;
  return Math.min(30000, baseDelayMs * (2 ** Math.min(failures - 1, 4)));
}

async function runWatch(options) {
  const watchStartedAt = Date.now();
'@),
        @('const delayMs = Math.min(30000, baseDelayMs * (2 ** Math.min(failures - 1, 4)));',
          'const delayMs = startupTargetRetryDelay(baseDelayMs, failures, Date.now() - watchStartedAt, Boolean(error));'),
        @('const probe = await waitForCodexProbe(session);',
          'const probe = await waitForCodexProbe(session, Date.now() - watchStartedAt < 45000 ? 250 : 1800);'),
        @('rejectTarget(target, 5000);','rejectTarget(target, probe?.excludedPetSurface ? 30000 : 5000);')
    )
    $newline=if($Source.Contains("`r`n")){"`r`n"}else{"`n"}
    foreach($edit in $edits){
        if([regex]::Matches($Source,[regex]::Escape($edit[0])).Count-ne 1){throw 'Skin watcher source differs from the reviewed cold-start patch.'}
        $replacement=$edit[1].Replace("`r`n","`n").Replace("`n",$newline)
        $Source=$Source.Replace($edit[0],$replacement)
    }
    return $Source
}
function Get-CodexColdStartTextHash {
    param([string]$Text)
    $sha=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($sha.ComputeHash([Text.UTF8Encoding]::new($false).GetBytes($Text))).Replace('-','')}finally{$sha.Dispose()}
}
function Install-CodexColdStartPatch {
    param([string]$RuntimeRoot)
    $path=Join-Path $RuntimeRoot 'payload\scripts\injector.mjs'
    $backup=$path+'.before-codex-cold-start'
    Assert-CodexScenePath $path;Assert-CodexScenePath $backup
    $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if($hash-ceq$script:CodexColdStartOriginalHash){
        $source=[IO.File]::ReadAllText($path)
        if(Test-Path -LiteralPath $backup){
            if((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash-cne$script:CodexColdStartOriginalHash){throw 'Cold-start backup does not match the reviewed watcher.'}
        }else{[IO.File]::WriteAllBytes($backup,[IO.File]::ReadAllBytes($path))}
    }else{
        if(-not(Test-Path -LiteralPath $backup)-or(Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash-cne$script:CodexColdStartOriginalHash){throw 'The skin watcher has changed; its cold-start patch needs review.'}
        $source=[IO.File]::ReadAllText($backup)
    }
    $patched=ConvertTo-CodexColdStartInjector $source
    $expected=Get-CodexColdStartTextHash $patched
    if($hash-ceq$expected){return}
    if($hash-cne$script:CodexColdStartOriginalHash){throw 'Unrecognized skin watcher changes were preserved.'}
    $temp=$path+'.'+[Guid]::NewGuid().ToString('N')+'.tmp';$swap=$temp+'.replaced'
    try{
        [IO.File]::WriteAllText($temp,$patched,[Text.UTF8Encoding]::new($false))
        [IO.File]::Replace($temp,$path,$swap)
    }finally{foreach($owned in @($temp,$swap)){if(Test-Path -LiteralPath $owned){Remove-Item -LiteralPath $owned -Force}}}
}
function Restore-CodexColdStartPatch {
    param([string]$RuntimeRoot)
    $path=Join-Path $RuntimeRoot 'payload\scripts\injector.mjs';$backup=$path+'.before-codex-cold-start'
    Assert-CodexScenePath $path;Assert-CodexScenePath $backup
    if(-not(Test-Path -LiteralPath $path)-or-not(Test-Path -LiteralPath $backup)){return $false}
    if((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash-cne$script:CodexColdStartOriginalHash){throw 'Unrecognized watcher backup was preserved.'}
    $current=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if($current-ceq$script:CodexColdStartOriginalHash){return $false}
    $patchedHash=Get-CodexColdStartTextHash (ConvertTo-CodexColdStartInjector ([IO.File]::ReadAllText($backup)))
    if($current-cne$patchedHash){throw 'Newer watcher changes were preserved instead of restoring over them.'}
    $temp=$path+'.'+[Guid]::NewGuid().ToString('N')+'.tmp';$swap=$temp+'.replaced'
    try{[IO.File]::Copy($backup,$temp);[IO.File]::Replace($temp,$path,$swap)}finally{foreach($owned in @($temp,$swap)){if(Test-Path -LiteralPath $owned){Remove-Item -LiteralPath $owned -Force}}}
    return $true
}
