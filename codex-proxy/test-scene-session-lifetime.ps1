$ErrorActionPreference='Stop'
$fixture=Join-Path $PSScriptRoot ('qa-scene-recovery\controller-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force|Out-Null
$scriptPath=Join-Path $PSScriptRoot 'scene-session.ps1'
$bridge=Join-Path (Split-Path -Parent $PSScriptRoot) 'codex-dynamic-skin-trial\scene-runtime'
$exe=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$args='-NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File "'+$scriptPath+'" -SkinRoot "'+$fixture+'" -BridgeRoot "'+$bridge+'"'
$first=$null;$second=$null
try{
 $info=[Diagnostics.ProcessStartInfo]::new($exe,$args);$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle='Hidden'
 $first=[Diagnostics.Process]::Start($info)
 $deadline=[DateTime]::UtcNow.AddSeconds(8)
 while(-not(Test-Path -LiteralPath (Join-Path $fixture 'scene-session.json'))-and[DateTime]::UtcNow-lt$deadline){Start-Sleep -Milliseconds 50}
 if(-not(Test-Path -LiteralPath (Join-Path $fixture 'scene-session.json'))){throw 'Controller did not write bounded failure status.'}
 $second=[Diagnostics.Process]::Start($info)
 if(-not$second.WaitForExit(5000)-or$second.ExitCode-ne 0){throw 'Duplicate scene controller did not exit immediately.'}
 if(-not$first.WaitForExit(12000)-or$first.ExitCode-ne 0){throw 'Controller did not exit when no managed session existed.'}
 $status=[IO.File]::ReadAllText((Join-Path $fixture 'scene-session.json'))|ConvertFrom-Json
 if($status.ready-or$status.controllerPid-ne$first.Id){throw 'Duplicate controller overwrote the owning process status.'}
 'PASS: duplicate controller exits, missing-session retries are bounded, owning status preserved; no Codex or scene process started.'
}finally{foreach($process in @($first,$second)){if($process){if(-not$process.HasExited){$process.Kill()};$process.Dispose()}}}
