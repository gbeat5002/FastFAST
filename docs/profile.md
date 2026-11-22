# Frame profile: where the main thread goes (2026-11-21)

Tool: `Profiler/` (Mod Profiler). Harmony Pre/Post timers, installed once per session and never removed; 20 s captures triggered by `profile.flag` (pass 1) or `profile2.flag` (pass 2). Fresh Green Peaks city, default start camera, after Phase 1 change 1. Times are inclusive ms per frame and include timer overhead.

## Result
At 22.5-23 ms per frame (43-44 FPS while profiling; the benchmark measures 46-63 at its views):

| What | ms/frame | Notes |
|---|---|---|
| FPS Booster `Updater.LateUpdate` (game LateUpdates) | 11.7-11.9 | Container: everything below runs inside it |
| of which **UIView.LateUpdate (the UI)** | **~9-10** | Measured 9.8 in the run that also timed UI components. That run's overhead pushed the frame to 29 ms, so ~8-9 ms is the honest figure. 4,254 UI components are live. |
| of which RenderManager.LateUpdate | 2.7-3.1 | Nets 0.6, buildings 0.5, terrain 0.2, props 0.1, vehicles 0.05. Trees are untimed (transpiled by a mod). |
| FPS Booster `Updater.Update` (game Updates) | 3.1-3.2 | UIComponent updates 0.7, UIInput 0.6, simulation 0.1 |
| Lumina dynamic-resolution pass (`OnRenderImage`) | 1.8 | The supersampling cost; changing it would change the look |
| All other mods' per-frame hooks | < 0.1 total | 39 MonoBehaviour/threading hooks |
| All other mods' Harmony prefixes/postfixes (635 timed) | < 0.2 total | Mods' patch code is not the problem |
| Not covered by any timer | ~6 | Unity engine: culling, render loop, present |

## What it means
- The earlier assumption ("mod per-frame overhead") is wrong for hooks and patches: together they cost under 0.3 ms.
- **The UI is the single largest main-thread cost**: about 40% of the frame, even in an empty city. The likely mechanism is that ColossalFramework UI rebuilds the whole UI's render data when any component is invalidated, so one element that changes every frame (a clock, a counter, an animated button) makes all 4,254 components rebuild every frame. **Not yet verified**; that is the next measurement.
- Game rendering prep (RenderManager) is about 3 ms. The GPU is half idle, so draw submission is not the limit at this city size.

## Caveats
- Profiled at the default start view, not the 5 bench positions.
- Profiler pass 2 crashed twice: patching other mods' prefixes on struct members (TreeControl's `TreeInstance.set_GrowState`, PropControl's `PropInstance.get_Position`) crashes Mono's JIT natively. Patches on value types are now skipped (see rejected.md).
- Don't run Bench in a session where profiler timers are installed: they stay on and add overhead.

## UI redraw attribution (2026-11-21 18:20, fresh Green Peaks city)
Tool: Mod Profiler's UI capture (`uiprofile.flag`, 10 s). It patches `UIComponent.Invalidate` and `UIView.Render`, and records which call switched the UI view from clean to dirty.

- The UI re-rendered on **535 of 535 frames**, at 1.80 ms per re-render. 28,607 UI components exist.
- **531 of those 535 re-renders were started by one button: Crossroads' toolbar toggle** (`CrossroadsToggleButton`). `Crossroads.Tool.ToggleButtonStyle.Apply` re-applies its style 5 times per frame, invalidating the button each time.
- The game's own per-frame invalidations (hotel-chain profit label, hidden industry and monument menu items) never dirtied the view: they're hidden, so they don't trigger a redraw.
- The other 4 re-renders came from real changes (WatchIt2 panel icons, a Chirper message).
- Expected saving if the button stops invalidating: at least the 1.8 ms re-render on most frames. UIView.LateUpdate measured ~9 ms in the frame profile, so part of the rest may also drop. Not yet measured.

## RAM snapshot (2026-11-21 21:45, fresh Green Peaks city, 0 custom assets)
Tool: Mod Profiler `memreport.flag` (process numbers read from outside, since Mono's `Process` reports 0 for the game).

| What | Size |
|---|---|
| Process private (committed) | 20.9 GB |
| Working set (actually in RAM) | 9.1 GB |
| Managed heap (C#) | 3.4 GB |
| Unity native allocations | 5.5 GB (5.8 reserved) |
| Committed but not in RAM | ~11.8 GB |

- **Most of the "20 GB" is committed, not resident.** Only 9 GB is really in RAM. The rest is reserved or paged out. The likely cause is that Mono's garbage collector never returns heap to Windows, so the peak during loading stays committed. It costs page-file space and commit charge, not physical RAM. Not yet proven.
- **Textures are the biggest resident item:** 34,840 Texture2D, about 5.4 GB at GPU size, even with 0 custom assets loaded. 1,529 of 1,742 sampled textures are readable, which means Unity keeps an extra RAM copy: **~3 GB of RAM copies**. 11,470 textures are 109 px, which is asset-thumbnail size (466 MB). 535 are 2048 px (2.0 GB).
- **Game data buffers: 0.5 GB** (largest: terrain detail arrays at 107 MB; trees raised to 1.8 M = 25 MB). Raised limits are not the RAM problem.
- **Mods' own objects: 0.8 GB.** Adaptive Roads 233 MB (a lane array of 262,144), Lumina 169 MB (render textures), Surface Painter 89 MB, TM:PE 79 MB, Skyve injections 71 MB, AutoPatcher 59 MB.
- Earlier crash: the first version of this snapshot read a pointer field by reflection and crashed the game; pointers are now skipped.

## Next UI culprit (same session)
With both guards on, the UI still re-renders every frame (514/514, 3.9 ms each). Now it's the game's `GeneratedScrollPanel.ShowSelectedIndex` re-selecting a button in the hidden Industries menu (512 of 514). Each guard so far uncovers the next hidden-panel invalidator.

## Guard quick A/B (same session, 20 s each, same spot, profiler timers on)
- Guards off: 26.0 ms average (38 FPS); slowest 1% 42.2 ms.
- Guards on: 24.9 ms average (40 FPS); slowest 1% 76.8 ms (one hitch in a 20 s sample).
- About 1 ms saved. The full ~4 ms re-render saving needs the redraw to stop entirely, so the next culprit has to be handled too.
