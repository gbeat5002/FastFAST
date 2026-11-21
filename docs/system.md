# System and install (verified 2026-11-21)

## Hardware
| Part | Value | Note |
|---|---|---|
| CPU | AMD Ryzen 7 9700X, 8 cores / 16 threads | |
| GPU | AMD Radeon RX 7900 XT, 20 GB VRAM | Driver 32.0.32029.2010 |
| RAM | 32 GB DDR5 (2 x 16 GB) at **4800 MT/s** | Running at JEDEC default; the 9700X is specced for ~6000 MT/s with EXPO. BIOS change, user's call. |
| Display | 2560 x 1440 @ 170 Hz | |
| OS | Windows 11 Home 10.0.26200 | |

## Game
- Cities: Skylines 1.21.1-f9 (Steam), Unity 5.6.7f1, Direct3D 11
- Game path: `C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines`
- Workshop path: `C:\Program Files (x86)\Steam\steamapps\workshop\content\255710`
- Data path: `%LOCALAPPDATA%\Colossal Order\Cities_Skylines`
- Launched through the Paradox Launcher. Start Steam via Explorer only (see `tools/render-test.ps1`).

## Content
- Workshop items subscribed: **2,560** (up from 512 on 2026-11-20)
- Asset files (.crp): **9,532**, 28.8 GB on disk
- Loading Screen Mod Revisited: "load used assets" only, sharing on
- Skyve: 46 mods marked "excluded" (not loaded at all)

## Graphics-relevant mod settings (current)
- Lumina: dynamic resolution on, SSAAFactor 2 (was 4)
- Dynamic Resolution mod: ssaaFactor 1 (was 3)
- Originals backed up in `backup/2026-11-21/settings/`

## Benchmark city
- New game on a stock map (user's choice after save-loading issues), empty city.
- Positions are picked by the Bench mod and frozen in `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Bench\positions.txt`.
- Limits: an empty city measures mod/effects/terrain cost, not building or simulation load. Simulation-correctness checks need a populated city.

## Enabled mods
Exported by the Bench mod from the game's plugin manager on each run (`mods.csv` in the run folder); summary below.
