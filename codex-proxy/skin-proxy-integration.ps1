# Loaded after the original Dream Skin common functions. Keep its verified
# Store executable and lifecycle checks; change only how the child is launched.
. (Join-Path $PSScriptRoot 'proxy-network.ps1')

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
