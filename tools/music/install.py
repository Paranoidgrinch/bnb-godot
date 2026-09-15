#!/usr/bin/env python3
"""Assemble the ten runtime files and put them in the game.

Takes the best candidate for each track (the broad sweep's winners.json, overridden by the
harder second look in deep.json where one was run), normalises every one of them to the same
loudness, encodes to Ogg Vorbis, and writes them into the Godot project under the names the
integration document asks for.

It does NOT set the loop flag — that lives in each file's `.import`, which Godot writes, so the
flag is patched after the editor has imported them (see the shell steps in the session log).
The point of keeping that separate is that a re-import must not be able to quietly undo it: the
`--smoke-music` probe reads the flag back out of the loaded stream and fails if it is gone.
"""
import json
import os
import shutil
import subprocess
import sys

# state -> (candidate key, the name the game loads it by)
RUNTIME = {
    "title":    ("secret_sanctum",         "music_title_secret_sanctum.ogg"),
    "campfire": ("waystone_inn",           "music_campfire_waystone_inn.ogg"),
    "shop":     ("savvy_merchant",         "music_shop_savvy_merchant.ogg"),
    "act1":     ("victoriana_loop",        "music_act1_victoriana_loop.ogg"),
    "act2":     ("dark_chamber",           "music_act2_dark_chamber.ogg"),
    "act3":     ("forest_whisper_theme",   "music_act3_forest_whisper_theme.ogg"),
    "act4":     ("eternal_sands",          "music_act4_eternal_sands.ogg"),
    "elite":    ("dark_descent",           "music_elite_dark_descent.ogg"),
    "boss":     ("desecrated_temple",      "music_boss_desecrated_temple.ogg"),
    "act5":     ("land_of_the_great_gods", "music_act5_land_of_the_great_gods.ogg"),
}


def main():
    out_dir = sys.argv[1]
    winners = json.load(open("winners.json"))
    deep = json.load(open("deep.json")) if os.path.exists("deep.json") else {}

    plan = {}
    for state, (key, filename) in RUNTIME.items():
        # ⚠ THE HARDER SEARCH IS NOT ALWAYS THE BETTER ONE. A finer step and a wider place to look let the
        # search reach loop points the broad sweep could not — and also let it walk past the one the broad
        # sweep found, because the length bonus is weighed against a score that is now computed over
        # different candidates. dark_descent came back WORSE from the deep pass (96.1 against 88.9). So the
        # two are compared on the only thing that matters and the better one ships, whichever produced it.
        candidates = [(winners[key], "sweep")]
        if key in deep:
            candidates.append((deep[key], "deep"))
        chosen, origin = min(candidates, key=lambda c: (round(c[0]["join_percentile"] * 2),
                                                        -c[0]["loop_seconds"]))
        plan[key] = {"path": chosen["path"], "out": filename,
                     "state": state,
                     "from": origin,
                     "runner_up": None if len(candidates) == 1 else
                                  f"{[c for c in candidates if c[1] != origin][0][0]['join_percentile']}"
                                  f" from the {[c[1] for c in candidates if c[1] != origin][0]} pass",
                     "loop_seconds": chosen["loop_seconds"],
                     "join_percentile": chosen["join_percentile"],
                     "verdict": chosen["verdict"]}
    json.dump(plan, open("install-plan.json", "w"), indent=2)

    print("the chosen cut for each state")
    for key, entry in plan.items():
        runner = f"  (beat {entry['runner_up']})" if entry["runner_up"] else ""
        print(f"  {entry['state']:9} {key:22} {entry['loop_seconds']:7.1f}s  "
              f"p={entry['join_percentile']:5.1f}  {entry['verdict']:10} ({entry['from']}){runner}")

    print("\nnormalising and encoding")
    subprocess.run([sys.executable, "tools/bake.py", "install-plan.json", out_dir], check=True)


if __name__ == "__main__":
    main()
