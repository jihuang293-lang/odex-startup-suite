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
function Get-CombinedLaunchStatus {param($Network) $script:Statuses.Dequeue()}
function Set-CodexProxyEnvironment {param($Configuration)}
function Write-CombinedLaunchEvent {param($Stage,$Detail) if($Stage -eq 'launcher-error'){throw $Detail}; $script:Events.Add($Stage)}
function Start-CodexDragonAnimation {param([switch]$Ready) $script:Events.Add('animation-created');if($case -eq 'blocked'){throw 'Application control blocked test fixture'};[pscustomobject]@{Id=123;HasExited=$true} }
function Complete-CodexDragonStartup {$script:Events.Add('ready-signal')}
function Test-CodexWindowRecoveryNeeded {param($RuntimeRoot,$SkinRoot) $false}
function Test-CodexAppearanceReady {param($VerificationPath,$ThemePath) $false}
$CheckOnly=$PrepareOnly=$ManagerOnly=$Minimized=$PreviewAnimation=$false
$savedPath=$env:PATH
try {
    foreach($case in @('already-running','fresh-start','disabled','blocked')) {
        $script:Events=New-Object 'Collections.Generic.List[string]'
        $global:AnimationEntryTestEvents=$script:Events
        $script:Statuses=New-Object 'Collections.Generic.Queue[object]'
        $script:Statuses.Enqueue([pscustomobject]@{Running=($case -ne 'fresh-start');RunningHasProxyFlag=$true;RunningHasDebugFlag=$true})
        $script:Statuses.Enqueue([pscustomobject]@{Running=$true;RunningHasProxyFlag=$true;RunningHasDebugFlag=$true})
        $WithoutAnimation=($case -eq 'disabled');$dragonPlayer=$null
        . $entryCode
        $created=@($script:Events | Where-Object {$_ -eq 'animation-created'}).Count
        if($created -ne [int](-not $WithoutAnimation)){throw "Wrong animation behavior: $case"}
        if(-not $WithoutAnimation -and $case -ne 'blocked' -and $script:Events.IndexOf('ready-signal') -le $script:Events.IndexOf('skin-applied')){throw 'Readiness was released before the skin completed'}
        if($case -eq 'blocked' -and ($script:Events -notcontains 'dragon-unavailable' -or $script:Events -contains 'ready-signal' -or $script:Events -notcontains 'skin-applied')){throw 'A policy-blocked animation must preserve normal skin startup and never claim animation readiness'}
        if($script:Events -contains 'launcher-error'){throw 'Main entry failed'}
    }
} finally {$env:PATH=$savedPath}
'PASS: real launcher entry block creates animation for background-running and fresh sessions, waits for skin readiness, and honors explicit disable; no applications started or stopped.'
