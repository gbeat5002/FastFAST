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
