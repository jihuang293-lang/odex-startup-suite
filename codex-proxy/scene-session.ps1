[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SkinRoot,[Parameter(Mandatory=$true)][string]$BridgeRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'scene-recovery.ps1')
Assert-CodexScenePath $SkinRoot;Assert-CodexScenePath $BridgeRoot
$hash=[Security.Cryptography.SHA256]::Create();try{$key=[BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($SkinRoot).ToLowerInvariant()))).Replace('-','').Substring(0,20)}finally{$hash.Dispose()}
$mutex=[Threading.Mutex]::new($false,('Local\CodexSceneSession-'+$key));$owns=$false;$player=$null;$errorLine=$null;$startedBrowser=$null;$startedScene=$null;$streamUrl=$null
$statusPath=Join-Path $SkinRoot 'scene-session.json';$themePath=Join-Path $SkinRoot 'active-theme\theme.json'
$statePath=Join-Path $SkinRoot 'state.json';$misses=0;$failures=0;$lastError=$null
function Stop-OwnedScene {
    if($script:player){try{if(-not$script:player.HasExited){$script:player.StandardInput.WriteLine('{"command":"shutdown"}');$script:player.StandardInput.Flush();if(-not$script:player.WaitForExit(7000)){$script:player.Kill()}}}catch{}finally{$script:player.Dispose();$script:player=$null}}
    $script:streamUrl=$null;$script:errorLine=$null
}
function Save-SceneStatus {
    param([bool]$Ready,[string]$Browser,[string]$ThemeId,[string]$Url)
    $record=[ordered]@{ready=$Ready;browserId=$Browser;themeId=$ThemeId;streamUrl=$Url;controllerPid=$PID;error=$script:lastError;updatedAt=[DateTimeOffset]::UtcNow.ToString('o')}
    $temp=$statusPath+'.'+$PID+'.tmp';$backup=$temp+'.old'
    try{[IO.File]::WriteAllText($temp,($record|ConvertTo-Json),[Text.UTF8Encoding]::new($false));if(Test-Path -LiteralPath $statusPath){[IO.File]::Replace($temp,$statusPath,$backup)}else{[IO.File]::Move($temp,$statusPath)}}finally{foreach($file in @($temp,$backup)){if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file -Force}}}
}
try{
    try{$owns=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$owns=$true}
    if(-not$owns){return}
    while($true){
        $phase='session'
        try{
            $state=Read-CodexSceneJson $statePath
            if($state.port-ne 9335-or$state.browserId-notmatch'^[a-fA-F0-9-]{32,36}$'){throw 'Invalid managed browser identity.'}
            $request=[Net.HttpWebRequest]::Create('http://127.0.0.1:9335/json/version');$request.Proxy=$null;$request.Timeout=1500;$request.ReadWriteTimeout=1500;$response=$request.GetResponse()
            try{$reader=[IO.StreamReader]::new($response.GetResponseStream());try{$text=New-Object char[] 65537;$length=$reader.ReadBlock($text,0,$text.Length);if($length-gt 65536){throw 'Oversized debug metadata.'};$version=([string]::new($text,0,$length))|ConvertFrom-Json}finally{$reader.Dispose()}}finally{$response.Dispose()}
            if(-not([string]$version.webSocketDebuggerUrl).EndsWith('/'+$state.browserId,[StringComparison]::Ordinal)){throw 'Managed browser identity changed.'}
            $misses=0;$phase='scene';$theme=Read-CodexSceneJson $themePath;$scene=Get-CodexSceneDescription $theme
            if(-not$scene){Stop-OwnedScene;Save-SceneStatus $false $state.browserId $theme.id '';Start-Sleep -Seconds 1;continue}
            if($player-and($player.HasExited-or$startedBrowser-cne$state.browserId-or$startedScene-ine$scene.Scene-or$theme.media.streamUrl-cne$streamUrl)){Stop-OwnedScene}
            if(-not$player-and(Test-CodexSceneStream $theme.media.streamUrl)){
                $failures=0;$lastError=$null
                Save-SceneStatus $true $state.browserId $theme.id $theme.media.streamUrl
                Start-Sleep -Seconds 1;continue
            }
            if(-not$player){
                Save-SceneStatus $false $state.browserId $theme.id ''
                $exe=Join-Path $BridgeRoot 'SceneViewer.exe';Assert-CodexScenePath $exe
                $random=New-Object byte[] 24;$rng=[Security.Cryptography.RandomNumberGenerator]::Create();$rng.GetBytes($random);$rng.Dispose()
                $info=[Diagnostics.ProcessStartInfo]::new($exe,('--stdin-json --resolution 1280x720 --fps 15 "'+$scene.Assets+'" "'+$scene.Scene+'"'))
                $info.WorkingDirectory=$BridgeRoot;$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle='Hidden';$info.RedirectStandardInput=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
                $info.EnvironmentVariables['WP_STREAM_TOKEN']=([BitConverter]::ToString($random)).Replace('-','').ToLowerInvariant();$info.EnvironmentVariables['WP_STREAM_PORT']='0'
                $previous=[string]$theme.media.streamUrl;$player=[Diagnostics.Process]::Start($info);$errorLine=$player.StandardError.ReadLineAsync();$ready=$player.StandardOutput.ReadLineAsync()
                if(-not$ready.Wait(60000)-or$ready.Result-notmatch'^scene-viewer: stream ready (http://127\.0\.0\.1:[0-9]+/[a-f0-9]{48}/stream\.mp4)$'){
                    $detail='';for($j=0;$j-lt 8-and$errorLine-and$errorLine.IsCompleted;$j++){if($null-eq$errorLine.Result){break};$detail+=' '+$errorLine.Result;$errorLine=$player.StandardError.ReadLineAsync()}
                    throw ('Scene process did not publish a valid stream.'+$detail)
                }
                $streamUrl=$Matches[1]
                if(-not(Set-CodexRecoveredSceneUrl $themePath $scene $previous $streamUrl)){Stop-OwnedScene;Start-Sleep -Milliseconds 500;continue}
                $startedBrowser=$state.browserId;$startedScene=$scene.Scene;$lastError=$null;$failures=0
            }
            for($i=0;$i-lt 16-and$errorLine-and$errorLine.IsCompleted;$i++){if($null-eq$errorLine.Result){$errorLine=$null;break};$errorLine=$player.StandardError.ReadLineAsync()}
            Save-SceneStatus $true $state.browserId $theme.id $streamUrl
            Start-Sleep -Seconds 1
        }catch{
            try{[IO.File]::AppendAllText((Join-Path $SkinRoot 'scene-session-errors.log'),([DateTimeOffset]::Now.ToString('o')+' '+$_.Exception.Message+' '+$_.ScriptStackTrace+[Environment]::NewLine),[Text.UTF8Encoding]::new($false))}catch{}
            $lastError=$_.Exception.Message.Substring(0,[math]::Min(512,$_.Exception.Message.Length))
            try{Save-SceneStatus $false '' '' ''}catch{}
            if($phase-eq'session'){
                # Keep an owned stream through a short debugging-endpoint stall.
                # Only repeated identity failures end this controller's session.
                $misses++;if($misses-ge 3){break}
                Start-Sleep -Seconds ([math]::Pow(2,$misses));continue
            }
            Stop-OwnedScene;$failures++;if($failures-ge 3){break}
            Start-Sleep -Seconds ([math]::Min(10,[math]::Pow(2,[math]::Min($failures,3))))
        }
    }
}finally{Stop-OwnedScene;if($owns){try{Save-SceneStatus $false '' '' ''}catch{};$mutex.ReleaseMutex()};$mutex.Dispose()}
