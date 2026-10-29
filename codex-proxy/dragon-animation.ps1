. (Join-Path $PSScriptRoot 'scene-recovery.ps1')
. (Join-Path $PSScriptRoot 'startup-tcp-tables.ps1')

function Resolve-CodexAnimationMedia {
    param([object]$Configuration, [string]$ThemePath, [string]$AnimationRoot)
    if ($Configuration.FollowSkin) {
        $theme = [IO.File]::ReadAllText($ThemePath) | ConvertFrom-Json
        $cachedMedia = $null
        if ($theme.media.type -eq 'scene' -and ([string]$theme.media.workshopId) -match '^[0-9]{1,20}$') {
            $scenePoster = Join-Path (Split-Path -Parent $AnimationRoot.TrimEnd('\','/')) ('codex-dynamic-skin-trial\scene-runtime\poster-cache\' + $theme.media.workshopId + '.jpg')
            if (Test-Path -LiteralPath $scenePoster -PathType Leaf) { $cachedMedia = $scenePoster }
        }
        if ($theme.media.proxy -and -not [IO.Path]::IsPathRooted([string]$theme.media.proxy)) {
            $skinRoot = [IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent $ThemePath))).TrimEnd('\')
            $candidate = [IO.Path]::GetFullPath((Join-Path $skinRoot $theme.media.proxy))
            if ($candidate.StartsWith($skinRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and
                (Test-Path -LiteralPath $candidate -PathType Leaf)) { $cachedMedia = $candidate }
        }
        if ($cachedMedia) {
            $mediaPath = $cachedMedia
        } elseif ($theme.media.source -eq 'wallpaper-engine-local') {
            $mediaPath = Join-Path (Join-Path $theme.media.workshopRoot ([string]$theme.media.workshopId)) $theme.media.relativePath
        } else {
            $mediaPath = Join-Path (Split-Path -Parent $ThemePath) $theme.image
        }
    } else {
        $mediaPath = if ($Configuration.CustomVideo) { [string]$Configuration.CustomVideo } else { [string]$Configuration.Video }
        if (-not [IO.Path]::IsPathRooted($mediaPath)) { $mediaPath = Join-Path $AnimationRoot $mediaPath }
    }
    $mediaPath = [IO.Path]::GetFullPath($mediaPath)
    if ([IO.Path]::GetExtension($mediaPath).ToLowerInvariant() -notin @('.mp4','.png','.jpg','.jpeg','.bmp')) {
        throw 'Choose an MP4 video or a PNG/JPEG/BMP image for the startup wallpaper.'
    }
    if (-not (Test-Path -LiteralPath $mediaPath -PathType Leaf)) {
        throw 'The selected startup wallpaper is unavailable. Check the current skin or choose another local file.'
    }
    return $mediaPath
}

function Test-CodexAppearanceReport {
    param([object]$Report,[object]$Theme)
    foreach ($target in @($Report.targets)) {
        $result = $target.result
        if($result.expectedRevision-and$result.revision-cne$result.expectedRevision){continue}
        if($result.expectedVersion-and$result.version-cne$result.expectedVersion){continue}
        if($null-ne$result.mediaPresent-and$result.mediaPresent-ne$true){continue}
        if($null-ne$result.mediaElementOpacity-and$result.mediaElementOpacity-ne'1'){continue}
        if($Theme.media.type-eq'scene'-and($result.mediaStream-ne$true-or$result.streamStats.status-ne'streaming'-or$result.streamStats.chunks-lt 1-or$result.streamStats.error)){continue}
        if ($result.installed -eq $true -and $result.stylePresent -eq $true -and
            $result.themeId -ceq $Theme.id -and $result.documentHidden -eq $false -and
            $result.mediaReady -eq $true -and $result.readiness.documentPass -eq $true -and
            $result.readiness.viewportPass -eq $true -and $result.readiness.structurePass -eq $true) { return $true }
    }
    return $false
}

function Test-CodexAppearanceReady {
    param([string]$VerificationPath, [string]$ThemePath)
    try {
        if (-not (Test-Path -LiteralPath $VerificationPath) -or (Get-Item -LiteralPath $VerificationPath).Length -gt 1048576) { return $false }
        $text = [IO.File]::ReadAllText($VerificationPath)
        $first = $text.IndexOf('{'); $last = $text.LastIndexOf('}')
        if ($first -lt 0 -or $last -le $first) { return $false }
        $report = $text.Substring($first, $last - $first + 1) | ConvertFrom-Json
        $theme = [IO.File]::ReadAllText($ThemePath) | ConvertFrom-Json
        return Test-CodexAppearanceReport -Report $report -Theme $theme
    } catch { return $false }
    return $false
}

function Get-CodexHandoffWindow {
    param([string]$ExecutablePath,[switch]$TryActivate)
    try {
        $package=Get-AppxPackage -Name OpenAI.Codex|Sort-Object Version -Descending|Select-Object -First 1
        if(-not$package-or"$($package.SignatureKind)"-ine'Store'-or$package.IsDevelopmentMode){return $null}
        $official=Join-Path $package.InstallLocation 'app\ChatGPT.exe'
        if($ExecutablePath-and$ExecutablePath-ine$official){return $null}
        $processes=@(Get-CimInstance Win32_Process -Filter "Name='ChatGPT.exe'"|Where-Object {$_.ExecutablePath-ieq$official})
        if(-not$processes.Count){return $null}
        if($TryActivate){
            # Same exact-package AppActivate operation as the upstream helper.
            # It neither starts another client nor closes a background session.
            $shell=New-Object -ComObject WScript.Shell
            foreach($item in $processes){try{if($shell.AppActivate([int]$item.ProcessId)){break}}catch{}}
            Start-Sleep -Milliseconds 300
        }
        $assemblyPath=Join-Path (Split-Path -Parent $PSScriptRoot) 'dragon-codex-boot\CodexCinematicBoot.exe'
        Assert-CodexScenePath $assemblyPath
        $assembly=[Reflection.Assembly]::LoadFrom($assemblyPath)
        $native=$assembly.GetType('DragonCodexBoot.Native',$true)
        $config=[Activator]::CreateInstance($assembly.GetType('DragonCodexBoot.Config',$true))
        $config.ProcessNames=@('ChatGPT');$config.ProcessPathContains=$official
        $handle=$native.GetMethod('FindApp').Invoke($null,@($config))
        if($handle-eq[IntPtr]::Zero-or-not$native.GetMethod('IsWindowVisible').Invoke($null,@($handle))-or$native.GetMethod('IsIconic').Invoke($null,@($handle))){return $null}
        $nativeArguments=[object[]]@($handle,[uint32]0)
        $null=$native.GetMethod('GetWindowThreadProcessId').Invoke($null,$nativeArguments)
        $owner=[int]$nativeArguments[1]
        $matching=@($processes|Where-Object {$_.ProcessId-eq$owner})
        if($matching.Count-ne 1){return $null}
        $cloakArguments=[object[]]@($handle,14,0,4)
        $cloakResult=$native.GetMethod('DwmGetWindowAttribute').Invoke($null,$cloakArguments)
        if($cloakResult-eq 0-and[int]$cloakArguments[2]-ne 0){return $null}
        $bounds=$native.GetMethod('ClientBounds').Invoke($null,@($handle))
        if($bounds.Width-lt 450-or$bounds.Height-lt 280-or$bounds.Width-gt 7680-or$bounds.Height-gt 4320){return $null}
        return [pscustomobject]@{Handle=$handle.ToInt64();ProcessId=$owner;Executable=$official;Width=$bounds.Width;Height=$bounds.Height}
    }catch{return $null}
}

function Get-CodexLiveRendererReport {
    param([string]$Node,[string]$Injector,[string]$BrowserId,[string]$ThemeDirectory,[ValidateRange(500,5000)][int]$TimeoutMilliseconds=5000)
    $info=[Diagnostics.ProcessStartInfo]::new($Node,('"'+$Injector+'" --verify --port 9335 --browser-id '+$BrowserId+' --theme-dir "'+$ThemeDirectory+'" --timeout-ms '+$TimeoutMilliseconds))
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle='Hidden';$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $process=[Diagnostics.Process]::Start($info)
    try{
        $output=$process.StandardOutput.ReadToEndAsync();$errors=$process.StandardError.ReadToEndAsync()
        if(-not$process.WaitForExit($TimeoutMilliseconds+4000)){$process.Kill();return $null}
        # Exit 2 is a structured verification miss. Its fresh report can still
        # prove the wallpaper and shell ready while an unrelated layout check
        # fails. Do not wait for all upstream checks to authorize this cover.
        if($process.ExitCode-notin@(0,2)-or-not$output.Wait(1000)-or$output.Result.Length-gt 1048576){return $null}
        $report=$output.Result|ConvertFrom-Json
        if($report.mode-cne'verify'-or$report.port-ne 9335-or$null-eq$report.targets){return $null}
        return $report
    }finally{$process.Dispose()}
}

function Test-CodexLiveAppearanceReady {
    param([string]$SkinRoot,[string]$RuntimeRoot,[ValidateRange(500,5000)][int]$TimeoutMilliseconds=5000)
    try {
        $state=Read-CodexSceneJson (Join-Path $SkinRoot 'state.json')
        if($state.port-ne 9335-or$state.browserId-notmatch'^[a-fA-F0-9-]{32,36}$'){return $false}
        $node=Join-Path $RuntimeRoot 'node\node.exe';$injector=Join-Path $RuntimeRoot 'payload\scripts\injector.mjs'
        if($state.nodePath-ine$node-or$state.injectorPath-ine$injector){return $false}
        Assert-CodexScenePath $node;Assert-CodexScenePath $injector
        $window=Get-CodexHandoffWindow -ExecutablePath $state.codexExe
        if(-not$window){return $false}
        $owners=@(Get-CodexStartupTcpListeners -Port 9335|Where-Object {$_.LocalAddress-eq'127.0.0.1'})
        if(-not$owners.Count){return $false}
        foreach($owner in $owners){
            $process=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$owner.OwningProcess)
            if(-not$process-or$process.ExecutablePath-ine$window.Executable){return $false}
        }
        $themePath=Join-Path $SkinRoot 'active-theme\theme.json';$theme=Read-CodexSceneJson $themePath
        $report=Get-CodexLiveRendererReport -Node $node -Injector $injector -BrowserId $state.browserId -ThemeDirectory (Join-Path $SkinRoot 'active-theme') -TimeoutMilliseconds $TimeoutMilliseconds
        if(-not(Test-CodexAppearanceReport -Report $report -Theme $theme)){return $false}
        $currentState=Read-CodexSceneJson (Join-Path $SkinRoot 'state.json');$currentTheme=Read-CodexSceneJson $themePath
        $currentWindow=Get-CodexHandoffWindow -ExecutablePath $state.codexExe
        return $currentState.browserId-ceq$state.browserId-and$currentTheme.id-ceq$theme.id-and$currentTheme.media.streamUrl-ceq$theme.media.streamUrl-and$currentWindow-and$currentWindow.Handle-eq$window.Handle-and$currentWindow.ProcessId-eq$window.ProcessId
    }catch{return $false}
}

function Write-CodexReadinessEvent {
    param([string]$Stage,[string]$Detail='')
    try {
        $path=Join-Path $PSScriptRoot 'startup-readiness.log'
        Assert-CodexScenePath $path
        if((Test-Path -LiteralPath $path)-and(Get-Item -LiteralPath $path).Length-gt 65536){[IO.File]::WriteAllText($path,'')}
        [IO.File]::AppendAllText($path,('{0} {1} {2}{3}'-f[DateTimeOffset]::Now.ToString('o'),$Stage,$Detail,[Environment]::NewLine))
    }catch{}
}

function Test-CodexStartupReadyPath {
    param([string]$ReadyFile)
    $root=Join-Path (Split-Path -Parent $PSScriptRoot) 'dragon-codex-boot'
    $path=[IO.Path]::GetFullPath($ReadyFile)
    return (Split-Path -Parent $path)-ieq$root-and[IO.Path]::GetFileName($path)-cmatch'^client-ready\.[a-f0-9]{32}\.flag$'
}

function Get-CodexStartupPlayer {
    param([int]$PlayerId,[long]$StartedAtTicks)
    try {
        $player=Get-Process -Id $PlayerId -ErrorAction Stop
        $exe=Join-Path (Split-Path -Parent $PSScriptRoot) 'dragon-codex-boot\CodexCinematicBoot.exe'
        if($player.HasExited-or$player.Path-ine$exe-or$player.StartTime.ToUniversalTime().Ticks-ne$StartedAtTicks){return $null}
        return $player
    }catch{return $null}
}

function Invoke-CodexStartupReadinessMonitor {
    param([int]$PlayerId,[long]$StartedAtTicks,[string]$ReadyFile,[ValidateRange(1,180)][int]$TimeoutSeconds=125)
    if(-not(Test-CodexStartupReadyPath $ReadyFile)){throw 'Invalid animation session readiness path.'}
    Assert-CodexScenePath $ReadyFile
    $skinRoot=Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
    $runtimeRoot=Join-Path $skinRoot 'manager-runtime\1.7.3'
    $timer=[Diagnostics.Stopwatch]::StartNew();$released=$false
    Write-CodexReadinessEvent 'monitor-started' ('player='+$PlayerId)
    try {
        while($timer.Elapsed.TotalSeconds-lt$TimeoutSeconds){
            if(-not(Get-CodexStartupPlayer $PlayerId $StartedAtTicks)){return}
            if(-not$released){
                if(Test-Path -LiteralPath $ReadyFile){$released=$true}
                elseif(Test-CodexLiveAppearanceReady -SkinRoot $skinRoot -RuntimeRoot $runtimeRoot -TimeoutMilliseconds 800){
                    # Recheck the owned player after the potentially slow CDP
                    # probe. Esc/exit must never release a later animation.
                    if(-not(Get-CodexStartupPlayer $PlayerId $StartedAtTicks)){return}
                    Assert-CodexScenePath $ReadyFile
                    [IO.File]::WriteAllText($ReadyFile,[DateTimeOffset]::Now.ToString('o'))
                    $released=$true
                    Write-CodexReadinessEvent 'appearance-ready-independent' ('player={0}; elapsed-seconds={1:F1}; upstream-startup-may-still-be-running=True'-f$PlayerId,$timer.Elapsed.TotalSeconds)
                }
            }
            Start-Sleep -Milliseconds 500
        }
        Write-CodexReadinessEvent 'monitor-deadline' ('player={0}; released={1}; no-forced-ready=True'-f$PlayerId,$released)
    }finally{
        # This filename is unique to this player. Remove only its exact flag
        # after that same player exits; never remove another run's signal.
        if(-not(Get-CodexStartupPlayer $PlayerId $StartedAtTicks)){
            if(Test-Path -LiteralPath $ReadyFile){Assert-CodexScenePath $ReadyFile;Remove-Item -LiteralPath $ReadyFile -Force}
        }
        Write-CodexReadinessEvent 'monitor-stopped' ('player='+$PlayerId)
    }
}

function Start-CodexStartupReadinessMonitor {
    param([object]$Player,[string]$ReadyFile,[int]$TimeoutSeconds=125)
    if(-not(Test-CodexStartupReadyPath $ReadyFile)){throw 'Invalid animation session readiness path.'}
    $worker=Join-Path $PSScriptRoot 'watch-startup-readiness.ps1';Assert-CodexScenePath $worker
    $powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $ticks=$Player.StartTime.ToUniversalTime().Ticks
    $arguments='-NoProfile -ExecutionPolicy RemoteSigned -File "{0}" -PlayerId {1} -StartedAtTicks {2} -ReadyFile "{3}" -TimeoutSeconds {4}'-f$worker,$Player.Id,$ticks,$ReadyFile,$TimeoutSeconds
    return Start-Process -FilePath $powershell -ArgumentList $arguments -WindowStyle Hidden -PassThru
}

function Test-CodexWindowRecoveryNeeded {
    param([string]$RuntimeRoot, [string]$SkinRoot)
    try {
        $state = [IO.File]::ReadAllText((Join-Path $SkinRoot 'state.json')) | ConvertFrom-Json
        if ($state.browserId -notmatch '^[0-9a-fA-F-]{32,36}$' -or [int]$state.port -ne 9335) { return $false }
        $node = Join-Path $RuntimeRoot 'node\node.exe'
        $injector = Join-Path $RuntimeRoot 'payload\scripts\injector.mjs'
        $output = & $node $injector --verify --port 9335 --browser-id $state.browserId --theme-dir (Join-Path $SkinRoot 'active-theme') --timeout-ms 3000 2>$null
        $raw = $output -join "`n"; $first = $raw.IndexOf('{'); $last = $raw.LastIndexOf('}')
        if ($first -lt 0 -or $last -le $first) { return $false }
        $report = $raw.Substring($first, $last-$first+1) | ConvertFrom-Json
        $targets = @($report.targets)
        if ($targets.Count -eq 0) { return $false }
        foreach ($target in $targets) { if ($target.result.documentHidden -ne $true) { return $false } }
        return $true
    } catch { return $false }
}

function Start-CodexDragonAnimation {
    param([switch]$Ready)
    $root = Join-Path (Split-Path -Parent $PSScriptRoot) 'dragon-codex-boot'
    $exe = Join-Path $root 'CodexCinematicBoot.exe'
    $flag = Join-Path $root 'client-ready.flag'
    $visibleFlag = Join-Path $root 'overlay-visible.flag'
    $config = [IO.File]::ReadAllText((Join-Path $root 'launcher.json')) | ConvertFrom-Json
    if ($config.ReadyFile -eq 'client-ready.flag') { $config.ReadyFile = $flag }
    if ($config.LaunchClient -ne $false -or $config.AutoReplaceEntrypoints -ne $false -or
        $config.ScanAllLocalDrives -ne $false -or $config.ReadyFile -ne $flag) {
        throw 'Dragon must use the existing proxy/skin launcher and must not replace other shortcuts.'
    }
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Startup wallpaper player is missing.' }
    $themePath = Join-Path $env:LOCALAPPDATA 'CodexDreamSkin\active-theme\theme.json'
    $bridgeRoot=Join-Path (Split-Path -Parent $PSScriptRoot) 'codex-dynamic-skin-trial\scene-runtime'
    if($config.FollowSkin){$null=Repair-CodexScenePoster -ThemePath $themePath -BridgeRoot $bridgeRoot}
    if ($Ready -and -not (Test-CodexLiveAppearanceReady -SkinRoot (Join-Path $env:LOCALAPPDATA 'CodexDreamSkin') -RuntimeRoot (Join-Path $env:LOCALAPPDATA 'CodexDreamSkin\manager-runtime\1.7.3'))) {
        throw 'Codex content and wallpaper are not verified ready for the animation preview yet.'
    }
    $config.Video = Resolve-CodexAnimationMedia -Configuration $config -ThemePath $themePath -AnimationRoot $root
    if (Test-Path -LiteralPath $flag) { Remove-Item -LiteralPath $flag -Force }
    if (Test-Path -LiteralPath $visibleFlag) { Remove-Item -LiteralPath $visibleFlag -Force }
    # Each animation owns its signal. A slow previous launcher cannot release
    # a new cover by writing the former global client-ready.flag.
    $flag=Join-Path $root ('client-ready.'+[Guid]::NewGuid().ToString('N')+'.flag')
    $config.ReadyFile=$flag
    [IO.File]::WriteAllText((Join-Path $root 'launcher.runtime.json'), ($config | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    if ($Ready) { [IO.File]::WriteAllText($flag, [DateTimeOffset]::Now.ToString('o')) }
    # This is the visible animation explicitly requested by the user.
    $player = Start-Process -FilePath $exe -ArgumentList ('--runtime-config --ready-file "'+$flag+'"') -WorkingDirectory $root -WindowStyle Normal -PassThru
    if ($config.TransitionMode -eq 'Cinematic') {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while (-not (Test-Path -LiteralPath $visibleFlag)) {
            $player.Refresh()
            if ($player.HasExited) { throw 'The startup animation exited before its loading cover was visible.' }
            if ([DateTime]::UtcNow -ge $deadline) { $player.Kill(); throw 'The startup animation did not display its loading cover in time.' }
            Start-Sleep -Milliseconds 50
        }
    }
    $player|Add-Member -NotePropertyName StartupReadyFile -NotePropertyValue $flag -Force
    try {
        $monitor=Start-CodexStartupReadinessMonitor -Player $player -ReadyFile $flag -TimeoutSeconds ([Math]::Min(180,[int]$config.MaxWaitSeconds+20))
        $player|Add-Member -NotePropertyName StartupMonitorId -NotePropertyValue $monitor.Id -Force
    }catch{Write-CodexReadinessEvent 'monitor-unavailable' ('player='+$player.Id+'; synchronous-completion-retained=True')}
    return $player
}

function Complete-CodexDragonStartup {
    param([object]$Player)
    $root = Join-Path (Split-Path -Parent $PSScriptRoot) 'dragon-codex-boot'
    $skinRoot = Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
    if($Player){
        $Player.Refresh()
        if($Player.HasExited){return $false}
        $flag=[string]$Player.StartupReadyFile
        if(-not(Test-CodexStartupReadyPath $flag)){throw 'Animation readiness belongs to a different launch.'}
        if(Test-Path -LiteralPath $flag){return $true}
    }else{throw 'A matching animation player is required to complete startup.'}
    if (-not (Test-CodexLiveAppearanceReady -SkinRoot $skinRoot -RuntimeRoot (Join-Path $skinRoot 'manager-runtime\1.7.3'))) {
        if($Player.StartupMonitorId){return $false}
        throw 'The Codex interface and current wallpaper have not passed appearance readiness checks.'
    }
    Assert-CodexScenePath $flag
    [IO.File]::WriteAllText($flag, [DateTimeOffset]::Now.ToString('o'))
    return $true
}
