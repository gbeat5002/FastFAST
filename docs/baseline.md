# Baseline (Phase 0)

## Run 1: 20261121_153338 (provisional)
New game on a stock map, empty city, 2560x1440, all current mods. 60 s per segment, timed in-game by the Bench mod; thread and GPU load sampled once per second by `tools/sample.ps1`.

| # | position | mode | avg FPS | 1% low | 0.1% low | main % | sim % | render % | GPU % | likely limit |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | street | paused | 53.9 | 41.6 | 35.2 | 100 | 1 | 13 | 44 | main thread |
| 1 | street | 3x | 57.7 | 46.8 | 37.4 | 99 | 1 | 12 | 51 | main thread |
| 2 | downtown_mid | paused | 57.9 | 49.9 | 46.2 | 100 | 1 | 12 | 56 | main thread |
| 3 | downtown_mid | 3x | 56.8 | 28.4 | 5.7 | 99 | 0 | 13 | 57 | main thread |
| 4 | zoomed_out | paused | 54.5 | 38.6 | 29.7 | 100 | 1 | 13 | 49 | main thread |
| 5 | zoomed_out | 3x | 53.6 | 46.3 | 43.0 | 100 | 0 | 13 | 51 | main thread |
| 6 | interchange | paused | 52.6 | 40.8 | 34.0 | 100 | 1 | 13 | 54 | main thread |
| 7 | interchange | 3x | 52.7 | 38.9 | 29.5 | 99 | 1 | 14 | 55 | main thread |
| 8 | dense_assets | paused | 52.8 | 42.5 | 37.7 | 100 | 1 | 14 | 54 | main thread |
| 9 | dense_assets | 3x | 52.8 | 40.7 | 36.2 | 99 | 1 | 14 | 54 | main thread |

### What it says
- **The main thread is the limit everywhere** (99-100% of one core) while the GPU sits at 44-57% and the simulation thread is idle. This is with nothing built, so the cost is per-frame work by the game and mods, not city size.
- Targets missed: average 52-58 FPS (target 100+), 1% lows 28-50 (target 60+).
- One stutter spike: downtown_mid 3x, 0.1% low 5.7 FPS.

### Why it is provisional
- **Reference screenshots are unusable.** The city was at night, so 3 of 5 shots are near-black (mean luma 11/255) and the other 2 show only night sky. SSIM on near-black frames is ~1.0 whatever changes, so they can't guard visual quality.
- **All 5 positions were the same point** (0, 60, 0), differing only in angle and zoom, because the empty city had no buildings, nodes or props to pick from.
- Fixes in the Bench mod (not yet installed, the game was running): the clock is set to 12:00 before every screenshot and segment, and empty-city positions fall back to three separate map points with a steeper street-level pitch. Run 2 re-picks positions and replaces this table as the baseline.
- A mod's IMGUI "Area Fill" button stays visible with the UI hidden. It is identical in every shot, so it doesn't affect SSIM.

## Run 2: 20261121_155738 (invalid scene)
Same setup, fixed bench (clock pinned to 12:00, positions spread). Results match run 1: every segment main-thread bound (98-100%), GPU 46-61%, average 52-60 FPS, 1% lows 40-50 in clean segments. Two segments voided for focus loss; dense_assets paused had a 0.1% low of 4.9 FPS.

**The screenshots are still unusable, and the reason is the scene itself: the map never loads.** Terrain samples are exactly 60 m at every position, there is no water, and the frame is uniform black at night and uniform white at noon. The likely cause is More Network Capacity (Renew), Workshop 3818403598, installed 2026-11-21 01:45. It hooks the game's deserialization stream and logs "Suppressed BeginDeserialize('TerrainManager') tag mismatch (read 'TerrainManager')", and the same for WaterSimulation, while the map loads. Not yet confirmed by disabling it.

Until a map loads correctly, neither run is a valid baseline: an empty flat plane draws far less than a real map.
