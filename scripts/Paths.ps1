function Assert-OrdinaryPath {
    param([Parameter(Mandatory=$true)][string]$Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw ('Redirected path refused: '+$cursor) }
        $parent = Split-Path -Parent $cursor
        if ($parent -eq $cursor) { break }; $cursor=$parent
    }
}
function Assert-ChildPath {
    param([string]$Path,[string]$Root)
    $full=[IO.Path]::GetFullPath($Path); $base=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')+'\'
    if (-not $full.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing a path outside the intended output directory.' }
    Assert-OrdinaryPath $full
    return $full
}
