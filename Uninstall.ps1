$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'scripts\Paths.ps1')
$shell=New-Object -ComObject WScript.Shell
$desktop=[Environment]::GetFolderPath('Desktop')
foreach ($name in @('Codex Startup Suite.lnk','Codex Startup Settings.lnk')) {
    $path=Join-Path $desktop $name
    if (Test-Path -LiteralPath $path) {
        $link=$shell.CreateShortcut($path)
        $launcher=Join-Path $PSScriptRoot 'codex-proxy\start-codex-with-skin.ps1'
        $settings=Join-Path $PSScriptRoot 'dragon-codex-boot\CodexCinematicBoot.exe'
        $expectedPs=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $expectedArgs='-NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File "'+$launcher+'"'
        if (($name -eq 'Codex Startup Suite.lnk' -and $link.TargetPath -ieq $expectedPs -and $link.Arguments -ceq $expectedArgs) -or
            ($name -eq 'Codex Startup Settings.lnk' -and $link.TargetPath -ieq $settings -and $link.Arguments -ceq '--settings')) { Assert-OrdinaryPath $path; Remove-Item -LiteralPath $path -Force }
    }
}
$common=Join-Path $env:LOCALAPPDATA 'CodexDreamSkin\manager-runtime\1.7.3\payload\scripts\common-windows.ps1'
$helper=Join-Path $PSScriptRoot 'codex-proxy\skin-proxy-integration.ps1'
$hook="`r`n# CODEX_LOCAL_PROXY_INTEGRATION_V1`r`n. '$($helper.Replace("'", "''"))'`r`n"
if (Test-Path -LiteralPath $common) {
    Assert-OrdinaryPath $common
    $text=[IO.File]::ReadAllText($common)
    if ($text.EndsWith($hook,[StringComparison]::Ordinal)) {
        $original=$text.Substring(0,$text.Length-$hook.Length)
        $bytes=[Text.Encoding]::UTF8.GetBytes($original)
        $sha=[Security.Cryptography.SHA256]::Create()
        try { $hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','') } finally { $sha.Dispose() }
        if ($hash -cne 'A6EA8E59633943EC49F2D41061882E84A7B2255C4B442944D55FB0DD36914FC9') { throw 'Runtime was changed; preserved it for manual review.' }
        [IO.File]::WriteAllBytes($common,$bytes)
    }
}
'Owned shortcuts and matching integration hook removed. Suite files and all user data retained. Restore official appearance in the skin manager to close its debug session.'
