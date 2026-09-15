#!/usr/bin/env python3
"""Turn a track that ENDS into a track that LOOPS.

Six of the ten tracks BnB uses fade out — they were written with an ending, and an ending
cannot be crossfaded into a loop.  What they need is a loop POINT: a moment `s` early in the
music and a moment `e` later in it that sound like the same moment, so the music can be cut to
[s, e) and wrap from e back to s without a player hearing the join.

The method, in three passes, coarse to fine:

  1. FEATURES.  The track is decoded once to 8 kHz mono and reduced to a frame every 20 ms
     holding the energy in three bands.  Timbre, not just level — a seam where the strings
     drop out is audible even when the loudness matches exactly.
  2. SEARCH.  Every plausible (s, e) pair is scored by how closely the seconds FOLLOWING each
     one agree, band by band.  `e` is kept out of the fade-out (found from the loudness
     envelope, not assumed) and the loop is required to be long enough to be worth having.
  3. REFINE.  The winning `e` is nudged by up to ±150 ms at sample resolution so the two
     waveforms line up in phase rather than merely in spectrum, then snapped to a zero
     crossing so the splice cannot click.

The cut is rendered with an equal-power crossfade at the wrap: the first X seconds of the
output blend src[s…] in as src[e…] fades out.  Because the output's last sample is src[e⁻] and
its first sample is src[e], the wrap is continuous BY CONSTRUCTION — the crossfade is only
there to hide the fact that the music either side of it is not the same bar.

Nothing here is loudness-aware; normalisation is a separate pass (bake.sh), because a loop
point is a musical question and a target LUFS is not.
"""
import array
import json
import math
import os
import subprocess
import sys

RATE = 48000        # the rate everything is rendered at
FEAT = 8000         # the rate the search thinks at
HOP = 160           # 20 ms at FEAT


def decode(path, rate, channels):
    """The whole file as interleaved signed-16 samples. `array` and not a list: a nine-minute
    track is 27 million samples, which is 144 MB as an array and most of a gigabyte as a list
    of Python floats."""
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
    """Per-frame [low, mid, high] energy in dB, one frame every HOP samples.

    Two one-pole filters split the signal; corners are ~150 Hz and ~1.5 kHz at 8 kHz.
    """
    lo = mid = 0.0
    a1, a2 = 0.12, 0.7
    frames = []
    n = len(mono)
    for start in range(0, n - HOP, HOP):
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


def loudness_envelope(frames):
    """One number per frame: how loud the music is there, in dB."""
    return [10 * math.log10(sum(10 ** (b / 10) for b in f) + 1e-12) for f in frames]


def body_end(env, drop_db=4.0):
    """The frame where the fade-out begins — i.e. the last moment the track is still playing
    at its normal level.  Found from the envelope rather than assumed, because three of these
    tracks fade over 20 seconds and one stops dead."""
    live = sorted(v for v in env if v > -80)
    if not live:
        return len(env) - 1
    typical = live[int(len(live) * 0.6)]
    for i in range(len(env) - 1, -1, -1):
        if env[i] >= typical - drop_db:
            return i
    return len(env) - 1


def body_start(env, drop_db=6.0):
    """The frame the music proper starts at, past any quiet intro bar."""
    live = sorted(v for v in env if v > -80)
    if not live:
        return 0
    typical = live[int(len(live) * 0.6)]
    for i, v in enumerate(env):
        if v >= typical - drop_db:
            return i
    return 0


