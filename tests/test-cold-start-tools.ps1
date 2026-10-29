$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'codex-proxy\scene-recovery.ps1')
. (Join-Path $root 'codex-proxy\cold-start-patch.ps1')
# Exercise only the version-bound transformation/install/restore engine on a
# synthetic source. The external manager's full payload is not redistributed.
$source=@'
async function runWatch(options) {
 const rejectTarget = (target, baseDelayMs, error) => {
  const failures = 1;
  const delayMs = Math.min(30000, baseDelayMs * (2 ** Math.min(failures - 1, 4)));
  return delayMs;
 };
 const session = {}, target = {};
 const probe = await waitForCodexProbe(session);
 rejectTarget(target, 5000);
}
'@
$script:CodexColdStartOriginalHash=Get-CodexColdStartTextHash $source
$fixture=Join-Path $root ('.test-output\patch-'+[Guid]::NewGuid().ToString('N'))
$directory=Join-Path $fixture 'payload\scripts';New-Item -ItemType Directory -Path $directory -Force|Out-Null
$path=Join-Path $directory 'injector.mjs'
[IO.File]::WriteAllText($path,$source,[Text.UTF8Encoding]::new($false))
$expected=Get-CodexColdStartTextHash (ConvertTo-CodexColdStartInjector $source)
Install-CodexColdStartPatch $fixture
if((Get-FileHash $path).Hash-cne$expected){throw 'Installed patch differs from the reviewed transformation'}
$stamp=(Get-Item $path).LastWriteTimeUtc.Ticks
Install-CodexColdStartPatch $fixture
if((Get-Item $path).LastWriteTimeUtc.Ticks-ne$stamp){throw 'Idempotent install changed the file'}
if(-not(Restore-CodexColdStartPatch $fixture)-or(Get-FileHash $path).Hash-cne$script:CodexColdStartOriginalHash){throw 'Exact original backup did not restore'}
Install-CodexColdStartPatch $fixture
[IO.File]::AppendAllText($path,"`n// newer user edit")
$newer=(Get-FileHash $path).Hash
foreach($action in @('Install-CodexColdStartPatch','Restore-CodexColdStartPatch')){$blocked=$false;try{$null=& $action $fixture}catch{$blocked=$true};if(-not$blocked-or(Get-FileHash $path).Hash-cne$newer){throw 'Newer source was overwritten'}}
'PASS: isolated source transformation, exact backup, idempotency, restore/reapply and newer-source preservation; no installed runtime changed.'
