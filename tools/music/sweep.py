#!/usr/bin/env python3
"""Try every reasonable way to loop each track, and keep the one that measures best.

The crossfade length is the one real lever (too short and the join is a bump, too long and the
music smears), and for the four tracks that do not fade out it is genuinely unclear whether
they are better TRIMMED at their own ends or CUT to a loop found inside them.  Rather than
decide either by ear-less guesswork, every candidate is rendered and scored by verify.py, and
the winner is the one whose wrap is least remarkable against its own music.

Ties on the percentile go to the longer loop: a player hears a 40-second loop three times as
often as a 120-second one.
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CROSSFADES = [0.25, 0.6, 1.2, 2.5]

# What the measurement pass (analysis.json) found, turned into candidates worth rendering.
PLAN = {
    "secret_sanctum":          {"src": "src/secret_sanctum.wav",        "modes": ["loop"], "min_loop_s": 60},
    "waystone_inn":            {"src": "src/waystone_inn.wav",          "modes": ["loop"], "min_loop_s": 60},
    "savvy_merchant":          {"src": "src/savvy_merchant.ogg",        "modes": ["loop"], "min_loop_s": 45},
    "victoriana_loop":         {"src": "src/victoriana_loop.mp3",       "modes": ["loop", "trim"], "min_loop_s": 45,
                                "head_s": 0.03, "tail_s": 0.01},
    "dark_chamber":            {"src": "src/dark_chamber.mp3",          "modes": ["loop", "trim"], "min_loop_s": 60,
                                "head_s": 0.05, "tail_s": 0.02},
    "forest_whisper_theme":    {"src": "src/forest_whisper_theme.wav",  "modes": ["trim", "loop"], "min_loop_s": 40,
                                "head_s": 0.0, "tail_s": 0.0},
    "eternal_sands":           {"src": "src/eternal_sands.wav",         "modes": ["loop", "trim"], "min_loop_s": 45,
                                "head_s": 0.0, "tail_s": 0.0},
    "dark_descent":            {"src": "src/dark_descent.mp3",          "modes": ["loop"], "min_loop_s": 30},
    "desecrated_temple":       {"src": "src/desecrated_temple.mp3",     "modes": ["loop"], "min_loop_s": 60},
    "land_of_the_great_gods":  {"src": "src/land_of_the_great_gods.ogg", "modes": ["loop"], "min_loop_s": 60},
}


def run(module, payload):
    out = subprocess.run([sys.executable, f"{HERE}/{module}", json.dumps(payload)],
                         capture_output=True, text=True)
    if out.returncode != 0:
        return None
    return json.loads(out.stdout.strip().splitlines()[-1])


def verify(path):
    out = subprocess.run([sys.executable, f"{HERE}/verify.py", path],
                         capture_output=True, text=True, check=True)
    return json.loads(out.stdout.strip().splitlines()[0])


def main():
    os.makedirs("cand", exist_ok=True)
    winners, all_tries = {}, {}
    for name, cfg in PLAN.items():
        tries = []
        for mode in cfg["modes"]:
            # A trim keeps the author's own join and has no crossfade to tune — one candidate,
            # not four identical ones.
            for cross in (CROSSFADES if mode == "loop" else [0.0]):
                path = f"cand/{name}__{mode}_{cross}.wav"
                payload = {"src": cfg["src"], "out_path": path, "mode": mode,
                           "min_loop_s": cfg.get("min_loop_s", 45), "cross_s": cross,
                           "head_s": cfg.get("head_s", 0.0), "tail_s": cfg.get("tail_s", 0.0)}
                made = run("loopsmith.py", payload)
                if made is None:
                    continue
                score = verify(path)
                tries.append({**made, **score, "path": path, "cross_s": cross})
                print(f"  {name:24} {mode:5} xf={cross:<5} "
                      f"len={made['loop_seconds']:7.1f}s  p={score['join_percentile']:5.1f}  "
                      f"{score['verdict']}", flush=True)
        if not tries:
            print(f"  {name:24} NOTHING RENDERED", flush=True)
            continue
        # Lowest percentile wins.  Within half a percentile of each other the longer loop does
        # — a player hears a 40-second loop three times as often as a 120-second one — and
        # among those, the crossfade nearest 1.2 s: long enough to forgive the small musical
        # differences a three-band measurement cannot see, short enough not to smear the music.
        best = min(tries, key=lambda t: (round(t["join_percentile"] * 2),
                                         -t["loop_seconds"],
                                         abs(t["cross_s"] - 1.2)))
        winners[name] = best
        all_tries[name] = tries
        print(f"→ {name:24} PICK {best['mode']} xf={best['cross_s']} "
              f"{best['loop_seconds']}s p={best['join_percentile']} {best['verdict']}\n", flush=True)
    json.dump(winners, open("winners.json", "w"), indent=2)
    json.dump(all_tries, open("sweep-all.json", "w"), indent=2)


if __name__ == "__main__":
    main()
