# Builds FastFAST (one mod, one DLL) against the installed Cities: Skylines and installs it.
# Usage: build.ps1 [-NoInstall]
# Close the game first: CS1 only picks up a new DLL on its next start.
# The sources stay in one folder per part; they all compile into FastFAST.dll.
param([switch]$NoInstall)
$game = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"
$mods = "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods"
$harmony = "C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\2040656402"   # CitiesHarmony (Workshop)

# Explicit source files (nothing gets swept in by accident).
$files = "FastFAST\FastFASTMod.cs",
    "UiRedrawGuard\UiRedrawGuard.cs",
    "OnDemandAssets\OnDemandAssets.cs", "OnDemandAssets\TextureTrim.cs",
    "Profiler\Profiling.cs", "Profiler\ProfilerMod.cs", "Profiler\MemSnapshot.cs", "Profiler\Census.cs", "Profiler\UiGhostTest.cs", "Profiler\StressCity.cs",
    "Bench\Bench.cs"
$refs = @("Assembly-CSharp", "ColossalManaged", "ICities", "UnityEngine" | ForEach-Object { "/r:$game\$_.dll" }) +
    "/r:$PSScriptRoot\lib\CitiesHarmony.API.dll", "/r:$harmony\CitiesHarmony.Harmony.dll", "/r:System.Core.dll"
$src = @($files | ForEach-Object { Join-Path $PSScriptRoot $_ })
$out = "$PSScriptRoot\FastFAST\FastFAST.dll"

& C:\Windows\Microsoft.NET\Framework64\v3.5\csc.exe /nologo /target:library /optimize /out:$out @refs @src
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }
if ($NoInstall) { "Built: $out"; exit 0 }

$dst = Join-Path $mods "FastFAST"
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item $out, "$PSScriptRoot\lib\CitiesHarmony.API.dll" $dst -Force
"Built and installed: $dst"
# The old separate mods (OnDemandTest, UiRedrawGuard, ModProfiler, Bench folders) must not stay installed next to
# FastFAST, or every part loads twice. Move them out of Addons\Mods before enabling FastFAST.
