# The music, and where it came from

Every track in Bureaucrats & Broomsticks is somebody else's work, used under a Creative Commons
licence. This file is the record of **which file, from where, in what state** — so that any
runtime `.ogg` in this folder can be rebuilt from scratch and checked against what was actually
downloaded.

The original downloads are **not** in this repository: they are 200 MB of WAV, and a checksum
plus a URL reproduces them exactly. Keep them wherever you keep third-party sources; the SHA-256
below is what proves the file you have is the file these were made from.

## What was done to them

All ten were **edited for use in BnB**, and the credits screen says so (CC BY asks that
modifications be indicated). Two things were done and nothing else:

1. **Cut to a loop.** Six of the ten fade out — they were written with an ending, and a game
   needs a loop. Each was cut to a section `[s, e)` whose two halves of the join sound alike,
   with an equal-power crossfade at the wrap. Four already ran at a steady level and were cut
   the same way, because trimming them at their own ends measured worse in every case.
2. **Normalised.** The sources arrive between −4.7 and −29.8 LUFS. Every runtime file is
   corrected to **−16 LUFS integrated, −1.5 dBTP** by a two-pass flat gain (`loudnorm
   linear=true`), so the game's Music Volume slider is the only thing that changes how loud the
   music is. A single-pass loudnorm would have compressed dynamically and pumped across the loop
   point; a flat gain cannot.

Nothing was re-pitched, re-mixed, shortened for length, or layered.

## Where the loop points came from

They were measured, not guessed. `tools/` in the build scratch holds the three scripts:
`analyse.py` (what each source is), `loopsmith.py` (find the loop point, render the cut),
`verify.py` (play the result twice and ask whether the join stands out). The last one is the
real test: it measures how much the sound changes at the wrap and compares that against every
other transition **in the same track**, because a seam is only audible if it changes more than
the music around it does. Every shipped file sits below the 90th percentile of its own music.

`godot --headless -- --smoke-music` re-checks the shipped result: ten files, all present, all
with the loop flag set, and the priority table from the design document.

## The sources

| Track | Composer / required credit | Licence | SHA-256 of the source file | Source |
|---|---|---|---|---|
| Secret Sanctum | composed, performed, mixed and mastered by Viktor Kraus | CC BY 3.0 | `1861188f72ad71c2155723d4c351a82a7cc7da7375552e91091d900ab50eed33` (`Viktor Kraus - Secret Sanctum.wav`) | https://opengameart.org/content/secret-sanctum |
| Waystone Inn | Enkrez | CC BY 4.0 | `006528b53ef8bd1a41302c8f48d4869f6d163beb1c568966e51322a057435352` (`waystone_inn_0.wav`) | https://opengameart.org/content/waystone-inn |
| Fantasy Music - The Savvy Merchant | HitCtrl | CC BY 3.0 | `90e0e472dcee3c86b27d9c2b5756911dfe84f378f2309277d323235007e4f707` (`The Savvy Merchant.ogg`) | https://opengameart.org/content/fantasy-music-the-savvy-merchant |
| Victoriana Loop | Joe Baxter-Webb (BossLevelVGM) | CC BY 3.0 | `058dbff5fa03de97598231827525827380be04112d004673421876fa0a81156e` (`Victoriana Loop_2.mp3`) | https://opengameart.org/content/victoriana-loop |
| Dark chamber | Marcelo Fernandez — http://www.marcelofernandezmusic.com | CC BY 4.0 | `2b5d4fb921b8c84c4e7f9efb07323b67ce02df445d81ab22c148fd0502667164` (`Dark chamber.mp3`) | https://opengameart.org/content/dark-chamber |
| Forest Whisper Theme | Cleyton Kauffman — https://soundcloud.com/cleytonkauffman | CC0 | `cd834fafd028bb882500c49e7e766d50f9de337d644951bfdc4e68b09b91fadf` (`Forest Whisper.wav`, from `forest_whisper_theme.zip`) | https://opengameart.org/content/forest-whisper-theme |
| Fantasy Music - The Eternal Sands | HitCtrl | CC BY 3.0 | `3e2f22bf1a39dbffcf8ca6a55be9ed1c32d8c6417b36045fe11763fe447581bd` (`the_eternal_sands.wav`) | https://opengameart.org/content/fantasy-music-the-eternal-sands |
| Dark Descent | Matthew Pablo — http://www.matthewpablo.com | CC BY 3.0 | `a835debcb0d86077000ecf33440b9d04b0d715c1bd78e222010dc6838538803e` (`Dark Descent_0.mp3`) | https://opengameart.org/content/dark-descent |
| The Desecrated Temple | Insydnis | CC BY 3.0 | `52a0ed919f65bc9ced41578c69ad350c41514d1c91467563c9c37c6733d4ad11` (`The Desecrated Temple_2.mp3`) | https://opengameart.org/content/the-descecrated-temple |
| Land of the Great Gods | Alexandr Zhelanov | CC BY 4.0 | `4b9c543eb5f09aa484a11b49432685d5cb65aeef6d25237e9e532f846ea76837` (`land_of_the_great_gods.ogg`) | https://opengameart.org/content/land-of-the-great-gods |

## Three things the source pages say that the integration document could not

- **Waystone Inn** is filed on OpenGameArt under the author name *Esiltir*, but the attribution
  notice on the same page asks to be credited as **Enkrez**. Enkrez is what the credits show.
- **Dark chamber** carries two different licences on one page: the site metadata says CC BY 3.0,
  the composer's own notice on the same page says CC BY 4.0. **4.0** is used — it is what the
  author wrote in his own words, and his full notice (title, name, site) is reproduced.
- **Forest Whisper Theme** is CC0 and requires no attribution whatsoever. The author asked to be
  credited anyway, so he is.

Two further notes: *Victoriana Loop* lists its MP3 twice on OpenGameArt and the two files are
byte-identical — there is no separate loop version to prefer. The URL for *The Desecrated Temple*
really is spelled `the-descecrated-temple`; that typo is in the author's own slug, and it is the
address that resolves.
