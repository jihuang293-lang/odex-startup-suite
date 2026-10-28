$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Paths.ps1')
& (Join-Path $root 'Build.ps1')
$dist=Join-Path $root 'dist'; Assert-OrdinaryPath $dist
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$stage=Join-Path $dist ('stage-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$files=@(& git -c ('safe.directory='+$root.Replace('\','/')) -C $root ls-files)
if ($LASTEXITCODE -ne 0 -or $files.Count -lt 10) { throw 'Initialize and stage the reviewed source files in Git before packing.' }
foreach ($relative in $files) {
    if ($relative -like 'downloads/*') { continue }
    if ($relative -like 'docs/preview/*') { continue }
    if ($relative -match '(?i)(^|/)(qa|logs|media|tools|dependencies|dist|backup[^/]*)(/|$)|\.(exe|dll|mp4|jpg|png|log|zip|7z)$') { throw ('Unexpected publish input: '+$relative) }
    $target=Assert-ChildPath (Join-Path $stage $relative) $stage
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target
}
foreach ($relative in @('dragon-codex-boot\CodexCinematicBoot.exe','dragon-codex-boot\DragonCodexBoot.exe','codex-scene-bridge\SceneViewer.exe')) { Copy-Item -LiteralPath (Join-Path $root $relative) -Destination (Join-Path $stage $relative) }
$manifest=@(Get-ChildItem -LiteralPath $stage -File -Recurse -Force | ForEach-Object { [pscustomobject]@{path=$_.FullName.Substring($stage.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()} })
[IO.File]::WriteAllText((Join-Path $stage 'SHA256SUMS.json'),($manifest|ConvertTo-Json -Depth 3),[Text.UTF8Encoding]::new($false))
$zip=Join-Path $dist 'codex-startup-suite-v0.1.0-windows-x64.zip'
if (Test-Path -LiteralPath $zip) { throw 'Release archive already exists; preserve it or choose another version before packing again.' }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in @(Get-ChildItem -LiteralPath $stage -File -Recurse -Force)) {
        $name=$file.FullName.Substring($stage.Length+1).Replace('\','/')
        $null=[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$name,[IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
Remove-Item -LiteralPath (Assert-ChildPath $stage $dist) -Recurse -Force
Get-FileHash -LiteralPath $zip
