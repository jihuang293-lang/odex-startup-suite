$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'scene-library.ps1')
$fixture=Join-Path $PSScriptRoot ('qa\alias-fixture-'+[Guid]::NewGuid().ToString('N'))
$content=Join-Path $fixture 'steamapps\workshop\content\431960'
try {
    foreach($id in @('1001','1002','1003','1004')) {New-Item -ItemType Directory -Path (Join-Path $content $id) -Force | Out-Null}
    $header=[byte[]](8,0,0,0,80,75,71,86,48,48,50,51,1,2,3,4)
    foreach($id in @('1001','1002','1003','1004')) {[IO.File]::WriteAllBytes((Join-Path (Join-Path $content $id) 'gifscene.pkg'),$header)}
    [IO.File]::WriteAllText((Join-Path $content '1001\project.json'),'{"type":"scene","file":"gifscene.json"}')
    [IO.File]::WriteAllText((Join-Path $content '1002\project.json'),'{"type":"scene","file":"../1001/gifscene.json"}')
    [IO.File]::WriteAllText((Join-Path $content '1003\project.json'),'{"type":"web","file":"gifscene.json"}')
    [IO.File]::WriteAllText((Join-Path $content '1004\project.json'),'{"type":"Scene","file":"gifscene.json"}')
    [IO.File]::WriteAllText((Join-Path $content '1004\scene.pkg'),'preserve existing native package')
    $aliases=@(Update-SceneBridgeLibrary -SteamLibraryPath $fixture)
    if($aliases.Count -ne 1 -or $aliases[0].workshopId -ne '1001') {throw 'Valid scene alias or traversal/type guard failed.'}
    if((Get-FileHash -LiteralPath (Join-Path $content '1001\scene.pkg')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $content '1001\gifscene.pkg')).Hash) {throw 'Alias bytes differ.'}
    if([IO.File]::ReadAllText((Join-Path $content '1004\scene.pkg')) -ne 'preserve existing native package') {throw 'Existing package was modified.'}
    if(@(Update-SceneBridgeLibrary -SteamLibraryPath $fixture).Count -ne 0) {throw 'Repeated scan was not idempotent.'}
    'PASS: valid package alias, unchanged native package, traversal/type rejection, repeat scan.'
} finally {
    if(Test-Path -LiteralPath $fixture) {
        $resolved=(Get-Item -LiteralPath $fixture).FullName
        if(-not $resolved.StartsWith((Join-Path $PSScriptRoot 'qa\alias-fixture-'),[StringComparison]::OrdinalIgnoreCase)) {throw 'Refusing unexpected fixture removal.'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
