# Rebuilding the music

Everything here is what turned ten downloaded tracks into the ten `.ogg` files in
`assets/music/`. It is kept so the result can be re-derived rather than trusted — the licence
record and what was done to each file is `assets/music/SOURCES.md`.

Needs `ffmpeg`, `ffprobe` and Python 3 (standard library only — no numpy, deliberately: this
runs on a machine with nothing installed).

```bash
./fetch.sh                      # the ten sources from OpenGameArt, each checked against its Content-Length
python3 analyse.py src/*        # what each source IS: silence, loudness, does it fade out, does it loop
python3 sweep.py                # render every plausible cut of every track and score it     → winners.json
python3 deep.py                 # a finer search for the ones that came back near the line   → deep.json
python3 install.py out          # pick the better of the two per track, normalise, encode    → out/*.ogg
cp out/*.ogg ../../assets/music/
cd ../.. && godot --headless --import
sed -i 's/^loop=false$/loop=true/' assets/music/*.ogg.import
godot --headless --import
godot --headless -- --smoke-music
```

## The one measurement everything rests on

`verify.py` is the reason any of this can be claimed without listening to it. It plays a file
**twice** — concatenates the track with itself — and measures spectral flux across the whole
thing, so the wrap gets the same measurement as every other moment in the music. The answer is a
percentile: a join at the 30th percentile of a track's own transitions is an ordinary musical
moment, a join at the 100th is the single biggest change in the track.

That framing matters because a seam is only audible if it changes **more than the music around
it does**. An absolute threshold would pass a 4 dB step in a loud orchestral track and fail the
same step in quiet ambient music, and it is the quiet one a player would notice.

It also reports the raw sample step at the wrap, because a click is not a spectral event.

## Two things this turned up that are worth remembering

**"Loops seamlessly" on a download page is a claim, not a fact.** Three tracks advertise looping.
All three measured worse at their own ends than at a loop point found inside them — *Forest
Whisper Theme* by 10 dB of spectral jump and thirteen times the typical sample step, which is an
audible click. Every one of the ten ships as a cut, not a trim.

**The harder search is not always the better one.** `deep.py` steps three times finer and looks
in a wider place, and for four tracks it found much better loops (*Waystone Inn* went from the
88th percentile to the 22nd). For *Dark Descent* and *Forest Whisper Theme* it found worse ones,
because a different candidate set is weighed differently. `install.py` therefore compares both
passes and ships whichever measured better — it does not assume the expensive one won.
