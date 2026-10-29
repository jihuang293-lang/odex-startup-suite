$ErrorActionPreference = 'Stop'
Import-Module ([IO.Path]::Combine($PSHOME,'Modules','Microsoft.PowerShell.Utility','Microsoft.PowerShell.Utility.psd1'))
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'start-codex-with-skin.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Launcher syntax failed'}
foreach($name in @('Get-CombinedStartDecision','Test-CombinedAnimationRequested','Start-CombinedAnimation')) {
    $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    . ([scriptblock]::Create($function.Extent.Text))
}
$entry=@($ast.EndBlock.Statements | Where-Object {$_ -is [Management.Automation.Language.TryStatementAst]})
if($entry.Count -ne 1){throw 'Expected the main launcher entry block'}
$entryCode=[scriptblock]::Create($entry[0].Extent.Text)
$fixture=Join-Path $PSScriptRoot 'qa-animation-entry'
$null=New-Item -ItemType Directory -Path $fixture -Force
$startPath=Join-Path $fixture 'mock-skin.ps1'
[IO.File]::WriteAllText($startPath,'param([switch]$RestartExisting,[switch]$PromptRestart) $global:AnimationEntryTestEvents.Add("skin-applied"); if($RestartExisting){throw "Unexpected restart"}')
$managerPath=$startPath;$expectedManagerHash='verified-fixture';$runtimeRoot=$fixture;$skinRoot=$fixture
function Get-FileHash {param($Algorithm,$LiteralPath) [pscustomobject]@{Hash='verified-fixture'}}
function Get-CodexLocalProxy {[pscustomobject]@{Proxy='http://127.0.0.1:7897';NoProxy='localhost'}}
function Get-Process {param($Name,$ErrorAction) [pscustomobject]@{Path=$managerPath}}
function Install-CombinedLaunchHook {}
function Install-CodexColdStartPatch {param($RuntimeRoot)}
function Get-CombinedLaunchStatus {param($Network) $script:Statuses.Dequeue()}
function Set-CodexProxyEnvironment {param($Configuration)}
function Write-CombinedLaunchEvent {param($Stage,$Detail) if($Stage -eq 'launcher-error'){throw $Detail}; $script:Events.Add($Stage)}
function Start-CodexDragonAnimation {param([switch]$Ready) $script:Events.Add('animation-created');if($case -eq 'blocked'){throw 'Application control blocked test fixture'};[pscustomobject]@{Id=123;HasExited=($case-ne'monitor-pending')} }
function Complete-CodexDragonStartup {param($Player) if($case-in@('monitor-pending','closed-after-early-handoff')){return $false};$script:Events.Add('ready-signal');return $true}
function Test-CodexWindowRecoveryNeeded {param($RuntimeRoot,$SkinRoot) $false}
# Deliberately stale positive cache: it must never authorize the warm fast path.
function Test-CodexAppearanceReady {param($VerificationPath,$ThemePath) $true}
function Test-CodexLiveAppearanceReady {param($SkinRoot,$RuntimeRoot) $false}
function Get-CodexHandoffWindow {param($ExecutablePath,[switch]$TryActivate) if($case-eq'no-native-window'){if($TryActivate){$script:Events.Add('activation-attempted')};return $null};[pscustomobject]@{Handle=123;ProcessId=456}}
$CheckOnly=$PrepareOnly=$ManagerOnly=$Minimized=$PreviewAnimation=$false
$savedPath=$env:PATH
try {
    foreach($case in @('already-running','fresh-start','disabled','blocked','monitor-pending','closed-after-early-handoff')) {
        $script:Events=New-Object 'Collections.Generic.List[string]'
        $global:AnimationEntryTestEvents=$script:Events
        $script:Statuses=New-Object 'Collections.Generic.Queue[object]'
        $script:Statuses.Enqueue([pscustomobject]@{Running=($case -ne 'fresh-start');RunningHasProxyFlag=$true;RunningHasDebugFlag=$true})
        $script:Statuses.Enqueue([pscustomobject]@{Running=$true;RunningHasProxyFlag=$true;RunningHasDebugFlag=$true})
        $WithoutAnimation=($case -eq 'disabled');$dragonPlayer=$null
        . $entryCode
        $created=@($script:Events | Where-Object {$_ -eq 'animation-created'}).Count
        if($created -ne [int](-not $WithoutAnimation)){throw "Wrong animation behavior: $case"}
        if(-not $WithoutAnimation -and $case -notin @('blocked','monitor-pending','closed-after-early-handoff') -and $script:Events.IndexOf('ready-signal') -le $script:Events.IndexOf('skin-applied')){throw 'Synchronous fallback must not release unverified readiness'}
        if($case-eq'monitor-pending'-and($script:Events-contains'ready-signal'-or$script:Events-notcontains'dragon-readiness-monitor-pending')){throw 'A pending monitor must remain independent without claiming readiness'}
        if($case-eq'closed-after-early-handoff'-and($script:Events-contains'ready-signal'-or$script:Events-notcontains'dragon-cover-already-closed')){throw 'An exited player must never be signalled ready again'}
        if($case -eq 'blocked' -and ($script:Events -notcontains 'dragon-unavailable' -or $script:Events -contains 'ready-signal' -or $script:Events -notcontains 'skin-applied')){throw 'A policy-blocked animation must preserve normal skin startup and never claim animation readiness'}
        if($script:Events -contains 'launcher-error'){throw 'Main entry failed'}
    }
    $case='no-native-window';$script:Events=New-Object 'Collections.Generic.List[string]';$global:AnimationEntryTestEvents=$script:Events
    $script:Statuses=New-Object 'Collections.Generic.Queue[object]'
    $script:Statuses.Enqueue([pscustomobject]@{Running=$true;RunningHasProxyFlag=$true;RunningHasDebugFlag=$true})
    $WithoutAnimation=$false;$dragonPlayer=$null;$preserved=$false
    try{. $entryCode}catch{if($_.Exception.Message-notlike '*no visible window*'){throw};$preserved=$true}
    if(-not$preserved-or$script:Events-notcontains'activation-attempted'-or$script:Events-notcontains'window-unavailable-preserved'-or$script:Events-contains'animation-created'-or$script:Events-contains'skin-applied'){throw 'A background-only session must attempt activation, preserve the app and never create a desktop cover.'}
} finally {$env:PATH=$savedPath}
'PASS: real launcher entry block creates animation for background-running and fresh sessions, waits for skin readiness, and honors explicit disable; no applications started or stopped.'
