# Sets the two supersampling values, restarts CS1 into the last save, and returns once launched.
# Usage: render-test.ps1 -Dr 1 -Lumina 2   (then press Play in the Paradox Launcher)
param([int]$Dr, [int]$Lumina)
$drCfg = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\DynamicResolutionConfig.xml"
$luCfg = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Lumina.xml"

# Close the game gracefully: a forced stop makes the Paradox Launcher report a crash.
$p = Get-Process Cities -ErrorAction SilentlyContinue
if ($p) { $p.CloseMainWindow() | Out-Null; if (-not $p.WaitForExit(60000)) { Write-Error "Game did not close; quit it from the menu"; exit 1 } }

(Get-Content $drCfg -Raw) -replace '<ssaaFactor>\d+</ssaaFactor>', "<ssaaFactor>$Dr</ssaaFactor>" | Set-Content $drCfg -NoNewline
(Get-Content $luCfg -Raw) -replace '<SSAAFactor>\d+</SSAAFactor>', "<SSAAFactor>$Lumina</SSAAFactor>" | Set-Content $luCfg -NoNewline

# Launch through Explorer so Steam (if it has to start) gets the normal desktop environment, not this shell's.
Start-Process explorer.exe 'steam://rungameid/255710'
"DR=$Dr Lumina=$Lumina launched $(Get-Date -Format HH:mm:ss)"
