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
