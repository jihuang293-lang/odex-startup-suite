$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'proxy-network.ps1')
$passed = 0
foreach ($value in @('127.0.0.1:7897', 'http://localhost:7897', 'http=127.0.0.1:7897;https=127.0.0.1:7897')) {
    $result = ConvertTo-CodexLocalProxy -ProxyServer $value -ProxyEnabled 1
    if ($result.Proxy -ne 'http://127.0.0.1:7897') { throw "Wrong proxy normalization: $value" }
    $passed++
}
foreach ($value in @('example.com:7897', '127.0.0.1:0', '127.0.0.1:65536', '127.0.0.1:7897;command=bad', '127.0.0.1:7897/path')) {
    $rejected = $false
    try { $null = ConvertTo-CodexLocalProxy -ProxyServer $value -ProxyEnabled 1 } catch { $rejected = $true }
    if (-not $rejected) { throw "Unsafe proxy accepted: $value" }
    $passed++
}
$disabledRejected = $false
try { $null = ConvertTo-CodexLocalProxy -ProxyServer '127.0.0.1:7897' -ProxyEnabled 0 } catch { $disabledRejected = $true }
if (-not $disabledRejected) { throw 'Disabled proxy accepted.' }
$passed++

# Mock the original executable launcher. These tests never start or stop apps.
function Start-DreamSkinCodexDirect {
    param($Codex, [AllowEmptyCollection()][string[]]$Arguments)
    if ($Codex.SignatureKind -ne 'Store') { throw 'Package validation rejected the target.' }
    $script:CapturedLaunch = [pscustomobject]@{
        Arguments = $Arguments
        HttpProxy = $env:HTTP_PROXY
        HttpsProxy = $env:HTTPS_PROXY
        AllProxy = $env:ALL_PROXY
        NoProxy = $env:NO_PROXY
    }
    return 77
}
. (Join-Path $PSScriptRoot 'skin-proxy-integration.ps1')
$script:TestProxyPort = 7897
function Get-CodexLocalProxy {
    ConvertTo-CodexLocalProxy -ProxyServer "127.0.0.1:$script:TestProxyPort" -ProxyEnabled 1
}
$beforeEnvironment = @{}
foreach ($name in @('HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'NO_PROXY')) {
    $beforeEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$pidResult = Start-DreamSkinCodex -Codex @{SignatureKind='Store'} -Arguments @(
    '--remote-debugging-address=127.0.0.1', '--remote-debugging-port=9335',
    '--user-data-dir=C:\Example Profile', '--no-proxy-server', '--proxy-server=http://127.0.0.1:1'
    '--disable-features=ExistingFeature'
)
if ($pidResult -ne 77 -or $script:CapturedLaunch.Arguments -notcontains '--proxy-server=http://127.0.0.1:7897' -or
    @($script:CapturedLaunch.Arguments | Where-Object {$_ -like '--proxy-server=*'}).Count -ne 1 -or
    $script:CapturedLaunch.Arguments -contains '--no-proxy-server' -or
    $script:CapturedLaunch.Arguments -notcontains '--remote-debugging-port=9335' -or
    $script:CapturedLaunch.Arguments -notcontains '--user-data-dir=C:\Example Profile') {
    throw 'Combined proxy/CDP arguments were not preserved.'
}
$passed++
if ($script:CapturedLaunch.Arguments -notcontains '--disable-features=CalculateNativeWinOcclusion,ExistingFeature' -or
    $script:CapturedLaunch.Arguments -notcontains '--disable-backgrounding-occluded-windows' -or
    @($script:CapturedLaunch.Arguments | Where-Object {$_ -like '--disable-features=*'}).Count -ne 1) {
    throw 'Startup coverage flags were not merged with the caller settings.'
}
$passed++
if ($script:CapturedLaunch.HttpProxy -ne 'http://127.0.0.1:7897' -or
    $script:CapturedLaunch.HttpsProxy -ne $script:CapturedLaunch.HttpProxy -or
    $script:CapturedLaunch.AllProxy -ne $script:CapturedLaunch.HttpProxy -or
    $script:CapturedLaunch.NoProxy -ne 'localhost,127.0.0.1,::1') { throw 'Child proxy environment was not inherited.' }
$passed++
foreach ($name in $beforeEnvironment.Keys) {
    if ([Environment]::GetEnvironmentVariable($name, 'Process') -cne $beforeEnvironment[$name]) { throw "Parent environment leaked: $name" }
}
$passed++
$script:TestProxyPort = 7898
$null = Start-DreamSkinCodexDirect -Codex @{SignatureKind='Store'} -Arguments @()
if ($script:CapturedLaunch.Arguments -notcontains '--proxy-server=http://127.0.0.1:7898') { throw 'A restart reused a stale proxy port.' }
$passed++
$script:CapturedLaunch = $null
$rejected = $false
try { $null = Start-DreamSkinCodex -Codex @{SignatureKind='Untrusted'} } catch { $rejected = $true }
if (-not $rejected -or $script:CapturedLaunch) { throw 'The original package check was bypassed.' }
$passed++
foreach ($name in $beforeEnvironment.Keys) {
    if ([Environment]::GetEnvironmentVariable($name, 'Process') -cne $beforeEnvironment[$name]) { throw "Failed launch leaked environment: $name" }
}
$passed++
function Get-CodexLocalProxy { throw 'Proxy unavailable.' }
$script:CapturedLaunch = $null
$rejected = $false
try { $null = Start-DreamSkinCodex -Codex @{SignatureKind='Store'} } catch { $rejected = $true }
if (-not $rejected -or $script:CapturedLaunch) { throw 'A missing proxy silently fell back to direct internet.' }
$passed++
Write-Output "PASS: $passed checks; no applications were launched or stopped."
