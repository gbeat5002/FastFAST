# Samples CPU per thread and GPU load of Cities.exe once per second during a Bench run.
# Start it right after creating Bench\run.flag; it stops when the run writes done.txt.
# Output: <run dir>\system.csv  (utc, main/sim/render thread CPU %, process CPU %, GPU 3D %, private/working set MB)
$root = Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines\Bench"
$t0 = Get-Date
$run = $null
while (-not $run -and ((Get-Date) - $t0).TotalMinutes -lt 15) {
    Start-Sleep 2
    $run = Get-ChildItem $root -Directory -ErrorAction SilentlyContinue | Where-Object { $_.CreationTime -gt $t0.AddSeconds(-5) -and (Test-Path "$($_.FullName)\meta.txt") } | Select-Object -First 1
}
if (-not $run) { "no Bench run started"; exit 1 }
$meta = Get-Content "$($run.FullName)\meta.txt" -Raw
$mainId = [int]([regex]::Match($meta, 'main thread OS id (\d+)').Groups[1].Value)
$simId = [int]([regex]::Match($meta, 'simulation thread OS id (\d+)').Groups[1].Value)
$out = "$($run.FullName)\system.csv"
"utc,main_pct,sim_pct,render_pct,render_tid,process_pct,gpu3d_pct,private_mb,workingset_mb" | Set-Content $out
$cores = [Environment]::ProcessorCount
$prev = @{}; $prevProc = $null; $prevT = $null
while (-not (Test-Path "$($run.FullName)\done.txt")) {
    $p = Get-Process Cities -ErrorAction SilentlyContinue
    if (-not $p) { break }
    $g = Get-Counter "\GPU Engine(pid_$($p.Id)*engtype_3D)\Utilization Percentage" -SampleInterval 1 -MaxSamples 1 -ErrorAction SilentlyContinue
    $p.Refresh(); $now = Get-Date
    $cur = @{}; foreach ($t in $p.Threads) { try { $cur[$t.Id] = $t.TotalProcessorTime.TotalMilliseconds } catch {} }
    if ($prevT) {
        $dt = ($now - $prevT).TotalMilliseconds
        $pct = @{}; foreach ($k in $cur.Keys) { if ($prev.ContainsKey($k)) { $pct[$k] = ($cur[$k] - $prev[$k]) / $dt * 100 } }
        # Render thread = busiest thread that is neither main nor simulation.
        $render = $pct.GetEnumerator() | Where-Object { $_.Key -ne $mainId -and $_.Key -ne $simId } | Sort-Object Value -Descending | Select-Object -First 1
        $procPct = ($p.TotalProcessorTime.TotalMilliseconds - $prevProc) / $dt * 100 / $cores
        "{0},{1:F1},{2:F1},{3:F1},{4},{5:F1},{6:F1},{7:F0},{8:F0}" -f $now.ToUniversalTime().ToString("o"), $pct[$mainId], $pct[$simId], $render.Value, $render.Key, $procPct,
            (($g.CounterSamples | Measure-Object CookedValue -Sum).Sum), ($p.PrivateMemorySize64 / 1MB), ($p.WorkingSet64 / 1MB) | Add-Content $out
    }
    $prev = $cur; $prevProc = $p.TotalProcessorTime.TotalMilliseconds; $prevT = $now
}
"sampling finished: $out"
