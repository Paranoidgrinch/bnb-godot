#!/usr/bin/env python3
"""A harder look at the tracks whose first loop was only just good enough.

The broad sweep steps the loop point every 250 ms and only considers the opening third of a
track as a place to start.  That is the right shape for a first pass over ten tracks and it is
too coarse for the four that came back near the audibility line: a loop point half a second
from a good one measures badly, and a track whose second section is the one that repeats has
no good `s` in its opening third at all.

So these get the expensive version — a finer step, a wider place to look, and several loop
lengths — and keep whatever measures best.  Everything else about the method is unchanged; this
is more search, not different search.
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

# Every track the broad sweep left at or above ~85, plus the one whose loop was good but short.
HARD = {
    "victoriana_loop":      {"src": "src/victoriana_loop.mp3",      "lens": [24, 32, 45, 60]},
    "dark_chamber":         {"src": "src/dark_chamber.mp3",         "lens": [45, 60, 90, 120]},
    "waystone_inn":         {"src": "src/waystone_inn.wav",         "lens": [45, 60, 90, 120]},
    "dark_descent":         {"src": "src/dark_descent.mp3",         "lens": [20, 26, 32, 40]},
    "forest_whisper_theme": {"src": "src/forest_whisper_theme.wav", "lens": [30, 40, 50, 60]},
    "eternal_sands":        {"src": "src/eternal_sands.wav",        "lens": [30, 40, 55, 70]},
}

CROSSFADES = [0.6, 1.2, 2.5]
STEP_S = 0.08          # ~three times finer than the broad sweep
S_FRAC = 0.55          # a loop may start past the opening third
LEVEL_TOL = 4.5        # a slightly wider dynamic window, since the step is finer


def run(payload):
    out = subprocess.run([sys.executable, f"{HERE}/loopsmith.py", json.dumps(payload)],
                         capture_output=True, text=True)
    return json.loads(out.stdout.strip().splitlines()[-1]) if out.returncode == 0 else None


def verify(path):
    out = subprocess.run([sys.executable, f"{HERE}/verify.py", path],
                         capture_output=True, text=True, check=True)
    return json.loads(out.stdout.strip().splitlines()[0])


def main(names):
    os.makedirs("cand", exist_ok=True)
    picks = {}
    for name in names:
        cfg = HARD[name]
        tries = []
        for min_loop in cfg["lens"]:
            for cross in CROSSFADES:
                path = f"cand/{name}__deep_{min_loop}_{cross}.wav"
                made = run({"src": cfg["src"], "out_path": path, "mode": "loop",
                            "min_loop_s": min_loop, "cross_s": cross, "step_s": STEP_S,
                            "s_frac": S_FRAC, "level_tol": LEVEL_TOL})
                if made is None:
                    continue
                score = verify(path)
                tries.append({**made, **score, "path": path, "cross_s": cross,
                              "min_loop_s": min_loop})
                print(f"  {name:22} min={min_loop:3} xf={cross:<4} len={made['loop_seconds']:7.1f}s"
                      f"  p={score['join_percentile']:5.1f}  {score['verdict']}", flush=True)
        if not tries:
            print(f"  {name:22} NOTHING RENDERED", flush=True)
            continue
        best = min(tries, key=lambda t: (round(t["join_percentile"] * 2),
                                         -t["loop_seconds"],
                                         abs(t["cross_s"] - 1.2)))
        picks[name] = best
        print(f"→ {name:22} PICK min={best['min_loop_s']} xf={best['cross_s']} "
              f"{best['loop_seconds']}s p={best['join_percentile']} {best['verdict']}\n", flush=True)
    existing = json.load(open("deep.json")) if os.path.exists("deep.json") else {}
    existing.update(picks)
    json.dump(existing, open("deep.json", "w"), indent=2)


if __name__ == "__main__":
    main(sys.argv[1:] or list(HARD))
