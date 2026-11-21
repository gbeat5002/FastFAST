# Samples Cities: Skylines (Cities.exe) memory/CPU every 2s into a CSV until the game exits.
param([string]$Out = "$PSScriptRoot\cs1-baseline.csv", [int]$Interval = 2)

Write-Host "Waiting for Cities.exe..."
while (-not ($p = Get-Process Cities -ErrorAction SilentlyContinue | Select-Object -First 1)) { Start-Sleep 1 }
$cores = [Environment]::ProcessorCount
$start = Get-Date
$lastCpu = $p.TotalProcessorTime; $lastT = $start
"elapsed_s,private_mb,workingset_mb,cpu_pct,free_ram_mb,pagefile_mb,vram_mb" | Set-Content $Out
Write-Host "Recording PID $($p.Id) -> $Out"

while (-not $p.HasExited) {
    Start-Sleep $Interval
    try { $p.Refresh() } catch { break }
    if ($p.HasExited) { break }
    $now = Get-Date
    $cpu = ($p.TotalProcessorTime - $lastCpu).TotalSeconds / ($now - $lastT).TotalSeconds / $cores * 100
    $lastCpu = $p.TotalProcessorTime; $lastT = $now
    $free = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1KB)
    $pf = (Get-CimInstance Win32_PageFileUsage | Measure-Object CurrentUsage -Sum).Sum
    $vram = ""
    try {
        $c = Get-Counter "\GPU Process Memory(pid_$($p.Id)*)\Dedicated Usage" -ErrorAction Stop
        $vram = [math]::Round(($c.CounterSamples | Measure-Object CookedValue -Sum).Sum / 1MB)
    } catch {}
    [string]::Format([cultureinfo]::InvariantCulture, "{0:F0},{1},{2},{3:F1},{4},{5},{6}",
        ($now - $start).TotalSeconds, [math]::Round($p.PrivateMemorySize64/1MB),
        [math]::Round($p.WorkingSet64/1MB), $cpu, $free, $pf, $vram) | Add-Content $Out
}
Write-Host "Game exited. Samples saved to $Out"
