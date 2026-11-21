# Rejected / failed attempts

## Profiler: timing every Harmony patch method (2026-11-21)
- Try 1: crashed the game (native Mono JIT crash in `RuntimeMethodHandle.GetFunctionPointer`) while re-detouring TreeControl's prefix on `TreeInstance.set_GrowState`.
- Try 2 (auto-skip the last crashing label): crashed on PropControl's prefix on `PropInstance.get_Position`.
- Try 3 (skip every patch whose original is a member of a value type): worked, 635 patch methods timed.
- Kept: try 3's rule. Lesson: Harmony patches on struct accessors can't be safely re-patched at runtime, and a try/catch doesn't help because the crash is native.

## Load profiler UnpatchAll (earlier, 2026-11-20)
- Unpatching timers mid-session crashed the game (KERNELBASE c0000005). Timers are now never removed; they stop recording instead.
