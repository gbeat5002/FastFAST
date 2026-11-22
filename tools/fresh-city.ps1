# Starts a new Green Peaks city from the main menu (Mod Profiler's newgame.flag) and waits until it has loaded.
# The game must already be at the main menu. Prints the load time, or "timeout".
$data = Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines"
$log = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt"
$before = (Select-String $log -Pattern 'calling ModProfiler.Loading.OnLevelLoaded').Count
Set-Content "$data\newgame.flag" "1"
$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 420) {
    Start-Sleep 5
    if ((Select-String $log -Pattern 'calling ModProfiler.Loading.OnLevelLoaded').Count -gt $before) { "city loaded after {0:N0} s" -f ((Get-Date) - $t0).TotalSeconds; exit 0 }
    if (Select-String $log -Pattern '^Crash!!!' -Quiet) { "game crashed"; exit 1 }
}
"timeout (flag still present: $(Test-Path "$data\newgame.flag"))"; exit 1
