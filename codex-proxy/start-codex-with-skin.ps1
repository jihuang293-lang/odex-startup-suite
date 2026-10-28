[CmdletBinding()]
param(
    [switch]$CheckOnly,
    [switch]$PrepareOnly,
    [switch]$ManagerOnly,
    [switch]$Minimized,
    [switch]$WithoutAnimation,
    [switch]$PreviewAnimation,
    [string]$ManagerPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'codex-dynamic-skin-trial\CodexDreamSkinManager.exe')
)

$ErrorActionPreference = 'Stop'
# Desktop launchers may inherit a module search path from another PowerShell
# runtime. Load the matching built-in utility module by its absolute path.
Import-Module ([IO.Path]::Combine($PSHOME, 'Modules', 'Microsoft.PowerShell.Utility', 'Microsoft.PowerShell.Utility.psd1')) -ErrorAction Stop
. (Join-Path $PSScriptRoot 'proxy-network.ps1')
. (Join-Path $PSScriptRoot 'dragon-animation.ps1')
$dragonPlayer = $null
$skinRoot = Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
$runtimeRoot = Join-Path $skinRoot 'manager-runtime\1.7.3'
$commonPath = Join-Path $runtimeRoot 'payload\scripts\common-windows.ps1'
$startPath = Join-Path $runtimeRoot 'payload\scripts\start-dream-skin.ps1'
$helperPath = Join-Path $PSScriptRoot 'skin-proxy-integration.ps1'
$expectedManagerHash = '46FC0B8D8367AC02CE9A3FEC37BA34B182D0482EAEEBC5AE2CD56903C128E936'
$expectedCommonHash = 'A6EA8E59633943EC49F2D41061882E84A7B2255C4B442944D55FB0DD36914FC9'
$marker = '# CODEX_LOCAL_PROXY_INTEGRATION_V1'
$hook = "`r`n$marker`r`n. '$($helperPath.Replace("'", "''"))'`r`n"

function Write-CombinedLaunchEvent {
    param([string]$Stage, [string]$Detail = '')
    if ($CheckOnly -or $PrepareOnly) { return }
    $line = '{0} {1} {2}{3}' -f [DateTimeOffset]::Now.ToString('o'), $Stage, $Detail, [Environment]::NewLine
    [IO.File]::AppendAllText((Join-Path $PSScriptRoot 'combined-launch.log'), $line, [Text.UTF8Encoding]::new($false))
}

function Get-CombinedStartDecision {
    param([object]$Status, [scriptblock]$ConfirmRestart)
    if ($Status.Running -and (-not $Status.RunningHasProxyFlag -or -not $Status.RunningHasDebugFlag -or $Status.NeedsWindowRecovery)) {
        if (-not (& $ConfirmRestart)) {
            return @{ Cancelled = $true; Parameters = @{} }
        }
        return @{ Cancelled = $false; Parameters = @{ RestartExisting = $true } }
    }
    return @{ Cancelled = $false; Parameters = @{ PromptRestart = $true } }
}

function Test-CombinedAnimationRequested {
    param([switch]$WithoutAnimation)
    # Closing the last Codex window can leave its main process alive.
    # A process already running must not suppress a desktop launch animation.
    return -not $WithoutAnimation
}

function Start-CombinedAnimation {
    param([switch]$Ready)
    try { return Start-CodexDragonAnimation -Ready:$Ready }
    catch {
        # Respect application-control enforcement. Never retry via a different
        # executable name, script host, or in-memory loading mechanism.
        Write-CombinedLaunchEvent -Stage 'dragon-unavailable' -Detail $_.ToString()
        return $null
    }
}

function Get-CombinedLaunchStatus {
    param([object]$Network)
    $package = Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1
    if (-not $package -or "$($package.SignatureKind)" -ine 'Store' -or $package.IsDevelopmentMode) {
        throw 'The official Store Codex package could not be validated.'
    }
    $exePath = Join-Path $package.InstallLocation 'app\ChatGPT.exe'
    $processes = @(Get-CimInstance Win32_Process -Filter "Name='ChatGPT.exe'" | Where-Object {
        $_.ExecutablePath -and $_.ExecutablePath.Equals($exePath, [StringComparison]::OrdinalIgnoreCase)
    })
    $mainProcesses = @($processes | Where-Object { $_.CommandLine -notmatch '(?:^|\s)--type=' })
    $proxyReady = $false
    $debugReady = $false
    foreach ($process in $mainProcesses) {
        if ($Network -and $process.CommandLine.Contains("--proxy-server=$($Network.Proxy)")) { $proxyReady = $true }
        if ($process.CommandLine -match '--remote-debugging-port=9335(?:\s|"|$)') { $debugReady = $true }
    }
    [pscustomobject]@{
        Executable = $exePath
        Proxy = if ($Network) { $Network.Proxy } else { $null }
        NoProxy = 'localhost,127.0.0.1,::1'
        DebugAddress = '127.0.0.1:9335'
        Running = $processes.Count -gt 0
        RunningHasProxyFlag = $proxyReady
        RunningHasDebugFlag = $debugReady
        Runtime = $runtimeRoot
        IntegrationPrepared = (Test-Path -LiteralPath $commonPath) -and
            ([IO.File]::ReadAllText($commonPath).EndsWith($hook, [StringComparison]::Ordinal))
    }
}

