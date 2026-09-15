#!/usr/bin/env python3
"""Does the loop actually hold, played twice?

Measuring the two ENDS of a file against each other says whether they match.  It does not say
whether a player will hear the join — music is full of moments where the sound changes a lot,
and a seam only stands out if it changes MORE than the music around it does.

So this plays the file twice: it concatenates the track with itself and measures spectral flux
— how much the sound changes from one 20 ms frame to the next — across the whole thing.  The
join gets the same measurement as every other moment, and the answer is a PERCENTILE: if the
wrap sits at the 60th percentile of the track's own transitions, it is an ordinary musical
moment and nobody will find it.  If it sits at the 100th, it is the single biggest change in
the track and everybody will.

Also reports the sample-level step at the wrap (a click is not a spectral event — it is a
discontinuity, and it is audible even when the spectrum matches perfectly).
"""
import array
import json
import math
import subprocess
import sys

FEAT = 8000
HOP = 160
BANDS = ((0.12, 0.7),)          # one-pole corners, ~150 Hz and ~1.5 kHz at 8 kHz


def decode(path, rate, channels):
    raw = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", path, "-map", "a:0", "-ac", str(channels),
         "-ar", str(rate), "-f", "s16le", "-"],
        capture_output=True, check=True).stdout
    a = array.array("h")
    a.frombytes(raw[: len(raw) // 2 * 2])
    return a


def db(x):
    return -99.0 if x <= 1e-9 else 20 * math.log10(x)


def features(mono):
    lo = mid = 0.0
    a1, a2 = BANDS[0]
    frames = []
    for start in range(0, len(mono) - HOP, HOP):
        e0 = e1 = e2 = 0.0
        for i in range(start, start + HOP):
            x = mono[i] / 32768.0
            lo += a1 * (x - lo)
            mid += a2 * (x - mid)
            e0 += lo * lo
            e1 += (mid - lo) ** 2
            e2 += (x - mid) ** 2
        frames.append((db(math.sqrt(e0 / HOP)), db(math.sqrt(e1 / HOP)), db(math.sqrt(e2 / HOP))))
    return frames


def flux(frames):
    """How much the sound changes from each frame to the next."""
    return [math.sqrt(sum((b - a) ** 2 for a, b in zip(frames[i], frames[i + 1])))
            for i in range(len(frames) - 1)]


def verify(path):
    mono = decode(path, FEAT, 1)
    n = len(mono)

    # The file played twice, so the wrap is an ordinary moment in the middle of a signal.
    twice = array.array("h", mono)
    twice.extend(mono)
    frames = features(twice)
    changes = flux(frames)

    join = n // HOP - 1                      # the frame pair straddling the wrap
    window = changes[max(0, join - 1):join + 2]
    at_join = max(window) if window else 0.0

    # …compared against every other transition in the same music.
    others = changes[:join - 2] + changes[join + 3:]
    others_sorted = sorted(others)
    rank = sum(1 for v in others_sorted if v < at_join)
    percentile = 100.0 * rank / max(1, len(others_sorted))
    median = others_sorted[len(others_sorted) // 2]

    # A click is not a spectral event. Measure the raw step across the wrap at full rate.
    full = decode(path, 48000, 1)
    step = abs(full[0] - full[-1]) / 32768.0
    typical_step = sum(abs(full[i + 1] - full[i]) for i in range(0, min(len(full) - 1, 480000), 7)) \
        / max(1, len(range(0, min(len(full) - 1, 480000), 7))) / 32768.0

    return {
        "file": path.split("/")[-1],
        "seconds": round(n / FEAT, 2),
        "join_flux_db": round(at_join, 2),
        "median_flux_db": round(median, 2),
        "join_percentile": round(percentile, 1),
        "step": round(step, 5),
        "typical_step": round(typical_step, 5),
        "verdict": (
            "inaudible" if percentile < 90 else
            "noticeable" if percentile < 99 else "AUDIBLE SEAM"),
    }


if __name__ == "__main__":
    rows = [verify(p) for p in sys.argv[1:]]
    for r in rows:
        print(json.dumps(r), flush=True)
    json.dump(rows, open("verify.json", "w"), indent=2)
