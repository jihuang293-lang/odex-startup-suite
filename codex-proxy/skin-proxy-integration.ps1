# Loaded after the original Dream Skin common functions. Keep its verified
# Store executable and lifecycle checks; change only how the child is launched.
. (Join-Path $PSScriptRoot 'proxy-network.ps1')
. (Join-Path $PSScriptRoot 'scene-recovery.ps1')
. (Join-Path $PSScriptRoot 'startup-tcp-tables.ps1')
$env:DREAMSKIN_COMPAT_PROFILE='0'

function Get-DreamSkinPortListeners {
    param([int]$Port)
    return @(Get-CodexStartupTcpListeners -Port $Port)
}

if (-not (Get-Variable -Name CodexProxyOriginalDirectLaunch -Scope Script -ErrorAction SilentlyContinue)) {
    $originalDirect = Get-Item -LiteralPath 'Function:\Start-DreamSkinCodexDirect' -ErrorAction Stop
    $script:CodexProxyOriginalDirectLaunch = $originalDirect.ScriptBlock
}

function Start-DreamSkinCodexDirect {
    param(
        [Parameter(Mandatory=$true)][object]$Codex,
        [Parameter(Mandatory=$true)][AllowEmptyCollection()][string[]]$Arguments
    )
    $network = Get-CodexLocalProxy
    $launchArguments = @($Arguments | Where-Object {
        $_ -notlike '--proxy-server=*' -and $_ -ne '--no-proxy-server' -and $_ -notlike '--disable-features=*'
    }) + @("--proxy-server=$($network.Proxy)")
    # Keep the new renderer running while the startup animation covers it.
    # Merge existing feature switches instead of replacing caller preferences.
    $disabledFeatures = @('CalculateNativeWinOcclusion')
    foreach ($argument in @($Arguments | Where-Object {$_ -like '--disable-features=*'})) {
        $disabledFeatures += $argument.Substring('--disable-features='.Length).Split(',')
    }
    $launchArguments += '--disable-features=' + (($disabledFeatures | Select-Object -Unique) -join ',')
    if ($launchArguments -notcontains '--disable-backgrounding-occluded-windows') { $launchArguments += '--disable-backgrounding-occluded-windows' }

    $previousEnvironment = @{}
    foreach ($name in @('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'NO_PROXY')) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    try {
        Set-CodexProxyEnvironment -Configuration $network
        # The original function still checks SignatureKind=Store, the exact
        # registered executable path and the package identity before starting.
        & $script:CodexProxyOriginalDirectLaunch -Codex $Codex -Arguments $launchArguments
    } finally {
        foreach ($name in $previousEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
        }
    }
}

function Start-DreamSkinCodex {
    param(
        [Parameter(Mandatory=$true)][object]$Codex,
        [AllowEmptyCollection()][string[]]$Arguments = @()
    )
    # Windows package activation may reuse a broker with another environment.
    # Use the project's own validated direct-launch path for consistent proxy
    # inheritance, including its ordinary restore/relaunch path.
    Start-DreamSkinCodexDirect -Codex $Codex -Arguments $Arguments
}

# Prepare nonstandard Scene package names when the manager scans its library.
# The bridge uses the original project.json; aliases share existing package bytes.
$sceneLibraryAction = Get-Variable -Name Action -ValueOnly -ErrorAction SilentlyContinue
$sceneLibraryHelper = Join-Path (Split-Path -Parent $PSScriptRoot) 'codex-scene-bridge\scene-library.ps1'
if ($sceneLibraryAction -eq 'ListWallpaperEngine' -and (Test-Path -LiteralPath $sceneLibraryHelper -PathType Leaf)) {
    . $sceneLibraryHelper
    Update-SceneBridgeLibrary | Out-Null
}

# Resume scene playback only after the upstream launcher has recorded the new
# browser identity. Old stream URLs cannot survive a complete Codex restart.
if ((Get-Command Write-DreamSkinState -ErrorAction SilentlyContinue) -and
    -not (Get-Variable CodexSceneOriginalStateWriter -Scope Script -ErrorAction SilentlyContinue)) {
    $script:CodexSceneOriginalStateWriter=(Get-Item 'Function:\Write-DreamSkinState').ScriptBlock
    function Write-DreamSkinState {
        param([Parameter(Mandatory=$true)][string]$Path,[Parameter(Mandatory=$true)][object]$State)
        & $script:CodexSceneOriginalStateWriter -Path $Path -State $State
        $root=Split-Path -Parent $Path
        $bridge=Join-Path (Split-Path -Parent $PSScriptRoot) 'codex-dynamic-skin-trial\scene-runtime'
        Start-CodexSceneRecovery -State $State -SkinRoot $root -BridgeRoot $bridge -Background
    }
}

# A renderer/media startup failure must not silently close the user's window
# and reopen a different, unskinned client. Explicit restart/restore still uses
# the original exact-package and lifecycle checks.
if ((Get-Command Stop-DreamSkinCodex -ErrorAction SilentlyContinue) -and
    -not (Get-Variable CodexSceneOriginalStop -Scope Script -ErrorAction SilentlyContinue)) {
    $script:CodexSceneOriginalStop=(Get-Item 'Function:\Stop-DreamSkinCodex').ScriptBlock
    function Stop-DreamSkinCodex {
        param([Parameter(Mandatory=$true)][object]$Codex,[AllowEmptyCollection()][int[]]$PreserveProcessIds=@(),[switch]$AllowForce)
        $failure=Get-Variable -Name startupError -ValueOnly -ErrorAction SilentlyContinue
        $foregroundFailure=Get-Variable -Name foregroundError -ValueOnly -ErrorAction SilentlyContinue
        if($failure-or$foregroundFailure){throw 'Skin startup failed; Codex was preserved instead of closing and reopening without its skin.'}
        & $script:CodexSceneOriginalStop -Codex $Codex -PreserveProcessIds $PreserveProcessIds -AllowForce:$AllowForce
    }
}

if ((Get-Command Invoke-DreamSkinNative -ErrorAction SilentlyContinue) -and
    -not (Get-Variable CodexSceneOriginalNative -Scope Script -ErrorAction SilentlyContinue)) {
    $script:CodexSceneOriginalNative=(Get-Item 'Function:\Invoke-DreamSkinNative').ScriptBlock
    function Invoke-DreamSkinNative {
        param([Parameter(Mandatory=$true)][string]$FilePath,[string[]]$ArgumentList=@(),[switch]$DiscardStderr)
        $category=Get-Variable -Name startFailureCategory -ValueOnly -ErrorAction SilentlyContinue
        if($category-ceq'renderer-verification-failed'-and$ArgumentList-contains'--once'){
            # The watcher is already applying the theme. Recheck it rather than
            # entering the upstream one-shot fallback with its different payload contract.
            $ArgumentList=@($ArgumentList|ForEach-Object{if($_-ceq'--once'){'--verify'}else{$_}})
        }
        & $script:CodexSceneOriginalNative -FilePath $FilePath -ArgumentList $ArgumentList -DiscardStderr:$DiscardStderr
    }
}
