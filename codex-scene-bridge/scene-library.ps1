# Compatibility aliases for valid Scene projects whose package is not named scene.pkg.
# Only new hard links are added; original Workshop files are never rewritten.
function Test-SceneBridgeOrdinaryPath {
    param([string]$Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { return $false }
        $parent = [IO.Directory]::GetParent($cursor)
        $cursor = if ($parent) {$parent.FullName} else {$null}
    }
    return $true
}

function Update-SceneBridgeLibrary {
    param([string[]]$SteamLibraryPath)
    if (-not $SteamLibraryPath) {
        $libraries = @()
        foreach ($engine in @(Get-Process -Name wallpaper64,wallpaper32 -ErrorAction SilentlyContinue)) {
            try {
                $enginePath = $engine.Path
                if ($enginePath -and $enginePath -match '(?i)\\steamapps\\common\\wallpaper_engine\\wallpaper(32|64)\.exe$') {
                    $steamApps = [IO.Directory]::GetParent([IO.Directory]::GetParent([IO.Path]::GetDirectoryName($enginePath)).FullName).FullName
                    $libraries += [IO.Directory]::GetParent($steamApps).FullName
                }
            } catch {}
        }
        foreach ($library in @($libraries | Select-Object -Unique)) {
            $vdf = Join-Path $library 'steamapps\libraryfolders.vdf'
            if ((Test-Path -LiteralPath $vdf -PathType Leaf) -and (Get-Item -LiteralPath $vdf).Length -lt 2MB -and (Test-SceneBridgeOrdinaryPath $vdf)) {
                foreach ($match in [regex]::Matches([IO.File]::ReadAllText($vdf), '"path"\s+"([^"]+)"')) {
                    $libraries += $match.Groups[1].Value.Replace('\\','\')
                }
            }
        }
        $SteamLibraryPath = @($libraries | Select-Object -Unique)
    }
    foreach ($library in $SteamLibraryPath) {
        $contentRoot = [IO.Path]::GetFullPath((Join-Path $library 'steamapps\workshop\content\431960'))
        if (-not (Test-Path -LiteralPath $contentRoot -PathType Container) -or -not (Test-SceneBridgeOrdinaryPath $contentRoot)) {continue}
        foreach ($item in @(Get-ChildItem -LiteralPath $contentRoot -Directory -Force -ErrorAction SilentlyContinue)) {
            if ($item.Name -notmatch '^[0-9]{1,20}$' -or -not (Test-SceneBridgeOrdinaryPath $item.FullName)) {continue}
            $alias = Join-Path $item.FullName 'scene.pkg'
            if (Test-Path -LiteralPath $alias) {continue}
            $projectPath = Join-Path $item.FullName 'project.json'
            if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf) -or (Get-Item -LiteralPath $projectPath).Length -gt 262144 -or -not (Test-SceneBridgeOrdinaryPath $projectPath)) {continue}
            try {
                $project = [IO.File]::ReadAllText($projectPath) | ConvertFrom-Json
                $relative = [string]$project.file
                if ([string]$project.type -ine 'scene' -or -not $relative -or [IO.Path]::IsPathRooted($relative) -or [IO.Path]::GetExtension($relative) -ine '.json') {continue}
                $packagePath = [IO.Path]::GetFullPath((Join-Path $item.FullName ([IO.Path]::ChangeExtension($relative,'.pkg'))))
                if (-not $packagePath.StartsWith($item.FullName.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $packagePath -PathType Leaf) -or -not (Test-SceneBridgeOrdinaryPath $packagePath)) {continue}
                $stream = [IO.File]::OpenRead($packagePath)
                try {
                    $header = New-Object byte[] 12
                    if ($stream.Read($header,0,12) -ne 12 -or [BitConverter]::ToInt32($header,0) -ne 8 -or [Text.Encoding]::ASCII.GetString($header,4,4) -cne 'PKGV') {continue}
                } finally {$stream.Dispose()}
                $null = New-Item -ItemType HardLink -Path $alias -Target $packagePath -ErrorAction Stop
                [pscustomobject]@{workshopId=$item.Name;alias=$alias;source=$packagePath;kind='hardlink';copiedBytes=0}
            } catch {
                Write-Verbose ('Scene alias was not created: '+$_.Exception.Message)
            }
        }
    }
}
