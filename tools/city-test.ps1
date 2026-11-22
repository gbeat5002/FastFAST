# Loads a test city via Mod Profiler's newgame.flag and runs census + frame profile on it.
# Usage: city-test.ps1 ["scenario:Fix the Traffic" | "Green Peaks"]   (game must be running, at the main menu)
# Writes results to <data>\citytest\<timestamp>\ : census.txt, profile.txt, system.csv (CPU/GPU each second), output_log.txt
param([string]$City = "scenario:Fix the Traffic")
$data = Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines"
$log = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt"
$out = Join-Path $data ("citytest\" + (Get-Date -Format yyyyMMdd_HHmmss)); New-Item -ItemType Directory -Force $out | Out-Null
$sh = New-Object -ComObject WScript.Shell
function Focus { $c = Get-Process Cities -ErrorAction SilentlyContinue; if ($c) { $sh.AppActivate($c.Id) | Out-Null } }
function Alive { $c = Get-Process Cities -ErrorAction SilentlyContinue; return [bool]($c -and $c.Responding) }

# Outside monitor: busiest thread CPU % and GPU 3D % every second, in its own job so a hang can't stop it.
$sampler = Start-Job -ArgumentList $out -ScriptBlock {
    param($out)
    "utc,top_thread_pct,process_pct,gpu3d_pct,responding" | Set-Content "$out\system.csv"
    $prev = @{}; $prevT = $null; $cores = [Environment]::ProcessorCount
    while ($true) {
        $p = Get-Process Cities -ErrorAction SilentlyContinue; if (-not $p) { break }
        $g = Get-Counter "\GPU Engine(pid_$($p.Id)*engtype_3D)\Utilization Percentage" -SampleInterval 1 -MaxSamples 1 -ErrorAction SilentlyContinue
        $p.Refresh(); $now = Get-Date; $cur = @{}; foreach ($t in $p.Threads) { try { $cur[$t.Id] = $t.TotalProcessorTime.TotalMilliseconds } catch {} }
        if ($prevT) {
            $dt = ($now - $prevT).TotalMilliseconds; $top = 0; $sum = 0
            foreach ($k in $cur.Keys) { if ($prev.ContainsKey($k)) { $v = ($cur[$k] - $prev[$k]) / $dt * 100; $sum += $v; if ($v -gt $top) { $top = $v } } }
            "{0},{1:F0},{2:F0},{3:F0},{4}" -f $now.ToUniversalTime().ToString("o"), $top, ($sum / $cores), (($g.CounterSamples | Measure-Object CookedValue -Sum).Sum), $p.Responding | Add-Content "$out\system.csv"
        }
        $prev = $cur; $prevT = $now
    }
}

try {
    $before = (Select-String $log -Pattern 'calling ModProfiler.Loading.OnLevelLoaded').Count
    Set-Content "$data\newgame.flag" $City
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt 600 -and (Select-String $log -Pattern 'calling ModProfiler.Loading.OnLevelLoaded').Count -le $before) {
        Focus; Start-Sleep 5
        if (Select-String $log -Pattern 'newgame.flag: (failed|map not found|scenario not found)' -Quiet) { Write-Host ((Select-String $log -Pattern 'newgame.flag:' | Select-Object -Last 1).Line); return }
    }
    Write-Host ("{0}: loaded after {1:N0} s" -f $City, ((Get-Date) - $t0).TotalSeconds)
    $t = Get-Date; while (((Get-Date) - $t).TotalSeconds -lt 45) { Focus; Start-Sleep 3 }

    foreach ($step in @(@{ flag = "census.flag"; file = "census.txt"; wait = 120 }, @{ flag = "profile.flag"; file = "profile-pass1.txt"; wait = 180 })) {
        Remove-Item "$data\$($step.file)" -ErrorAction SilentlyContinue
        Set-Content "$data\$($step.flag)" "1"
        $t = Get-Date
        while (-not (Test-Path "$data\$($step.file)") -and ((Get-Date) - $t).TotalSeconds -lt $step.wait) { Focus; Start-Sleep 3 }
        if (-not (Test-Path "$data\$($step.file)")) { Write-Host "$($step.file): not written (responding: $(Alive))"; break }
        Copy-Item "$data\$($step.file)" $out -Force
        Write-Host "$($step.file): saved"
    }
}
finally {
    Copy-Item $log "$out\output_log.txt" -Force -ErrorAction SilentlyContinue
    Stop-Job $sampler -ErrorAction SilentlyContinue; Remove-Job $sampler -Force -ErrorAction SilentlyContinue
    Write-Host "results: $out"
}
