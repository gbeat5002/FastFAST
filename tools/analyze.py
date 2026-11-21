"""Summarize a Bench run: per-segment FPS, lows, thread/GPU load, bottleneck; screenshot SSIM.

Usage: python analyze.py <run dir> [--ref <reference run dir>]
SSIM: 7x7 uniform-window SSIM on luminance (same definition as scikit-image's default).
"""
import csv
import datetime as dt
import sys
from pathlib import Path

import numpy as np
from PIL import Image


def ssim(a: np.ndarray, b: np.ndarray, win: int = 7) -> float:
    c1, c2 = (0.01 * 255) ** 2, (0.03 * 255) ** 2

    def box(x):  # mean over win x win windows via 2-D cumulative sums ("valid" region)
        s = np.cumsum(np.cumsum(np.pad(x, ((1, 0), (1, 0))), 0), 1)
        return (s[win:, win:] - s[:-win, win:] - s[win:, :-win] + s[:-win, :-win]) / (win * win)

    ma, mb = box(a), box(b)
    va, vb, cov = box(a * a) - ma * ma, box(b * b) - mb * mb, box(a * b) - ma * mb
    n = win * win
    va, vb, cov = va * n / (n - 1), vb * n / (n - 1), cov * n / (n - 1)  # sample variances, as skimage does
    s = ((2 * ma * mb + c1) * (2 * cov + c2)) / ((ma * ma + mb * mb + c1) * (va + vb + c2))
    return float(s.mean())


def luma(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("L"), dtype=np.float64)


def parse_utc(s: str) -> dt.datetime:
    s = s.replace("Z", "+00:00")
    if "." in s:  # trim .NET's 7-digit fractions to Python's 6
        head, frac = s.split(".", 1)
        digits = "".join(ch for ch in frac if ch.isdigit())
        tz = frac[len(digits):]
        s = f"{head}.{digits[:6]}{tz}"
    return dt.datetime.fromisoformat(s).replace(tzinfo=None)


def main() -> None:
    run = Path(sys.argv[1])
    ref = Path(sys.argv[sys.argv.index("--ref") + 1]) if "--ref" in sys.argv else None
    segs = list(csv.DictReader(open(run / "segments.csv", encoding="utf-8")))
    sysrows = list(csv.DictReader(open(run / "system.csv", encoding="utf-8"))) if (run / "system.csv").exists() else []
    for r in sysrows:
        r["t"] = parse_utc(r["utc"])

    print(f"## Run {run.name}\n")
    print("| # | position | mode | avg FPS | 1% low | 0.1% low | main % | sim % | render % | GPU % | likely limit | valid |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for s in segs:
        t0, t1 = parse_utc(s["start_utc"]), parse_utc(s["end_utc"])
        rows = [r for r in sysrows if t0 <= r["t"] <= t1]

        def avg(key):
            vals = [float(r[key]) for r in rows if r[key] not in ("", None)]
            return sum(vals) / len(vals) if vals else float("nan")

        main_p, sim_p, ren_p, gpu_p = avg("main_pct"), avg("sim_pct"), avg("render_pct"), avg("gpu3d_pct")
        fps, low1 = float(s["avg_fps"]), float(s["low1_fps"])
        limits = []
        if main_p >= 90: limits.append("main thread")
        if ren_p >= 90: limits.append("render thread")
        if s["mode"] == "3x" and sim_p >= 90: limits.append("simulation")
        if gpu_p >= 90: limits.append("GPU")
        if fps > 0 and low1 < 0.5 * fps: limits.append("stutter")
        valid = "yes" if s["valid"] == "True" else f"NO ({s['why_invalid']})"
        print(f"| {s['segment']} | {s['position']} | {s['mode']} | {fps:.1f} | {low1:.1f} | {float(s['low01_fps']):.1f} | "
              f"{main_p:.0f} | {sim_p:.0f} | {ren_p:.0f} | {gpu_p:.0f} | {', '.join(limits) or 'none above 90%'} | {valid} |")

    print("\n### Screenshots (SSIM, 1.0 = identical)\n")
    print("| position | A vs B (noise floor) |" + (" vs reference |" if ref else ""))
    print("|---|---|" + ("---|" if ref else ""))
    for a in sorted(run.glob("shot_*_A.png")):
        pos = a.name[len("shot_"):-len("_A.png")]
        b = a.with_name(f"shot_{pos}_B.png")
        size = Image.open(a).size
        line = f"| {pos} ({size[0]}x{size[1]}) | {ssim(luma(a), luma(b)):.4f} |"
        if ref:
            r = ref / a.name
            line += f" {ssim(luma(a), luma(r)):.4f} |" if r.exists() else " missing |"
        print(line)


if __name__ == "__main__":
    main()
