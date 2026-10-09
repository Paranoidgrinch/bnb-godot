# The sound effects, and where they came from

All from OpenGameArt; the credits screen (scripts/CreditsPanel.cs) names each one.

| File(s) | Asset | Author | Licence | Source | Done to it |
|---|---|---|---|---|---|
| `hits/hit01–37.ogg` | 37 hits/punches | Independent.nu (Ljudbank) | CC0 | https://opengameart.org/content/37-hitspunches | FLAC → Ogg Vorbis (mono, q5); normalised |
| `metal/*.wav` | Metal Interactions | qubodup | CC0 | https://opengameart.org/content/metal-interactions | normalised |
| `debuff_energy_drain.ogg` | Energy Drain | qubodup | CC0 (page, 2024-06-23: "restrictions in the 7z file can be ignored") | https://opengameart.org/content/energy-drain | the pack's own .ogg; normalised |
| `buff_spell3.wav` | Spell 3 | Bart Kelsey | CC BY 3.0 (also BY-SA 3.0, GPL 2/3, OGA-BY 3.0) | https://opengameart.org/content/spell-3 | normalised |
| `death_falling_body.wav` | Falling body | remaxim, based on qubodup's recording | CC BY-SA 3.0 (also GPL 2/3) | https://opengameart.org/content/falling-body | normalised |

## Normalised

Every file sits at **−18 LUFS integrated (±0.7)**, so a blow, a block, a status and a fall are equally loud
and the Effects slider is the only thing that changes how loud they are. Two-pass `loudnorm linear=true`
(a flat gain, no compression), then a flat `volume` + `alimiter` (−1 dBFS) pass for the short, peaky
clips the first pass could not lift without clipping (the metal clicks were −25 … −31 LUFS).

## Where each plays

Where each plays (scripts/Sfx.cs): a random hit on every blow that lands or is blocked; a random metal
sound when block is gained; Energy Drain when a debuff is put on a body; Spell 3 for a buff; Falling
body when an enemy dies.
