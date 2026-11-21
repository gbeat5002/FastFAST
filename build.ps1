# Builds On-Demand Assets against the installed Cities: Skylines and installs it.
# Close the game first: CS1 only picks up the new DLL on its next start.
$game = "C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\Managed"
$out = "$PSScriptRoot\OnDemandAssets\OnDemandAssets.dll"
$refs = "Assembly-CSharp", "ColossalManaged", "ICities", "UnityEngine" | ForEach-Object { "/r:$game\$_.dll" }

& C:\Windows\Microsoft.NET\Framework64\v3.5\csc.exe /nologo /target:library /optimize /out:$out @refs `
    "$PSScriptRoot\OnDemandAssets\OnDemandAssets.cs" "$PSScriptRoot\OnDemandAssets\TextureTrim.cs"
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

# Installed in the OnDemandTest folder because that is the mod entry already enabled in Content Manager.
Copy-Item $out "$env:LOCALAPPDATA\Colossal Order\Cities_Skylines\Addons\Mods\OnDemandTest\" -Force
"Built and installed: $out"
