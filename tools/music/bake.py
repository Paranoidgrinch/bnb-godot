#!/usr/bin/env python3
"""The last pass: one loudness for all ten tracks, then Ogg Vorbis for the game.

The sources arrive between −4.7 and −29.8 LUFS — a 25 dB spread, which is the difference
between a boss fight that hurts and a forest theme nobody can hear.  Every runtime file is
therefore measured and corrected to the SAME integrated loudness, so the game's own Music
Volume slider is the only thing that changes how loud the music is.

Two passes, not one: ffmpeg's `loudnorm` in single-pass mode is a dynamic compressor that
guesses as it goes, and it will pump a quiet passage up mid-phrase.  Measuring first and then
applying the measurement makes it a flat gain with a limiter, which leaves the music's own
dynamics alone — which matters here, because these loops are 40 to 280 seconds long and any
pumping is something the player hears every time round.

⚠ THE NORMALISATION MUST NOT TOUCH THE LOOP POINT.  A flat gain cannot, but a compressor whose
attack envelope differs at the start and the end of the file absolutely can, and it would undo
the seam work by making the first second quieter than the last.  That is the second reason for
two-pass: `linear=true` is a constant gain, and the verification pass afterwards re-measures
the seam on the FINAL .ogg to prove it.
"""
import json
import os
import re
import subprocess
import sys

TARGET_LUFS = -16.0     # the usual resting point for game music under dialogue and effects
TRUE_PEAK = -1.5        # headroom, so a lossy encoder cannot push a peak over 0 dBFS
TARGET_LRA = 11.0
QUALITY = "5"           # Vorbis q5 ≈ 160 kbit/s stereo: transparent for this material


def measure(path):
    """What loudnorm would need to know to do its job as a flat gain."""
    out = subprocess.run(
        ["ffmpeg", "-v", "info", "-i", path, "-map", "a:0", "-af",
         f"loudnorm=I={TARGET_LUFS}:TP={TRUE_PEAK}:LRA={TARGET_LRA}:print_format=json",
         "-f", "null", "-"],
        capture_output=True, text=True).stderr
    blob = out[out.rfind("{"):out.rfind("}") + 1]
    return json.loads(blob)


def bake(src, out_path):
    m = measure(src)
    args = (f"loudnorm=I={TARGET_LUFS}:TP={TRUE_PEAK}:LRA={TARGET_LRA}"
            f":measured_I={m['input_i']}:measured_TP={m['input_tp']}"
            f":measured_LRA={m['input_lra']}:measured_thresh={m['input_thresh']}"
            f":offset={m['target_offset']}:linear=true:print_format=summary")
    subprocess.run(
        ["ffmpeg", "-v", "error", "-y", "-i", src, "-map", "a:0", "-af", args,
         "-ar", "48000", "-ac", "2", "-c:a", "libvorbis", "-q:a", QUALITY, out_path],
        check=True)
    return {"input_lufs": float(m["input_i"]), "input_tp": float(m["input_tp"])}


def verify_loudness(path):
    out = subprocess.run(
        ["ffmpeg", "-v", "info", "-i", path, "-map", "a:0", "-af", "ebur128=peak=true",
         "-f", "null", "-"], capture_output=True, text=True).stderr
    tail = out[out.rfind("Integrated loudness"):]

    def grab(label):
        m = re.search(re.escape(label) + r":\s*(-?[\d.]+|-inf)", tail)
        return float(m.group(1)) if m and m.group(1) != "-inf" else None

    return {"lufs": grab("I"), "peak_dbtp": grab("Peak"), "lra": grab("LRA")}


def main():
    plan = json.load(open(sys.argv[1]))          # name -> {"path": rendered wav, ...}
    out_dir = sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    rows = []
    for name, entry in plan.items():
        out_path = os.path.join(out_dir, entry["out"])
        before = bake(entry["path"], out_path)
        after = verify_loudness(out_path)
        row = {"name": name, "file": entry["out"],
               "from_lufs": before["input_lufs"], "from_tp": before["input_tp"],
               "lufs": after["lufs"], "peak_dbtp": after["peak_dbtp"], "lra": after["lra"],
               "bytes": os.path.getsize(out_path)}
        rows.append(row)
        print(f"  {entry['out']:44} {before['input_lufs']:7.1f} → {after['lufs']:6.1f} LUFS  "
              f"peak {after['peak_dbtp']:5.1f} dBTP  {row['bytes'] / 1e6:5.2f} MB", flush=True)
    json.dump(rows, open("baked.json", "w"), indent=2)

    spread = max(r["lufs"] for r in rows) - min(r["lufs"] for r in rows)
    print(f"\nloudness spread across all {len(rows)} tracks: {spread:.2f} dB")
    if spread > 1.0:
        print("⚠ the tracks are NOT at one volume")


if __name__ == "__main__":
    main()
