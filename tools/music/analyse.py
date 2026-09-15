#!/usr/bin/env python3
"""What each source track actually is, before anything is decided about it.

Reports, per track: duration, integrated loudness (EBU R128), true peak, how much digital
silence sits at each end, and — the question that matters for looping — how badly the END
joins back onto the START.  Nothing is written; this only looks.
"""
import array, json, math, os, re, subprocess, sys

RATE = 48000


def decode(path, rate=RATE):
    """The file as one mono signal of floats in [-1, 1]."""
    raw = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", path, "-map", "a:0", "-ac", "1",
         "-ar", str(rate), "-f", "s16le", "-"],
        capture_output=True, check=True).stdout
    a = array.array("h")
    a.frombytes(raw)
    return [s / 32768.0 for s in a]


def loudness(path):
    """Integrated LUFS / LRA / true peak, straight from ffmpeg's R128 meter."""
    out = subprocess.run(
        ["ffmpeg", "-v", "info", "-i", path, "-map", "a:0",
         "-af", "ebur128=peak=true", "-f", "null", "-"],
        capture_output=True, text=True).stderr
    tail = out[out.rfind("Integrated loudness"):]
    def grab(label):
        m = re.search(re.escape(label) + r":\s*(-?[\d.]+|-inf)", tail)
        return float(m.group(1)) if m and m.group(1) != "-inf" else None
    return {"lufs": grab("I"), "lra": grab("LRA"), "peak_dbtp": grab("Peak")}


def db(x):
    return -99.0 if x <= 1e-9 else 20 * math.log10(x)


def rms(xs):
    return math.sqrt(sum(v * v for v in xs) / len(xs)) if xs else 0.0


def edge_silence(sig, floor=-60.0):
    """Seconds of near-silence at head and tail, measured in 10 ms frames."""
    hop = RATE // 100
    frames = [db(rms(sig[i:i + hop])) for i in range(0, len(sig) - hop, hop)]
    head = next((i for i, d in enumerate(frames) if d > floor), len(frames))
    tail = next((i for i, d in enumerate(reversed(frames)) if d > floor), len(frames))
    return head / 100.0, tail / 100.0, frames


def bands(xs):
    """Coarse spectral shape: energy in four bands, via cheap one-pole splits.

    Enough to notice that a seam changes TIMBRE and not only level, which is what makes a
    loop audible even when the two ends are the same loudness.
    """
    lo, mid, out = 0.0, 0.0, []
    a1, a2 = 0.02, 0.2          # ~150 Hz and ~1.5 kHz corners at 48 kHz
    e = [0.0] * 4
    for x in xs:
        lo += a1 * (x - lo)
        mid += a2 * (x - mid)
        e[0] += lo * lo
        e[1] += (mid - lo) ** 2
        e[2] += (x - mid) ** 2
    n = max(1, len(xs))
    return [db(math.sqrt(v / n)) for v in e[:3]]


def seam(sig, window=0.10):
    """How the last moment of the track joins the first moment of it."""
    w = int(RATE * window)
    head, tail = sig[:w], sig[-w:]
    step = abs(sig[0] - sig[-1])
    hb, tb = bands(head), bands(tail)
    return {
        "step_db": round(db(step), 1),
        "head_db": round(db(rms(head)), 1),
        "tail_db": round(db(rms(tail)), 1),
        "level_gap_db": round(db(rms(tail)) - db(rms(head)), 1),
        "band_gap_db": [round(t - h, 1) for h, t in zip(hb, tb)],
    }


def ending(frames, seconds=4.0):
    """Does the track FADE OUT?  A falling envelope over the last seconds means the file has
    a real ending, and an ending cannot be crossfaded into a loop — it needs a loop point."""
    n = int(seconds * 100)
    tail = [d for d in frames[-n:] if d > -99]
    if len(tail) < 20:
        return None
    first, last = sum(tail[:10]) / 10, sum(tail[-10:]) / 10
    return round(last - first, 1)


def main(paths):
    report = []
    for p in paths:
        sig = decode(p)
        head, tail, frames = edge_silence(sig)
        row = {
            "file": os.path.basename(p),
            "seconds": round(len(sig) / RATE, 2),
            "silence_head_s": head,
            "silence_tail_s": tail,
            "tail_slope_db": ending(frames),
            **loudness(p),
            "seam": seam(sig),
        }
        report.append(row)
        print(json.dumps(row), flush=True)
    json.dump(report, open("analysis.json", "w"), indent=2)


if __name__ == "__main__":
    main(sys.argv[1:])
