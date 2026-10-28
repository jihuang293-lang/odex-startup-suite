$ErrorActionPreference = 'Stop'
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'start-codex-with-skin.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }
$functionAst = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Get-CombinedStartDecision'
}, $true)
if (-not $functionAst) { throw 'Restart decision function missing.' }
. ([scriptblock]::Create($functionAst.Extent.Text))

foreach ($proxy in @($true, $false)) {
    foreach ($skin in @($true, $false)) {
        $status = [pscustomobject]@{Running=$true;RunningHasProxyFlag=$proxy;RunningHasDebugFlag=$skin}
        if ($proxy -and $skin) {
            $decision = Get-CombinedStartDecision -Status $status -ConfirmRestart { throw 'Unnecessary restart confirmation.' }
            if ($decision.Cancelled -or $decision.Parameters.ContainsKey('RestartExisting')) { throw 'A healthy app was restarted.' }
        } else {
            $declined = Get-CombinedStartDecision -Status $status -ConfirmRestart { $false }
            if (-not $declined.Cancelled -or $declined.Parameters.Count) { throw 'Declining restart did not preserve the running app.' }
            $accepted = Get-CombinedStartDecision -Status $status -ConfirmRestart { $true }
            if ($accepted.Cancelled -or -not $accepted.Parameters.RestartExisting) { throw 'Accepted restart did not repair missing settings.' }
        }
    }
}
$closed = [pscustomobject]@{Running=$false;RunningHasProxyFlag=$false;RunningHasDebugFlag=$false}
$decision = Get-CombinedStartDecision -Status $closed -ConfirmRestart { throw 'Closed app does not need confirmation.' }
if ($decision.Cancelled -or $decision.Parameters.ContainsKey('RestartExisting') -or -not $decision.Parameters.PromptRestart) {
    throw 'A closed-app launch could silently stop an app opened concurrently.'
}
$null = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'launcher-messages.json')) | ConvertFrom-Json
$hidden=[pscustomobject]@{Running=$true;RunningHasProxyFlag=$true;RunningHasDebugFlag=$true;NeedsWindowRecovery=$true}
if(-not (Get-CombinedStartDecision -Status $hidden -ConfirmRestart {$true}).Parameters.RestartExisting){throw 'Hidden-window recovery must resolve consent before showing a full-screen cover'}
if(-not (Get-CombinedStartDecision -Status $hidden -ConfirmRestart {$false}).Cancelled){throw 'Hidden-session restart refusal must be respected'}
'PASS: missing proxy/skin, accepted and declined restart, healthy app, and concurrent-launch consent; no apps started or stopped.'
