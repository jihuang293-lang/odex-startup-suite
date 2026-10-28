$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$config=Get-Content -LiteralPath (Join-Path $root 'dragon-codex-boot\launcher.example.json') -Raw | ConvertFrom-Json
if ($config.ReadyFile -cne 'client-ready.flag' -or $config.AutoReplaceEntrypoints -or $config.ScanAllLocalDrives -or $config.LaunchClient) { throw 'Portable defaults changed launch ownership or retained an absolute ready path.' }
$assembly=Join-Path $root 'dragon-codex-boot\CodexCinematicBoot.exe'
$null=[Reflection.Assembly]::LoadFrom($assembly)
$temp=Join-Path $root ('.test-output\path with spaces-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'dragon-codex-boot\launcher.example.json') -Destination (Join-Path $temp 'launcher.json')
$resolved=[DragonCodexBoot.Entry]::ReadConfig($temp)
if ($resolved.ReadyFile -ine (Join-Path $temp 'client-ready.flag')) { throw 'C# did not rebase relative readiness path to the install directory.' }
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'codex-proxy\start-codex-with-skin.ps1'),[ref]$tokens,[ref]$errors)
$default=$ast.ParamBlock.Parameters | Where-Object {$_.Name.VariablePath.UserPath -eq 'ManagerPath'}
if (-not $default -or $default.DefaultValue.Extent.Text.Contains('D:\')) { throw 'Launcher still has a machine-specific default manager path.' }
'PASS: portable readiness rebasing under a path with spaces; safe defaults and relative manager discovery.'
