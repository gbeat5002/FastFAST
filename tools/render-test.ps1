# Sets the two supersampling values, restarts CS1 into the newest save (yokohama), and waits for the city.
# Usage: render-test.ps1 -Dr 3 -Lumina 4
param([int]$Dr, [int]$Lumina)
$drCfg = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\DynamicResolutionConfig.xml"
$luCfg = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Lumina.xml"
$log = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt"

$p = Get-Process Cities -ErrorAction SilentlyContinue
if ($p) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(15000) | Out-Null }
(Get-Content $drCfg -Raw) -replace '<ssaaFactor>\d+</ssaaFactor>', "<ssaaFactor>$Dr</ssaaFactor>" | Set-Content $drCfg -NoNewline
(Get-Content $luCfg -Raw) -replace '<SSAAFactor>\d+</SSAAFactor>', "<SSAAFactor>$Lumina</SSAAFactor>" | Set-Content $luCfg -NoNewline
(Get-Item "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Saves\yokohama.crp").LastWriteTime = Get-Date
Start-Sleep 2
& "C:\Program Files (x86)\Steam\steam.exe" -applaunch 255710 --continuelastsave
"DR=$Dr Lumina=$Lumina launched $(Get-Date -Format HH:mm:ss)"
