function ConvertTo-CodexLocalProxy {
    param([string]$ProxyServer, [int]$ProxyEnabled)
    if ($ProxyEnabled -ne 1 -or [string]::IsNullOrWhiteSpace($ProxyServer)) {
        throw 'Please start Clash Verge and enable System Proxy first. TUN is not required.'
    }
    $candidate = $ProxyServer
    if ($candidate -match '(?:^|;)https?=([^;]+)') { $candidate = $Matches[1] }
    if ($candidate -notmatch '^(?:http://)?(?:127\.0\.0\.1|localhost):(\d+)$') {
        throw 'Expected a local HTTP proxy such as 127.0.0.1:7897. No network settings were changed.'
    }
    $proxyPort = [int]$Matches[1]
    if ($proxyPort -lt 1 -or $proxyPort -gt 65535) { throw 'Invalid local proxy port.' }
    [pscustomobject]@{
        Proxy = "http://127.0.0.1:$proxyPort"
        Port = $proxyPort
        NoProxy = 'localhost,127.0.0.1,::1'
    }
}

function Get-CodexLocalProxy {
    $internet = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
    $configuration = ConvertTo-CodexLocalProxy -ProxyServer $internet.ProxyServer -ProxyEnabled $internet.ProxyEnable
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $connection = $client.ConnectAsync('127.0.0.1', $configuration.Port)
        if (-not $connection.Wait(3000) -or -not $client.Connected) {
            throw 'The local proxy is not listening. Please start Clash Verge first.'
        }
    } finally { $client.Dispose() }
    return $configuration
}

function Set-CodexProxyEnvironment {
    param([Parameter(Mandatory=$true)][object]$Configuration)
    $env:HTTP_PROXY = $Configuration.Proxy
    $env:HTTPS_PROXY = $Configuration.Proxy
    $env:ALL_PROXY = $Configuration.Proxy
    $env:NO_PROXY = $Configuration.NoProxy
}
