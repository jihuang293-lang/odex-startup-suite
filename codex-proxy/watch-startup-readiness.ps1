[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][int]$PlayerId,
    [Parameter(Mandatory=$true)][long]$StartedAtTicks,
    [Parameter(Mandatory=$true)][string]$ReadyFile,
    [ValidateRange(1,180)][int]$TimeoutSeconds=125
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dragon-animation.ps1')
try {
    Invoke-CodexStartupReadinessMonitor -PlayerId $PlayerId -StartedAtTicks $StartedAtTicks -ReadyFile $ReadyFile -TimeoutSeconds $TimeoutSeconds
}catch{
    Write-CodexReadinessEvent 'monitor-error' ('player='+$PlayerId+'; no-forced-ready=True')
    exit 1
}
