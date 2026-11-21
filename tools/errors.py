"""Attribute exceptions in the CS1 output log to mod assemblies.

Usage: python errors.py [output_log.txt]
Prints exception counts by the first stack frame that isn't game/engine code,
plus the game's own "The Mod ... has caused an error" popups.
"""
import collections
import re
import sys

LOG = sys.argv[1] if len(sys.argv) > 1 else r"C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities_Data\output_log.txt"
lines = open(LOG, encoding="utf-8", errors="replace").read().splitlines()

# Assembly name -> folder, from the game's own "Loading <dll>" lines.
dll_dir = {}
for l in lines:
    m = re.search(r"Loading (.+)[\\/]([^\\/]+)\.dll\s+\[Mods", l)
    if m:
        dll_dir.setdefault(m.group(2), m.group(1))

GAME = re.compile(r"^(UnityEngine|System|ColossalFramework|Mono|HarmonyLib|CitiesHarmony|DMD<|\(wrapper|ICities|"
                  r"PrefabCollection|Building|NetManager|LoadingManager|SimulationManager|ToolController|UIView|"
                  r"RenderManager|AsyncTask|MonoMethod|DebugLogHandler|Logger|Debug|PackageManager|Singleton)")
hits, first = collections.Counter(), {}
popups = collections.Counter()
for i, l in enumerate(lines):
    m = re.search(r"The Mod (.+?)\.dll\]? has caused an error", l)
    if m:
        popups[re.split(r"[\\/\[]", m.group(1))[-1]] += 1
    if "Exception" not in l or l.lstrip().startswith("at "):
        continue
    for f in lines[i + 1:i + 25]:
        f = f.strip()
        if f.startswith("at "):
            f = f[3:]
        if not f or f.startswith("(Filename"):
            break
        if GAME.match(f):
            continue
        ns = re.split(r"[.:<(\s+]", f, 1)[0]
        if ns:
            hits[ns] += 1
            first.setdefault(ns, l.strip()[:100])
        break

print("## Exceptions by first non-game frame")
for ns, n in hits.most_common(40):
    print(f"{n:6d}  {ns:32s} {dll_dir.get(ns, '')}\n        {first[ns]}")
print("\n## 'The Mod ... has caused an error' popups")
for d, n in popups.most_common():
    print(f"{n:6d}  {d}  {dll_dir.get(d, '')}")
