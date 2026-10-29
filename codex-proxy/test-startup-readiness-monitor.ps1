$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dragon-animation.ps1')
$qa=Join-Path $PSScriptRoot 'qa-readiness-monitor'
New-Item -ItemType Directory -Path $qa -Force|Out-Null
$checks=0
function Check([bool]$Condition,[string]$Label){if(-not$Condition){throw $Label};$script:checks++}

# Exercise the real process/output parser, including the exit-2 report that
# used to be discarded even when its visible skin was ready to hand off.
$source=@'
using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Collections.Generic;
class RendererReportFixture {
 public static int Main(string[] args) {
  var s=new JavaScriptSerializer();
  var c=s.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[0]));
  Console.Write(Convert.ToString(c["output"]));
  return Convert.ToInt32(c["exitCode"]);
 }
}
'@
$sourcePath=Join-Path $qa 'RendererReportFixture.cs'
$exe=Join-Path $qa 'RendererReportFixture.exe'
[IO.File]::WriteAllText($sourcePath,$source)
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /reference:System.Web.Extensions.dll ('/out:'+$exe) $sourcePath
if($LASTEXITCODE-ne 0){throw 'Fixture compilation failed'}
$fixture=Join-Path $qa 'renderer.json'
$report=@{mode='verify';port=9335;targets=@(@{result=@{pass=$false;installed=$true;stylePresent=$true;themeId='selected';documentHidden=$false;mediaReady=$true;readiness=@{documentPass=$true;viewportPass=$true;structurePass=$true}}})}
foreach($case in @('success','layout-only-failure','invalid-json','foreign-mode','foreign-port','unexpected-exit')){
 $value=$report|ConvertTo-Json -Depth 8|ConvertFrom-Json
 if($case-eq'foreign-mode'){$value.mode='once'}
 if($case-eq'foreign-port'){$value.port=9222}
 $output=$value|ConvertTo-Json -Depth 8 -Compress
 if($case-eq'invalid-json'){$output='incomplete report'}
 $exitCode=if($case-eq'success'){0}elseif($case-eq'unexpected-exit'){1}else{2}
 [IO.File]::WriteAllText($fixture,(@{output=$output;exitCode=$exitCode}|ConvertTo-Json -Compress))
 try{$actual=Get-CodexLiveRendererReport -Node $exe -Injector $fixture -BrowserId ('a'*32) -ThemeDirectory $qa -TimeoutMilliseconds 500}catch{$actual=$null}
 Check ($null-ne$actual-eq($case-in@('success','layout-only-failure'))) ('Report parser: '+$case)
 if($case-eq'layout-only-failure'){Check (Test-CodexAppearanceReport -Report $actual -Theme @{id='selected'}) 'A separate failed layout check must not block a visible ready skin'}
}

# Deterministic time/identity fixtures: no Codex process or window is touched.
function Write-CodexReadinessEvent {param($Stage,$Detail) $script:events.Add($Stage)}
function Get-CodexStartupPlayer {
 param($PlayerId,$StartedAtTicks)
 if(-not$script:alive){return $null}
 if($script:case-eq'exit-during-probe'-and$script:probeCalls-gt 0){$script:alive=$false;return $null}
 [pscustomobject]@{Id=$PlayerId;Path='verified-fixture';HasExited=$false}
}
function Test-CodexLiveAppearanceReady {
 param($SkinRoot,$RuntimeRoot,$TimeoutMilliseconds)
 $script:probeCalls++
 # Simulate user Esc closing the cover while a CDP call is in flight.
 if($script:case-eq'exit-during-probe'){return $true}
 return $script:case-eq'ready-before-upstream-finishes'
}
function Start-Sleep {
 param($Milliseconds)
 $script:sleeps++
 $script:signalObserved=Test-Path -LiteralPath $script:flag
 # Upstream startup deliberately remains unfinished throughout these probes.
 Check (-not$script:upstreamFinished) 'The monitor must run independently of the blocking startup helper'
 if($script:case-eq'deadline'){[Threading.Thread]::Sleep(1000)}else{$script:alive=$false}
}
$animationRoot=Join-Path (Split-Path -Parent $PSScriptRoot) 'dragon-codex-boot'
foreach($script:case in @('ready-before-upstream-finishes','not-ready','already-skipped','exit-during-probe','already-released','deadline')){
 $script:flag=Join-Path $animationRoot ('client-ready.'+[Guid]::NewGuid().ToString('N')+'.flag')
 $script:events=New-Object 'Collections.Generic.List[string]'
 $script:probeCalls=0;$script:sleeps=0;$script:signalObserved=$false;$script:alive=$script:case-ne'already-skipped';$script:upstreamFinished=$false
 if($script:case-eq'already-released'){[IO.File]::WriteAllText($script:flag,'ready')}
 try{
  Invoke-CodexStartupReadinessMonitor -PlayerId 123 -StartedAtTicks 456 -ReadyFile $script:flag -TimeoutSeconds 2
  Check ($script:signalObserved-eq($script:case-in@('ready-before-upstream-finishes','already-released'))) ('Readiness signal: '+$script:case)
  Check (-not(Test-Path -LiteralPath $script:flag)) ('Clean only the exited player signal: '+$script:case)
  if($script:case-eq'already-skipped'){Check ($script:probeCalls-eq 0) 'Esc must stop unnecessary probes'}
  if($script:case-eq'exit-during-probe'){Check ($script:events-notcontains'appearance-ready-independent') 'A closed player must never release a subsequent animation'}
  if($script:case-eq'ready-before-upstream-finishes'){Check ($script:events-contains'appearance-ready-independent') 'Release reason must be logged'}
  if($script:case-eq'deadline'){Check ($script:events-contains'monitor-deadline') 'Deadline must stop probing without pretending the skin is ready'}
 }finally{if(Test-Path -LiteralPath $script:flag){Remove-Item -LiteralPath $script:flag -Force}}
}
Check (-not(Test-CodexStartupReadyPath (Join-Path $qa 'client-ready.flag'))) 'Reject a readiness flag outside the player directory'
Check (-not(Test-CodexStartupReadyPath (Join-Path $animationRoot 'client-ready.flag'))) 'Reject the old shared flag'
Check (-not(Test-CodexStartupReadyPath (Join-Path $animationRoot 'client-ready.bad.flag'))) 'Reject malformed session identifiers'
"PASS: $checks checks; fresh structured exit-2 reports, independent early release, no premature ready, Esc cancellation, per-player cleanup and path isolation. No Codex windows started or stopped."
