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

function Test-CodexAppearanceReady {
    param([string]$VerificationPath, [string]$ThemePath)
    try {
        if (-not (Test-Path -LiteralPath $VerificationPath) -or (Get-Item -LiteralPath $VerificationPath).Length -gt 1048576) { return $false }
        $text = [IO.File]::ReadAllText($VerificationPath)
        $first = $text.IndexOf('{'); $last = $text.LastIndexOf('}')
        if ($first -lt 0 -or $last -le $first) { return $false }
        $report = $text.Substring($first, $last - $first + 1) | ConvertFrom-Json
        $theme = [IO.File]::ReadAllText($ThemePath) | ConvertFrom-Json
        foreach ($target in @($report.targets)) {
            $result = $target.result
            if ($result.installed -eq $true -and $result.stylePresent -eq $true -and
                $result.themeId -ceq $theme.id -and $result.documentHidden -eq $false -and
                $result.mediaReady -eq $true -and $result.readiness.documentPass -eq $true -and
                $result.readiness.viewportPass -eq $true -and $result.readiness.structurePass -eq $true) { return $true }
        }
    } catch { return $false }
    return $false
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
    if ($Ready -and -not (Test-CodexAppearanceReady -VerificationPath (Join-Path $env:LOCALAPPDATA 'CodexDreamSkin\verify.log') -ThemePath $themePath)) {
        throw 'Codex content and wallpaper are not verified ready for the animation preview yet.'
    }
    $config.Video = Resolve-CodexAnimationMedia -Configuration $config -ThemePath $themePath -AnimationRoot $root
    [IO.File]::WriteAllText((Join-Path $root 'launcher.runtime.json'), ($config | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $flag) { Remove-Item -LiteralPath $flag -Force }
    if (Test-Path -LiteralPath $visibleFlag) { Remove-Item -LiteralPath $visibleFlag -Force }
    if ($Ready) { [IO.File]::WriteAllText($flag, [DateTimeOffset]::Now.ToString('o')) }
    # This is the visible animation explicitly requested by the user.
    $player = Start-Process -FilePath $exe -ArgumentList '--runtime-config' -WorkingDirectory $root -WindowStyle Normal -PassThru
    if ($config.TransitionMode -eq 'Cinematic') {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while (-not (Test-Path -LiteralPath $visibleFlag)) {
            $player.Refresh()
            if ($player.HasExited) { throw 'The startup animation exited before its loading cover was visible.' }
            if ([DateTime]::UtcNow -ge $deadline) { $player.Kill(); throw 'The startup animation did not display its loading cover in time.' }
            Start-Sleep -Milliseconds 50
        }
    }
    return $player
}

function Complete-CodexDragonStartup {
    $root = Join-Path (Split-Path -Parent $PSScriptRoot) 'dragon-codex-boot'
    $skinRoot = Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
    if (-not (Test-CodexAppearanceReady -VerificationPath (Join-Path $skinRoot 'verify.log') -ThemePath (Join-Path $skinRoot 'active-theme\theme.json'))) {
        throw 'The Codex interface and current wallpaper have not passed appearance readiness checks.'
    }
    [IO.File]::WriteAllText((Join-Path $root 'client-ready.flag'), [DateTimeOffset]::Now.ToString('o'))
}