function Assert-LocalOrdinaryPath {
    param([string]$Path)
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "A launcher path is redirected: $current"
            }
        }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Install-CombinedLaunchHook {
    Assert-LocalOrdinaryPath -Path $commonPath
    Assert-LocalOrdinaryPath -Path $helperPath
    $bytes = [IO.File]::ReadAllBytes($commonPath)
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $commonPath).Hash
    $text = [IO.File]::ReadAllText($commonPath)
    if ($text.EndsWith($hook, [StringComparison]::Ordinal)) {
        $hookLength = [Text.Encoding]::UTF8.GetByteCount($hook)
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            $originalHash = [BitConverter]::ToString($sha256.ComputeHash($bytes, 0, $bytes.Length - $hookLength)).Replace('-', '')
        } finally { $sha256.Dispose() }
        if ($originalHash -cne $expectedCommonHash) { throw 'The original skin runtime was modified after integration.' }
        return
    }
    if ($hash -cne $expectedCommonHash) {
        throw 'The skin runtime has changed. Integration was not applied; review the new version first.'
    }
    $backupPath = $commonPath + '.before-codex-proxy'
    if (-not (Test-Path -LiteralPath $backupPath)) { [IO.File]::WriteAllBytes($backupPath, $bytes) }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $backupPath).Hash -cne $expectedCommonHash) {
        throw 'The original skin-runtime backup does not match the verified version.'
    }
    $extra = [Text.Encoding]::UTF8.GetBytes($hook)
    $patched = [byte[]]::new($bytes.Length + $extra.Length)
    [Array]::Copy($bytes, $patched, $bytes.Length)
    [Array]::Copy($extra, 0, $patched, $bytes.Length, $extra.Length)
    $temporary = $commonPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $swapBackup = $temporary + '.replaced'
    try {
        [IO.File]::WriteAllBytes($temporary, $patched)
        $tokens = $null; $errors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($temporary, [ref]$tokens, [ref]$errors)
        if ($errors.Count) { throw 'The combined runtime did not pass PowerShell syntax validation.' }
        # PowerShell 5.1 converts a null string argument to an empty path;
        # use a real temporary backup path with File.Replace instead.
        [IO.File]::Replace($temporary, $commonPath, $swapBackup)
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
        if (Test-Path -LiteralPath $swapBackup) { Remove-Item -LiteralPath $swapBackup -Force }
    }
}

