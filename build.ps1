# Builds each mod against the installed Cities: Skylines and installs it.
# Usage: build.ps1 [-NoInstall] [mod names...]   e.g.  build.ps1 Bench
# Close the game first: CS1 only picks up a new DLL on its next start.
param([switch]$NoInstall, [string[]]$Only)
$game = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"
$mods = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods"
$base = "Assembly-CSharp", "ColossalManaged", "ICities", "UnityEngine" | ForEach-Object { "/r:$game\$_.dll" }

# Each mod: explicit source files (nothing gets swept in by accident) and its install folder.
$build = @(
    @{ name = "OnDemandAssets"; files = "OnDemandAssets.cs", "TextureTrim.cs"; installTo = "OnDemandTest" },   # folder name kept: it is the entry already enabled in Content Manager
    @{ name = "Bench"; files = "Bench.cs"; installTo = "Bench" }
)

foreach ($m in $build) {
    if ($Only -and $Only -notcontains $m.name) { continue }
    $src = @($m.files | ForEach-Object { Join-Path "$PSScriptRoot\$($m.name)" $_ })
    $out = "$PSScriptRoot\$($m.name)\$($m.name).dll"
    & C:\Windows\Microsoft.NET\Framework64\v3.5\csc.exe /nologo /target:library /optimize /out:$out @base @src
    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed: $($m.name)"; exit 1 }
    if ($NoInstall) { "Built: $out"; continue }
    $dst = Join-Path $mods $m.installTo
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item $out $dst -Force
    "Built and installed: $($m.name) -> $dst"
}
