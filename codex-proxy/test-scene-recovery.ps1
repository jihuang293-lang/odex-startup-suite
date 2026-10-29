$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'scene-recovery.ps1')
$fixture=Join-Path $PSScriptRoot ('qa-scene-recovery\'+[Guid]::NewGuid().ToString('N'))
$item=Join-Path $fixture 'steamapps\workshop\content\431960\12345'
$bridge=Join-Path $fixture 'bridge';New-Item -ItemType Directory -Path $item,(Join-Path $bridge 'tools'),(Join-Path $bridge 'poster-cache') -Force|Out-Null
[IO.File]::WriteAllText((Join-Path $item 'scene.pkg'),'fixture')
[IO.File]::WriteAllText((Join-Path $item 'project.json'),'{"type":"scene","preview":"preview.gif"}')
Add-Type -AssemblyName System.Drawing
$image=[Drawing.Bitmap]::new(64,64);$g=[Drawing.Graphics]::FromImage($image)
try{$g.Clear([Drawing.Color]::CornflowerBlue);$image.Save((Join-Path $item 'preview.gif'),[Drawing.Imaging.ImageFormat]::Gif)}finally{$g.Dispose();$image.Dispose()}
$black=[Drawing.Bitmap]::new(64,64);try{$black.Save((Join-Path $bridge 'poster-cache\12345.jpg'),[Drawing.Imaging.ImageFormat]::Jpeg)}finally{$black.Dispose()}
if(Test-CodexScenePoster (Join-Path $bridge 'poster-cache\12345.jpg')){throw 'Black scene loading frame was accepted.'}
$theme=[pscustomobject]@{id='scene-12345';image='preview.jpg';palette=@{accent='kept'};media=[pscustomobject]@{type='scene';workshopId='12345';scenePath=(Join-Path $item 'scene.pkg');streamUrl=('http://127.0.0.1:1/'+('a'*48)+'/stream.mp4')}}
$themePath=Join-Path $fixture 'theme.fixture.json';[IO.File]::WriteAllText($themePath,($theme|ConvertTo-Json -Depth 8))
$scene=Get-CodexSceneDescription $theme
if($scene.Id-ne'12345'-or$scene.Assets-ine(Join-Path $fixture 'steamapps\common\wallpaper_engine\assets')){throw 'Scene library paths were not derived correctly.'}
foreach($bad in @('..\12345','../12345','not-an-id')){$copy=$theme|ConvertTo-Json -Depth 8|ConvertFrom-Json;$copy.media.workshopId=$bad;$rejected=$false;try{$null=Get-CodexSceneDescription $copy}catch{$rejected=$true};if(-not$rejected){throw 'Invalid Workshop ID accepted.'}}
$poster=Repair-CodexScenePoster $themePath $bridge
if(Test-CodexRenderedScenePoster $poster '12345'){throw 'An old or black thumbnail was promoted to a rendered scene.'}
$real=[Drawing.Bitmap]::new(1280,720);$draw=[Drawing.Graphics]::FromImage($real)
try{$draw.Clear([Drawing.Color]::CornflowerBlue);$real.Save($poster,[Drawing.Imaging.ImageFormat]::Jpeg)}finally{$draw.Dispose();$real.Dispose()}
$metadata=@{schemaVersion=1;source='rendered-scene';workshopId='12345';width=1280;height=720;imageSha256=(Get-FileHash -LiteralPath $poster).Hash.ToLowerInvariant()}
[IO.File]::WriteAllText(([IO.Path]::ChangeExtension($poster,'.json')),($metadata|ConvertTo-Json))
if(-not(Test-CodexRenderedScenePoster $poster '12345')){throw 'Verified full scene was rejected.'}
if(Test-CodexRenderedScenePoster $poster '67890'){throw 'Another scene identity was accepted.'}
$before=(Get-FileHash -LiteralPath $poster).Hash;$again=Repair-CodexScenePoster $themePath $bridge
if($before-cne(Get-FileHash -LiteralPath $again).Hash){throw 'Existing usable preview was unnecessarily replaced.'}
function Test-CodexSceneStream {param($Url) $true}
$replacement='http://127.0.0.1:54321/'+('b'*48)+'/stream.mp4'
$changed=$theme|ConvertTo-Json -Depth 8|ConvertFrom-Json;$changed.id='user-selected-another-scene';[IO.File]::WriteAllText($themePath,($changed|ConvertTo-Json -Depth 8))
if(Set-CodexRecoveredSceneUrl $themePath $scene $theme.media.streamUrl $replacement){throw 'A newer user wallpaper choice was overwritten.'}
[IO.File]::WriteAllText($themePath,($theme|ConvertTo-Json -Depth 8))
if(-not(Set-CodexRecoveredSceneUrl $themePath $scene $theme.media.streamUrl $replacement)){throw 'Stale scene URL was not replaced.'}
$updated=Read-CodexSceneJson $themePath
if($updated.palette.accent-cne'kept'-or$updated.media.streamUrl-cne$replacement){throw 'Scene URL update changed unrelated preferences.'}
$script:stateWrites=0;$script:sceneStarts=0;$script:stops=0
function Start-DreamSkinCodexDirect {param($Codex,$Arguments)}
function Write-DreamSkinState {param($Path,$State) $script:stateWrites++}
function Stop-DreamSkinCodex {param($Codex,$PreserveProcessIds,[switch]$AllowForce) $script:stops++}
function Invoke-DreamSkinNative {param($FilePath,$ArgumentList,[switch]$DiscardStderr) [pscustomobject]@{args=$ArgumentList;file=$FilePath;discard=[bool]$DiscardStderr}}
. (Join-Path $PSScriptRoot 'skin-proxy-integration.ps1')
function Start-CodexSceneRecovery {param($State,$SkinRoot,$BridgeRoot,[switch]$Background) if($script:stateWrites-ne 1-or-not$Background){throw 'Scene must start after state is written without blocking the app.'};$script:sceneStarts++}
Write-DreamSkinState -Path (Join-Path $fixture 'state.json') -State @{browserId=('a'*32)}
if($script:sceneStarts-ne 1){throw 'Session state did not trigger scene recovery.'}
function Invoke-FailedStartupStop {$startupError=[Exception]::new('fixture');Stop-DreamSkinCodex -Codex @{} -AllowForce}
$protected=$false;try{Invoke-FailedStartupStop}catch{$protected=$true}
if(-not$protected-or$script:stops-ne 0){throw 'Failed skin startup closed the current client.'}
Stop-DreamSkinCodex -Codex @{} -AllowForce
if($script:stops-ne 1){throw 'Explicit ordinary restart was incorrectly blocked.'}
function Invoke-StartupRetry {$startFailureCategory='renderer-verification-failed';Invoke-DreamSkinNative -FilePath 'node-fixture' -ArgumentList @('injector.mjs','--once','--port','9335') -DiscardStderr}
$retry=Invoke-StartupRetry
if($retry.args-contains'--once'-or$retry.args-notcontains'--verify'-or-not$retry.discard-or$retry.file-cne'node-fixture'){throw 'Startup retry did not preserve native options and recheck the watcher.'}
$ordinary=Invoke-DreamSkinNative -FilePath 'node-fixture' -ArgumentList @('injector.mjs','--once')
if($ordinary.args-notcontains'--once'){throw 'An unrelated native operation was rewritten.'}
'PASS: black-frame recovery, valid-preview preservation, Workshop path guards, fresh-session ordering, preference preservation and no unskinned restart on failure.'
