# Changelog

## 2026-11-21: Phase 1, change 1: exclude error-producing mods
User instruction: disable bad mods and assets that cause crashes and errors, by default.

Excluded in Skyve (`Skyve\ModConfig.xml`, `Excluded=true`; previous file in `backup/2026-11-21/Skyve/ModConfig.before-error-mods.xml`). Nothing was unsubscribed or deleted.

| Mod | Workshop | Evidence |
|---|---|---|
| More Nodes/Segments v5.1.7 (MoreNetworkCapacityRenew) | 3818403598 | Prime suspect for the map not loading: hooks deserialization and logs "Suppressed BeginDeserialize('TerrainManager') tag mismatch" for terrain and water. Also suppresses other mods' exceptions and popups, so it hides errors. Installed 2026-11-21 01:45. |
| Real Glass & Water 2.0.1 | 3823419476 | RoomFitMap NullReferenceException; hooks terrain and water draw calls |
| Curb Height Adjuster 1.7 | 2674842365 | NullReferenceException popup on every load |
| Advanced Vehicle Options 1.9.15 | 1548831935 | About 16,384 errors per load (earlier session) |
| One-Way Train Tracks | 724382534 | "Rail1LElevatedPillar prop not found" exceptions (missing dependency asset) |
| Railway Replacer | 1530376523 | InvalidOperationException on load |

**Save risk:** More Nodes/Segments enlarges the network buffers, and One-Way Train Tracks adds networks. A save that relies on either can lose roads or tracks if it is loaded and *saved* without them. Don't save a big city until this is checked; re-enable by setting `Excluded` back to false.

Assets: the LSM report for the main city (fdnhbdf) has no failed assets and one asset error (Torre De Las Americas, invalid LOD texture size 216x512, which renders fine). So no assets were disabled. The 54 missing assets are unsubscribed items the city uses, which is a subscription question, not a bad-asset one.

Result (verified in game, new Green Peaks city):
- The map loads correctly: terrain, river, forests and highway are back. In runs 1-2 it was a flat plane at 60 m with no water.
- Map load time went from 4:26 to 1:15. The game log shrank from 300k to 146k lines.
- No error popups. One remaining mod exception: Japanese Traffic Lights (810355214) can't find its own light props on a fresh map, because LSM "load used assets only" skips them. It is kept, since cities that use those lights load the props.
- Follow-up (user request after seeing its error popup): Japanese Traffic Lights (810355214) also excluded. Cities that use its traffic lights will show default lights or missing props until it is re-enabled.
- Kept by user choice: BOB (2197863850). Its "unable to load random template assets" popup on fresh maps is a warning only: LSM "load used assets" skips BOB's bundled template .crp files when a city doesn't use them. Cities that use BOB random trees/props load them.
- FPS is not comparable to runs 1-2, which rendered an empty plane. Run 3 is the new baseline (baseline.md).

## 2026-11-21: Phase 2, change 1: UI Redraw Guard (mod `UiRedrawGuard/`)
- Crossroads guard: skips `ToggleButtonStyle.Apply` when its arguments match the last call for that button. Verified in game with the UI capture: Crossroads went from starting 531 of 535 UI re-renders to none.
- Hotel guard: skips the game's `HotelChainPanel` per-frame update while the panel is hidden. It was the next re-render source once the Crossroads guard was on (566 of 590 frames). Installed; the re-render capture with it on has not been run yet.
- Both default on, each with an options checkbox (`UiRedrawGuard.txt`). Nothing visible changes: the skipped calls re-applied identical state or refreshed a hidden panel.
- FPS gain: **not measured yet.** Run 4 (both on, before the weather fix) is not comparable: random overcast weather darkened the frame. A guards-off run was stopped when the game closed. Bench now pins clear weather. Next measurement: a quick same-spot A/B (20 s off, 20 s on), not the full benchmark.

## 2026-11-21: Phase 2, change 2: free RAM copies of tooltip and thumbnail atlases (On-Demand Assets `TextureTrim`)
- What: after a city loads, `Texture2D.Apply(false, true)` (drop the CPU copy, keep the GPU copy) on the DLC hover-tooltip atlases and the build-menu thumbnail atlases. Other UI atlases stay readable, since mods copy sprites out of them. Option: On-Demand Assets settings → "Free RAM copies of tooltip and thumbnail images", or `trim-ui-atlases=off` in `OnDemandAssets.txt`.
- Found by: memory snapshot. 3.4 GB of texture RAM copies in a fresh city; 714 MB in thumbnail atlases and ~650 MB in tooltip atlases.
- Result (fresh Green Peaks city, short test): freed 68 atlases, **~1.2 GB**. Working set one minute after load: 7.9 GB, against 9.1 GB in the earlier session without it. That's a cross-session comparison, not an A/B in one session.
- Checks: no exceptions in the log after the trim or after opening the Roads menu. Build-menu thumbnails display normally. Visuals unchanged by design (the GPU copy is what's drawn).
- Not done: the other ~2 GB of RAM copies (Procedural Objects' sidewalk texture pack ~0.6 GB, ~24,000 small unattributed images). They're riskier, because their owners may read pixels later.
- Also: excluded the second Japanese Traffic Lights mod (809633246), which showed the same error popup as 810355214. The log now has zero mod exceptions on a fresh city.
