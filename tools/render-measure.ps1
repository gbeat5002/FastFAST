# Waits until the city is loaded (mod catalog line + settle time), then prints memory figures.
$log = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt"
$t0 = Get-Date
while ((Get-Date) - $t0 -lt [TimeSpan]::FromMinutes(10)) {
  Start-Sleep 5
  if ((Get-Item $log).LastWriteTime -gt $t0.AddSeconds(-60) -and (Select-String -Path $log -Pattern 'OnDemandAssets\] Catalog' -Quiet)) { break }
}
Start-Sleep 30
$p = Get-Process Cities -ErrorAction SilentlyContinue
if (-not $p) { "game not running"; return }
$c = Get-Counter "\GPU Process Memory(pid_$($p.Id)*)\Dedicated Usage" -ErrorAction SilentlyContinue
"loaded after {0:N0} s: private {1:N0} MB, working set {2:N0} MB, VRAM {3:N0} MB" -f ((Get-Date)-$t0).TotalSeconds, ($p.PrivateMemorySize64/1MB), ($p.WorkingSet64/1MB), $(if ($c) { ($c.CounterSamples | Measure-Object CookedValue -Sum).Sum/1MB } else { -1 })
(Select-String -Path $log -Pattern 'Loading simulation data from').Line
