Import-Module ([IO.Path]::Combine($PSHOME,'Modules','Microsoft.PowerShell.Utility','Microsoft.PowerShell.Utility.psd1')) -ErrorAction Stop
function Assert-CodexScenePath {
    param([string]$Path)
    $cursor=[IO.Path]::GetFullPath($Path)
    while($cursor){
        if((Test-Path -LiteralPath $cursor)-and((Get-Item -LiteralPath $cursor -Force).Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'Redirected scene paths are not supported.'}
        $next=Split-Path -Parent $cursor;if($next -eq $cursor){break};$cursor=$next
    }
}
function Read-CodexSceneJson {
    param([string]$Path)
    Assert-CodexScenePath $Path
    if(-not(Test-Path -LiteralPath $Path -PathType Leaf)-or(Get-Item -LiteralPath $Path).Length-gt 1048576){throw 'Scene metadata is missing or oversized.'}
    return ([IO.File]::ReadAllText($Path)|ConvertFrom-Json)
}
function Get-CodexSceneDescription {
    param([object]$Theme)
    if($Theme.media.type-ne'scene'){return $null}
    $id=[string]$Theme.media.workshopId
    if($id-notmatch'^[0-9]{1,20}$'){throw 'Invalid scene Workshop ID.'}
    $scene=[IO.Path]::GetFullPath([string]$Theme.media.scenePath);Assert-CodexScenePath $scene
    $tail='\steamapps\workshop\content\431960\'+$id+'\scene.pkg'
    if(-not$scene.EndsWith($tail,[StringComparison]::OrdinalIgnoreCase)-or-not(Test-Path -LiteralPath $scene -PathType Leaf)){throw 'Scene must be an installed Workshop scene.pkg.'}
    $directory=Split-Path -Parent $scene;$project=Read-CodexSceneJson (Join-Path $directory 'project.json')
    if($project.type-ine'scene'){throw 'Workshop item is not a scene.'}
    $steamApps=Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $directory)))
    $preview=$null
    if($project.preview){
        $candidate=[IO.Path]::GetFullPath((Join-Path $directory ([string]$project.preview)))
        if($candidate.StartsWith($directory+'\',[StringComparison]::OrdinalIgnoreCase)-and[IO.Path]::GetExtension($candidate).ToLowerInvariant()-in@('.jpg','.jpeg','.png','.webp','.gif')-and(Test-Path -LiteralPath $candidate -PathType Leaf)){Assert-CodexScenePath $candidate;$preview=$candidate}
    }
    return [pscustomobject]@{Id=$id;ThemeId=[string]$Theme.id;Scene=$scene;Assets=(Join-Path $steamApps 'common\wallpaper_engine\assets');Preview=$preview}
}
function Test-CodexScenePoster {
    param([string]$Path)
    try{
        if(-not(Test-Path -LiteralPath $Path -PathType Leaf)-or(Get-Item -LiteralPath $Path).Length-gt 10485760){return $false}
        Add-Type -AssemblyName System.Drawing
        $bitmap=[Drawing.Bitmap]::new($Path)
        try{
            if($bitmap.Width-lt 16-or$bitmap.Height-lt 16-or$bitmap.Width-gt 4096-or$bitmap.Height-gt 4096){return $false}
            $lit=0;$count=0
            for($y=0;$y-lt 18;$y++){for($x=0;$x-lt 32;$x++){$color=$bitmap.GetPixel([int](($x+.5)*$bitmap.Width/32),[int](($y+.5)*$bitmap.Height/18));if([math]::Max($color.R,[math]::Max($color.G,$color.B))-gt 24){$lit++};$count++}}
            return $lit-ge[math]::Max(3,[int]($count*.01))
        }finally{$bitmap.Dispose()}
    }catch{return $false}
}
function Test-CodexRenderedScenePoster {
    param([string]$Path,[string]$WorkshopId)
    try{
        if(-not(Test-CodexScenePoster $Path)){return $false}
        $metadata=Read-CodexSceneJson ([IO.Path]::ChangeExtension($Path,'.json'))
        if($metadata.schemaVersion-ne 1-or$metadata.source-cne'rendered-scene'-or[string]$metadata.workshopId-cne$WorkshopId-or$metadata.imageSha256-notmatch'^[a-f0-9]{64}$'){return $false}
        $image=[Drawing.Bitmap]::new($Path)
        try{if($image.Width-ne$metadata.width-or$image.Height-ne$metadata.height){return $false}}finally{$image.Dispose()}
        return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash-ieq$metadata.imageSha256
    }catch{return $false}
}
function Repair-CodexScenePoster {
    param([string]$ThemePath,[string]$BridgeRoot)
    $theme=Read-CodexSceneJson $ThemePath;$scene=Get-CodexSceneDescription $theme
    if(-not$scene){return $null}
    $cache=Join-Path $BridgeRoot ('poster-cache\'+$scene.Id+'.jpg');Assert-CodexScenePath $cache
    if(Test-CodexRenderedScenePoster $cache $scene.Id){return $cache}
    # An old Workshop thumbnail may exist here. The player ignores it until a
    # hash-matched rendered-scene sidecar arrives; never promote a GIF to a full poster.
    if(Test-Path -LiteralPath $cache -PathType Leaf){return $cache}
    New-Item -ItemType Directory -Path (Split-Path -Parent $cache) -Force|Out-Null
    $temporary=$cache+'.'+[Guid]::NewGuid().ToString('N')+'.tmp.jpg';$backup=$temporary+'.replaced'
    try{
        Add-Type -AssemblyName System.Drawing
        $image=[Drawing.Bitmap]::new(1280,720);$drawing=[Drawing.Graphics]::FromImage($image)
        try{$drawing.Clear([Drawing.Color]::FromArgb(8,12,20));$image.Save($temporary,[Drawing.Imaging.ImageFormat]::Jpeg)}finally{$drawing.Dispose();$image.Dispose()}
        if(Test-Path -LiteralPath $cache){[IO.File]::Replace($temporary,$cache,$backup)}else{[IO.File]::Move($temporary,$cache)}
        return $cache
    }finally{foreach($file in @($temporary,$backup)){if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file -Force}}}
}
function Test-CodexSceneStream {
    param([string]$Url)
    try{
        $uri=[Uri]$Url
        if($uri.Scheme-ne'http'-or$uri.Host-ne'127.0.0.1'-or$uri.AbsolutePath-notmatch'^/[a-f0-9]{48}/stream\.mp4$'-or$uri.Query-or$uri.Fragment){return $false}
        $request=[Net.HttpWebRequest]::Create($uri);$request.Proxy=$null;$request.Timeout=1000;$request.ReadWriteTimeout=1000
        $response=$request.GetResponse()
        try{$stream=$response.GetResponseStream();$bytes=New-Object byte[] 12;$offset=0;while($offset-lt 12){$read=$stream.Read($bytes,$offset,12-$offset);if($read-le 0){return $false};$offset+=$read};return [Text.Encoding]::ASCII.GetString($bytes,4,4)-ceq'ftyp'}finally{$response.Dispose()}
    }catch{return $false}
}
function Set-CodexRecoveredSceneUrl {
    param([string]$ThemePath,[object]$Expected,[string]$PreviousUrl,[string]$StreamUrl)
    if(-not(Test-CodexSceneStream $StreamUrl)){throw 'Replacement scene stream is unavailable.'}
    $theme=Read-CodexSceneJson $ThemePath;$scene=Get-CodexSceneDescription $theme
    if(-not$scene-or$scene.ThemeId-cne$Expected.ThemeId-or$scene.Scene-ine$Expected.Scene-or[string]$theme.media.streamUrl-cne$PreviousUrl){return $false}
    $theme.media.streamUrl=$StreamUrl
    $temporary=$ThemePath+'.'+[Guid]::NewGuid().ToString('N')+'.tmp';$backup=$temporary+'.replaced'
    try{[IO.File]::WriteAllText($temporary,($theme|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false));[IO.File]::Replace($temporary,$ThemePath,$backup)}
    finally{foreach($file in @($temporary,$backup)){if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file -Force}}}
    return $true
}
function Start-CodexSceneRecovery {
    param([object]$State,[string]$SkinRoot,[string]$BridgeRoot,[int]$TimeoutSeconds=70,[switch]$Background)
    $themePath=Join-Path $SkinRoot 'active-theme\theme.json';$theme=Read-CodexSceneJson $themePath
    if($theme.media.type-ne'scene'){return}
    $null=Get-CodexSceneDescription $theme
    $script=Join-Path $PSScriptRoot 'scene-session.ps1';Assert-CodexScenePath $script
    $powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $args='-NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File "'+$script+'" -SkinRoot "'+$SkinRoot+'" -BridgeRoot "'+$BridgeRoot+'"'
    $null=Start-Process -FilePath $powershell -ArgumentList $args -WindowStyle Hidden -PassThru
    if($Background){return}
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds);$statusPath=Join-Path $SkinRoot 'scene-session.json'
    while([DateTime]::UtcNow-lt$deadline){
        try{$status=Read-CodexSceneJson $statusPath;$now=Read-CodexSceneJson $themePath;if($status.ready-and$status.browserId-ceq$State.browserId-and$status.themeId-ceq$now.id-and$status.streamUrl-ceq$now.media.streamUrl-and(Test-CodexSceneStream $now.media.streamUrl)){return}}catch{}
        Start-Sleep -Milliseconds 200
    }
    throw 'The scene could not resume in time. Codex will be kept open; check Wallpaper Engine and apply the scene again.'
}
