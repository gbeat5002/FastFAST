# Runs one Bench pass per variant in the current game session, flipping UI Redraw Guard's settings file between runs.
# Usage: ab.ps1 "crossroads=off hotel=off" "crossroads=on hotel=off" "crossroads=on hotel=on"
# Prints each variant's run folder. Keep the game focused; don't run with profiler timers installed.
$data = Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines"
foreach ($variant in $args) {
    Set-Content "$data\UiRedrawGuard.txt" (($variant -split ' ') -join "`n")
    Start-Sleep 5   # guard re-reads its settings every 2 s
    $before = @(Get-ChildItem "$data\Bench" -Directory | ForEach-Object Name)
    Set-Content "$data\Bench\run.flag" $variant
    & "$PSScriptRoot\sample.ps1" | Out-Null
    $run = Get-ChildItem "$data\Bench" -Directory | Where-Object { $before -notcontains $_.Name } | Select-Object -First 1
    if (-not $run -or -not (Test-Path "$($run.FullName)\done.txt")) { "FAILED: $variant"; exit 1 }
    Set-Content "$($run.FullName)\variant.txt" $variant
    "$variant -> $($run.Name)"
}
