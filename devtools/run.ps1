# Developer run: starts Vampire Survivors windowed with the dev console on, records its PID (devtools/devinstall.py on first).
param([string]$Vs = 'D:\SteamLibrary\steamapps\common\Vampire Survivors', [switch]$Auto, [switch]$Slow, [string]$Hero = '', [string]$Cards = '')
$env:SteamAppId = '1794680'; $env:SteamGameId = '1794680'; $env:VAMPIREROUNDS_DEV = '1'
if ($Auto) { $env:VAMPIREROUNDS_AUTOPLAY = '1' }
if ($Slow) { $env:VAMPIREROUNDS_AUTOPLAY_SLOW = '1' }
if ($Hero) { $env:VAMPIREROUNDS_AUTOPLAY_HERO = $Hero }
if ($Cards) { $env:VAMPIREROUNDS_DEV_CARDS = $Cards }  # e.g. 'Explosive Bullet,Bouncy': every run starts with them
$p = Start-Process -FilePath (Join-Path $Vs 'VampireSurvivors.exe') -WorkingDirectory $Vs -ArgumentList '-screen-fullscreen','0','-screen-width','1280','-screen-height','720' -PassThru
$p.Id | Out-File -Encoding ascii (Join-Path $env:USERPROFILE 'vsr-work\vs.pid')
"started VS pid $($p.Id)"
