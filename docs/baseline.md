# Baseline (Phase 0)

## Current baseline: run 3, 20261121_163117
New game on Green Peaks (stock map, nothing built; the map's own highway traffic runs at 3x), 2560x1440, current mod set after Phase 1 change 1 (six error-producing mods excluded, see changelog.md). 60 s per segment, timed in-game by the Bench mod; thread and GPU load sampled once per second by `tools/sample.ps1`. Positions are re-picked on the real terrain and frozen in `Bench\positions.txt`.

| # | position | mode | avg FPS | 1% low | 0.1% low | main % | sim % | render % | GPU % | likely limit |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | street | paused | 52.6 | 40.8 | 37.3 | 99 | 1 | 15 | 44 | main thread |
| 1 | street | 3x | 48.7 | 38.2 | 36.5 | 99 | 48 | 30 | 42 | main thread |
| 2 | downtown_mid | paused | 51.0 | 26.2 | 6.4 | 99 | 1 | 16 | 46 | main thread |
| 3 | downtown_mid | 3x | 49.7 | 42.0 | 39.0 | 100 | 51 | 29 | 51 | main thread |
| 4 | zoomed_out | paused | 63.2 | 53.7 | 48.6 | 100 | 1 | 13 | 49 | main thread |
| 5 | zoomed_out | 3x | 57.3 | 46.9 | 43.9 | 99 | 52 | 29 | 49 | main thread |
| 6 | interchange | paused | 52.0 | 45.0 | 41.4 | 100 | 1 | 21 | 52 | main thread |
| 7 | interchange | 3x | 46.5 | 10.1 | 1.0 | 99 | 53 | 30 | 48 | main thread, hitch |
| 8 | dense_assets | paused | 54.5 | 47.2 | 41.5 | 99 | 1 | 16 | 52 | main thread |
| 9 | dense_assets | 3x | 49.0 | 40.0 | 38.4 | 99 | 51 | 29 | 48 | main thread |

- Map load (LSM "total loading time"): 1:15. Peak game RAM 18.9 GB (LSM), private bytes up to 21.6 GB, with 0 custom assets loaded.
- Screenshot noise floor (A vs B SSIM): 0.9997-1.0000. All 5 shots show real content (forest and rocks, highway over the river, interchange, whole-map view). The "street" view ended up under the highway bridge looking up; it is repeatable, so it stays.

### What it says
- **The main thread is the limit in every segment** (99-100% of one core). The GPU sits at 42-52% and the simulation thread at about 50% at 3x, so neither is the bottleneck.
- Even an empty city on this mod set holds 18-22 GB of RAM. That's the mods, not assets.
- Targets missed: average 46-63 FPS (target 100+), 1% lows 10-54 (target 60+). The worst hitch was interchange at 3x (0.1% low 1.0 FPS).

## Earlier runs (invalid scene)
- Run 1 (20261121_153338) and run 2 (20261121_155738): main-thread bound at 52-60 FPS, but **the map never loaded**. Terrain was flat at 60 m with no water, and screenshots were uniform black or white. The cause was More Nodes/Segments (3818403598), which hooked map deserialization; with it excluded the map loads (run 3). Load time went from 4:26 to 1:15.
- Run 1 also had every position stacked at the origin, and night lighting. The bench now pins the clock to 12:00 and spreads fallback positions.