try {
    Write-CombinedLaunchEvent -Stage 'launcher-started'
    if (-not (Test-Path -LiteralPath $managerPath)) { throw 'The installed skin manager was not found.' }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $managerPath).Hash -cne $expectedManagerHash) {
        throw 'The skin manager has changed. Verify the new version before using this integration.'
    }
    $network = if ($ManagerOnly) { $null } else { Get-CodexLocalProxy }
    if ($CheckOnly) { Get-CombinedLaunchStatus -Network $network | ConvertTo-Json -Depth 3; exit 0 }
    if ($PreviewAnimation) {
        $previewStatus = Get-CombinedLaunchStatus -Network $network
        if (-not $previewStatus.RunningHasProxyFlag -or -not $previewStatus.RunningHasDebugFlag) {
            throw 'Start Codex with proxy and skin before previewing the animation.'
        }
        $previewPlayer = Start-CodexDragonAnimation -Ready
        Write-CombinedLaunchEvent -Stage 'dragon-preview' -Detail ('pid={0}; current-app-preserved=True' -f $previewPlayer.Id)
        exit 0
    }
    if ($PrepareOnly) {
        Install-CombinedLaunchHook
        Get-CombinedLaunchStatus -Network $network | ConvertTo-Json -Depth 3
        exit 0
    }

    $manager = Get-Process -Name CodexDreamSkinManager -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -eq $managerPath } catch { $false }
    } | Select-Object -First 1
    if (-not $manager) {
        if ($network) { Set-CodexProxyEnvironment -Configuration $network }
        $launchOptions = @{FilePath=$managerPath; PassThru=$true}
        if ($Minimized -or $ManagerOnly) { $launchOptions.ArgumentList='--minimized'; $launchOptions.WindowStyle='Hidden' }
        $manager = Start-Process @launchOptions
        # The manager restores its embedded files during startup. Apply our
        # small, version-checked hook after its initial window is initialized.
        $deadline = [DateTime]::UtcNow.AddSeconds(25)
        $managerReady = $false
        do {
            Start-Sleep -Milliseconds 250
            $manager.Refresh()
            if ($manager.HasExited) { throw 'The skin manager exited before initialization completed.' }
            try { $managerReady = $manager.WaitForInputIdle(250) } catch { $managerReady = $false }
        } while ((-not $managerReady -or -not (Test-Path -LiteralPath $startPath)) -and [DateTime]::UtcNow -lt $deadline)
        if (-not $managerReady) { throw 'The skin manager did not finish initialization in time.' }
    }
    Install-CombinedLaunchHook
    if ($ManagerOnly) { exit 0 }

    $status = Get-CombinedLaunchStatus -Network $network
    if ($status.Running -and (Test-CombinedAnimationRequested -WithoutAnimation:$WithoutAnimation)) {
        $hiddenSession = Test-CodexWindowRecoveryNeeded -RuntimeRoot $runtimeRoot -SkinRoot $skinRoot
        $status | Add-Member -NotePropertyName NeedsWindowRecovery -NotePropertyValue $hiddenSession -Force
        Write-CombinedLaunchEvent -Stage 'window-preflight' -Detail ('hidden={0}; consent-before-cover=True' -f $hiddenSession)
    }
    Write-CombinedLaunchEvent -Stage 'before-skin-start' -Detail (
        'running={0}; proxy={1}; skin={2}' -f $status.Running, $status.RunningHasProxyFlag, $status.RunningHasDebugFlag)
    $decision = Get-CombinedStartDecision -Status $status -ConfirmRestart {
        Add-Type -AssemblyName System.Windows.Forms
        $messages = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'launcher-messages.json')) | ConvertFrom-Json
        $message = if ($status.NeedsWindowRecovery) { $messages.windowRestartMessage } else { $messages.restartMessage }
        $title = if ($status.NeedsWindowRecovery) { $messages.windowRestartTitle } else { $messages.restartTitle }
        $answer = [Windows.Forms.MessageBox]::Show($message, $title,
            [Windows.Forms.MessageBoxButtons]::YesNo, [Windows.Forms.MessageBoxIcon]::Question,
            [Windows.Forms.MessageBoxDefaultButton]::Button2)
        return $answer -eq [Windows.Forms.DialogResult]::Yes
    }
    if ($decision.Cancelled) {
        Write-CombinedLaunchEvent -Stage 'restart-declined'
        exit 0
    }
    if (-not $WithoutAnimation -and $status.RunningHasProxyFlag -and $status.RunningHasDebugFlag -and
        -not $status.NeedsWindowRecovery -and -not $decision.Parameters.RestartExisting -and
        (Test-CodexAppearanceReady -VerificationPath (Join-Path $skinRoot 'verify.log') -ThemePath (Join-Path $skinRoot 'active-theme\theme.json'))) {
        $dragonPlayer = Start-CombinedAnimation -Ready
        if ($dragonPlayer) { Write-CombinedLaunchEvent -Stage 'dragon-existing-ready-window' -Detail ('pid={0}; current-app-preserved=True' -f $dragonPlayer.Id) }
        exit 0
    }
    if (Test-CombinedAnimationRequested -WithoutAnimation:$WithoutAnimation) {
        $dragonPlayer = Start-CombinedAnimation
        if ($dragonPlayer) {
            Write-CombinedLaunchEvent -Stage 'dragon-started' -Detail ('pid={0}; awaiting-skin=True' -f $dragonPlayer.Id)
        }
    } else {
        Write-CombinedLaunchEvent -Stage 'dragon-disabled' -Detail 'WithoutAnimation=True'
    }
    Set-CodexProxyEnvironment -Configuration $network
    $env:PATH = (Join-Path $runtimeRoot 'node') + ';' + $env:PATH
    # Explicit restart is permitted only after the user chooses Yes above.
    # The runtime still verifies which installed app it may stop and relaunch.
    $startParameters = $decision.Parameters
    & $startPath @startParameters
    if (-not $?) { throw 'The skin startup failed; inspect the skin manager diagnostics.' }
    $afterStatus = Get-CombinedLaunchStatus -Network $network
    Write-CombinedLaunchEvent -Stage 'after-skin-start' -Detail (
        'running={0}; proxy={1}; skin={2}' -f $afterStatus.Running, $afterStatus.RunningHasProxyFlag, $afterStatus.RunningHasDebugFlag)
    if ($afterStatus.RunningHasProxyFlag -and $afterStatus.RunningHasDebugFlag) {
        if ($dragonPlayer) {
            Complete-CodexDragonStartup
            Write-CombinedLaunchEvent -Stage 'dragon-client-ready'
        }
    } elseif ($dragonPlayer) {
        throw 'Codex did not retain both proxy and skin settings; animation handoff was not released.'
    }
} catch {
    if ($CheckOnly -or $PrepareOnly) { Write-Error $_; exit 1 }
    Write-CombinedLaunchEvent -Stage 'launcher-error' -Detail ($_.ToString() + ' ' + $_.ScriptStackTrace)
    if ($dragonPlayer -and -not $dragonPlayer.HasExited) { $dragonPlayer.Kill() }
    Add-Type -AssemblyName System.Windows.Forms
    [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Codex proxy + skin', 'OK', 'Information')
    exit 1
}
