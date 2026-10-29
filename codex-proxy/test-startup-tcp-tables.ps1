$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'scene-recovery.ps1')
. (Join-Path $PSScriptRoot 'startup-tcp-tables.ps1')
$checks=0
function Check([bool]$Condition,[string]$Label){if(-not$Condition){throw $Label};$script:checks++}
function Key($Row){'{0}|{1}|{2}'-f$Row.LocalAddress,$Row.LocalPort,$Row.OwningProcess}
$owned=@()
try{
 foreach($address in @([Net.IPAddress]::Loopback,[Net.IPAddress]::IPv6Loopback)){
  $listener=[Net.Sockets.TcpListener]::new($address,0);$listener.Start();$owned+=$listener
  $port=$listener.LocalEndpoint.Port
  $native=@(Get-CodexNativeTcpListeners $port)
  $system=@(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction Stop)
  Check ((($native|ForEach-Object{Key $_}|Sort-Object)-join',')-ceq(($system|ForEach-Object{Key $_}|Sort-Object)-join',')) ('Native/system evidence agrees for '+$address)
  Check (@($native|Where-Object{$_.LocalAddress-eq$address.ToString()-and$_.OwningProcess-eq$PID}).Count-eq 1) ('Exact listener owner for '+$address)
  $listener.Stop()
  Check (@(Get-CodexNativeTcpListeners $port).Count-eq 0) 'No stale owner evidence after closing the owned listener'
 }
 foreach($invalid in @(0,65536,-1)){$blocked=$false;try{$null=Get-CodexNativeTcpListeners $invalid}catch{$blocked=$true};Check $blocked ('Invalid port rejected: '+$invalid)}
 $nativeMethod=(Get-Item 'Function:\Get-CodexNativeTcpListeners').ScriptBlock
 function Get-CodexNativeTcpListeners {[CmdletBinding()]param($Port) throw 'Fixture native-provider failure'}
 function Get-NetTCPConnection {param($State,$LocalPort,$ErrorAction) [pscustomobject]@{LocalAddress='::1';LocalPort=$LocalPort;OwningProcess=123}}
 $fallback=@(Get-CodexStartupTcpListeners 9335)
 Check ($fallback.Count-eq 1-and$fallback[0].LocalAddress-eq'::1'-and$fallback[0].OwningProcess-eq 123) 'Native failure retains the original provider evidence'
 Remove-Item 'Function:\Get-NetTCPConnection'
 Set-Item 'Function:\Get-CodexNativeTcpListeners' $nativeMethod
 $timingListener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$timingListener.Start();$owned+=$timingListener;$timingPort=$timingListener.LocalEndpoint.Port
 $clock=[Diagnostics.Stopwatch]::StartNew();$native=@(Get-CodexNativeTcpListeners $timingPort);$clock.Stop();$nativeMs=$clock.Elapsed.TotalMilliseconds
 $clock.Restart();$system=@(Get-NetTCPConnection -State Listen -LocalPort $timingPort -ErrorAction Stop);$clock.Stop();$systemMs=$clock.Elapsed.TotalMilliseconds
 Check ((($native|ForEach-Object{Key $_}|Sort-Object)-join',')-ceq(($system|ForEach-Object{Key $_}|Sort-Object)-join',')) 'Owned timing listener matches the system provider'
 $qa=Join-Path $PSScriptRoot 'qa-native-network';New-Item -ItemType Directory -Path $qa -Force|Out-Null
 $proof=[ordered]@{ok=$true;checks=$checks;ipv4AndIpv6Tested=$true;exactPidAgreement=$true;closedListenerNotCached=$true;fallbackTested=$true;nativeMilliseconds=[math]::Round($nativeMs,2);originalMilliseconds=[math]::Round($systemMs,2);helperSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'CodexStartupNetwork.dll') -Algorithm SHA256).Hash;noCodexRestart=$true}
 [IO.File]::WriteAllText((Join-Path $qa 'network-proof.json'),($proof|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
 $proof|ConvertTo-Json
}finally{foreach($listener in $owned){$listener.Stop()}}
