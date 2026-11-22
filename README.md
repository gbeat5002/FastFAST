# FastFAST

Performance mods and profiling tools for **Cities: Skylines** (CS1), built while tuning a heavily modded install (120+ mods, 2,500+ Workshop items) for higher FPS, less RAM and faster loading, **without lowering visual quality**.

Everything is a normal C# mod: no game files are edited or redistributed.

## One mod, four parts

FastFAST is a single mod (one DLL, one Content Manager entry, one options page). The source keeps one folder per part:

| Part | What it does | Measured result |
|---|---|---|
| **UI Redraw Guard** (`UiRedrawGuard/`) | Stops the game's UI from re-rendering every frame. Any UI element that "invalidates" itself makes the whole UI (28,000+ elements in a modded game) rebuild. Hidden elements no longer trigger that, and two known offenders are fixed: Crossroads' toolbar button and the hidden hotel-chain panel. | UI re-renders on 2 of 660 frames instead of every frame. **~3 ms per frame, 56 → 66 FPS** (same-session on/off/on test). |
| **On-Demand Assets** (`OnDemandAssets/`) | Browse and load unloaded props/buildings mid-game (Ctrl+Shift+L). Frees the RAM copy of textures once they're on the GPU: custom building/prop/vehicle textures, the DLC tooltip images and the build-menu thumbnails. | **~1.2 GB less RAM** in a fresh city, with no visual change (the GPU copy is what's drawn). |
| **Mod Profiler** (`Profiler/`) | Developer tool. Frame profiler (per mod hook, per game system), UI redraw attribution, camera/GC census, memory snapshot (who holds RAM), ghost-panel test, and a no-click test-city loader (`newgame.flag`, including built-in scenarios). | Used to find everything above. |
| **Bench** (`Bench/`) | Developer tool. Repeatable benchmark: 5 camera positions, paused and 3x, fixed noon and clear weather, reference screenshots for SSIM comparison. | |

Each optimization has an on/off checkbox in FastFAST's options. The developer tools do nothing unless their trigger files are used.

## Requirements

- Cities: Skylines 1.21.x (tested on 1.21.1-f9).
- [Harmony 2 (CitiesHarmony)](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402).

## Building

`build.ps1` compiles everything into `FastFAST.dll` with the .NET 3.5 C# compiler against your installed game DLLs, and installs it to `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\FastFAST\`. Close the game first.

```powershell
.\build.ps1             # build and install
.\build.ps1 -NoInstall  # build only
```

Edit the game path at the top of `build.ps1` if your install isn't in the default Steam folder. New local mods start disabled: enable **FastFAST** in Content Manager → Mods. If you used the earlier separate mods (On-Demand Assets, UI Redraw Guard, Mod Profiler, Bench), remove them from `Addons\Mods` first, or their parts load twice.

## Docs

- `docs/profile.md`: where the frame time and RAM actually go, with measurements.
- `docs/changelog.md`: every change with its before/after numbers.
- `docs/rejected.md`: ideas that were tried and didn't work, and why.

## Using this in your own work

You're welcome to use, change and build on this code under the [MIT License](LICENSE). **Credit is required:** keep the copyright notice in any copy or derivative. If you publish a mod based on it, please credit **mouli ([github.com/gbeat5002](https://github.com/gbeat5002))** in its description.

Cities: Skylines is a trademark of Paradox Interactive. This project isn't affiliated with or endorsed by Paradox Interactive or Colossal Order.
