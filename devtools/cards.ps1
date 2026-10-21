# Developer card sheet: starts ROUNDS hidden with the engine's --vampirerounds-cards (every card, whole and its logo, into
# %LOCALAPPDATA%\VampireRounds\cards\sheet, and what each is drawn with into cards\audit.txt), waits until it ends.
# Needs devtools/devinstall.py on (the engine in VS's UserData) and ROUNDS closed.
param([string]$Vs = 'D:\SteamLibrary\steamapps\common\Vampire Survivors', [string]$Rounds = 'D:\SteamLibrary\steamapps\common\ROUNDS', [int]$Timeout = 300)
$pre = Join-Path $Vs 'UserData\VampireRounds\rounds\BepInEx\core\BepInEx.Preloader.dll'
$psi = New-Object System.Diagnostics.ProcessStartInfo (Join-Path $Rounds 'Rounds.exe')
$psi.Arguments = "--doorstop-enabled true --doorstop-target-assembly `"$pre`" --vampirerounds-engine --vampirerounds-cards -batchmode -screen-fullscreen 0 -screen-width 640 -screen-height 360"
$psi.WorkingDirectory = $Rounds
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.EnvironmentVariables['SteamAppId'] = '1557740'
$psi.EnvironmentVariables['SteamGameId'] = '1557740'
$psi.EnvironmentVariables.Remove('DOORSTOP_DISABLE')
$psi.EnvironmentVariables.Remove('DOORSTOP_INITIALIZED')
$p = [System.Diagnostics.Process]::Start($psi)
"started ROUNDS pid $($p.Id) for the card sheet"
if (-not $p.WaitForExit($Timeout * 1000)) { $p.Kill(); "timed out; stopped pid $($p.Id)" } else { "ROUNDS ended (exit $($p.ExitCode))" }
