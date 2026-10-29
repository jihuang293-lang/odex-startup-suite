function Get-CodexNativeTcpListeners {
    [CmdletBinding()]
    param([ValidateRange(1,65535)][int]$Port)
    if(-not('CodexLocalStartup.StartupTcpTables'-as[type])){
        $assemblyPath=Join-Path $PSScriptRoot 'CodexStartupNetwork.dll'
        Assert-CodexScenePath $assemblyPath
        $null=[Reflection.Assembly]::LoadFrom($assemblyPath)
    }
    return [CodexLocalStartup.StartupTcpTables]::ReadListeners($Port)
}
function Get-CodexStartupTcpListeners {
    param([ValidateRange(1,65535)][int]$Port)
    try{return @(Get-CodexNativeTcpListeners -Port $Port -ErrorAction Stop)}
    catch{
        # A missing helper, unavailable address family or changing table must
        # retain the original provider and all downstream ownership checks.
        return @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    }
}