def search(frames, env, min_loop_s, window_s=4.0, step_s=0.25, s_frac=0.33, level_tol=3.0):
    """The best (s, e) in frames: the pair whose following `window_s` agree most closely.

    `s` is looked for in the opening third, `e` anywhere after it that is still inside the
    body of the music.  Level is compared alongside timbre so a quiet passage cannot match a
    loud one merely by having the same shape.
    """
    fps = FEAT / HOP
    w = int(window_s * fps)
    step = max(1, int(step_s * fps))
    min_loop = int(min_loop_s * fps)
    first, last = body_start(env), body_end(env)
    if last - first < min_loop + w:
        return None

    s_hi = min(first + int(len(frames) * s_frac), last - min_loop - w)
    best = None
    for s in range(first, max(first + 1, s_hi), step):
        ref = frames[s:s + w]
        ref_env = env[s]
        for e in range(s + min_loop, last - w, step):
            if abs(env[e] - ref_env) > level_tol:
                continue                      # a different dynamic; not the same moment
            total = 0.0
            for k in range(0, w, 2):          # every other frame is plenty at this stage
                a, b = ref[k], frames[e + k]
                total += (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2
            score = total / (w / 2)
            # Among near-equal matches prefer the longer loop: a 30-second loop that matches
            # perfectly is still worse to listen to than a 90-second one that matches well.
            score -= 0.02 * (e - s) / fps
            if best is None or score < best[0]:
                best = (score, s, e)
    return best


def refine(mono8, s8, e8, span_s=0.15, window_s=0.05):
    """Nudge `e` so the waveforms line up in phase, not merely in spectrum."""
    span = int(span_s * FEAT)
    w = int(window_s * FEAT)
    ref = mono8[s8:s8 + w]
    best, best_e = None, e8
    for e in range(max(0, e8 - span), min(len(mono8) - w, e8 + span)):
        total = 0
        for k in range(0, w, 2):
            d = ref[k] - mono8[e + k]
            total += d * d
        if best is None or total < best:
            best, best_e = total, e
    return best_e


def snap_zero(mono, at, span=240):
    """The nearest rising zero crossing, so the splice cannot click."""
    for d in range(span):
        for i in (at - d, at + d):
            if 0 < i < len(mono) - 1 and mono[i - 1] <= 0 < mono[i]:
                return i
    return at


def render(stereo, s, e, cross, out_path):
    """Write [s, e) with an equal-power crossfade at the wrap.

    out[t] = src[s+t]                                            for t >= cross
    out[t] = src[s+t]·sin(θ) + src[e+t]·cos(θ), θ = πt/2cross    for t <  cross

    The output's last frame is src[e−1] and its first frame begins as src[e], so the wrap is
    continuous by construction; the crossfade only hides that the music either side of the
    join is not the same bar.

    ⚠ THE CROSSFADE READS PAST `e`, and that is the whole reason it works. Where there is no
    music past `e` — a track being trimmed at its own ending — there is nothing to blend with
    and `cross` MUST be zero: blending the opening against the silence after the last sample
    fades the track in from half volume every time it wraps, which measures (and sounds) far
    worse than the join it was meant to smooth.
    """
    n = e - s
    if cross and e + cross > len(stereo) // 2:
        raise ValueError(f"crossfade of {cross} samples runs past the end of the source")
    out = array.array("h", bytes(4 * n))
    # the plain body first, at C speed
    out[2 * cross:] = stereo[2 * (s + cross):2 * e]
    for t in range(cross):
        theta = math.pi * t / (2 * cross)
        fin, fout = math.sin(theta), math.cos(theta)
        for c in (0, 1):
            a = stereo[2 * (s + t) + c]
            b = stereo[2 * (e + t) + c] if 2 * (e + t) + c < len(stereo) else 0
            v = int(a * fin + b * fout)
            out[2 * t + c] = max(-32768, min(32767, v))
    subprocess.run(
        ["ffmpeg", "-v", "error", "-y", "-f", "s16le", "-ar", str(RATE), "-ac", "2",
         "-i", "-", "-c:a", "pcm_s16le", out_path],
        input=out.tobytes(), check=True)
    return n / RATE


def trim_only(stereo, head_s, tail_s, out_path):
    """For a track that already loops: drop the silence at the ends and NOTHING ELSE.

    The join here is the author's own — they wrote the last bar to lead back into the first —
    so there is nothing to improve and a crossfade would only blur it. It also could not be
    applied: there is no music past the end of the file to blend the beginning against.
    """
    s = int(head_s * RATE)
    e = len(stereo) // 2 - int(tail_s * RATE)
    return render(stereo, s, e, 0, out_path)


def process(src, out_path, mode, min_loop_s=45.0, cross_s=0.5, head_s=0.0, tail_s=0.0,
            step_s=0.25, window_s=4.0, s_frac=0.33, level_tol=3.0):
    stereo = decode(src, RATE, 2)
    cross = int(cross_s * RATE)
    report = {"source": os.path.basename(src), "mode": mode,
              "source_seconds": round(len(stereo) / 2 / RATE, 2)}

    if mode == "trim":
        report["loop_seconds"] = round(trim_only(stereo, head_s, tail_s, out_path), 2)
        report["loop_start_s"] = round(head_s, 3)
        report["crossfade_s"] = 0.0
        return report

    mono8 = decode(src, FEAT, 1)
    frames = features(mono8)
    env = loudness_envelope(frames)
    found = search(frames, env, min_loop_s, window_s=window_s, step_s=step_s,
                   s_frac=s_frac, level_tol=level_tol)
    if found is None:
        raise SystemExit(f"{src}: no loop point survived the constraints")
    score, s_f, e_f = found

    s8, e8 = s_f * HOP, e_f * HOP
    e8 = refine(mono8, s8, e8)

    # Snapped at the RENDER rate, not the search rate: a zero crossing located in the 8 kHz
    # signal is only accurate to six samples at 48 kHz, which is exactly the size of step that
    # clicks.  The left channel stands for the pair — they cross together in this material.
    left = stereo[0::2]
    s = snap_zero(left, s8 * RATE // FEAT)
    e = snap_zero(left, e8 * RATE // FEAT)

    # The crossfade reads `cross` samples PAST the loop end, so the material has to be there.
    # It always is — the search keeps `e` inside the body of the music, ahead of the fade —
    # but a silent fade-to-nothing at the wrap is too quiet a failure to leave unguarded.
    if e + cross > len(stereo) // 2:
        raise SystemExit(f"{src}: loop end {e / RATE:.1f}s leaves no room to cross-fade")

    report.update({
        "match_score": round(score, 2),
        "loop_start_s": round(s / RATE, 3),
        "loop_end_s": round(e / RATE, 3),
        "loop_seconds": round(render(stereo, s, e, cross, out_path), 2),
        "crossfade_s": cross_s,
    })
    return report


if __name__ == "__main__":
    cfg = json.loads(sys.argv[1])
    print(json.dumps(process(**cfg)), flush=True)
