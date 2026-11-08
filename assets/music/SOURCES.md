# The music, and where it came from

Every track in Bureaucrats & Broomsticks is somebody else's work, used under a Creative Commons
licence. This file is the record of **which file, from where, in what state**.

## The nine tracks of 2026-11-08

Title, Acts I–IV, elite, boss, shop and campfire were replaced on 2026-11-08 by tracks the user
downloaded from free-stock-music.com. The licence notice that came with each file is kept verbatim
in `licenses/`. The MP3 sources are not in this repository; the SHA-256 below identifies them.

**What was done to them** (`tools/music/fade.sh`), and nothing else:
1. Leading and trailing silence trimmed (below −60 dB).
2. Faded in over 4 s and out over the last 8 s, so each track loops whole: quiet into quiet.
   No loop cut.
3. Normalised to −16 LUFS integrated, −1.5 dBTP by a two-pass flat gain (`loudnorm linear=true`),
   encoded to Vorbis 160 kb/s at 48 kHz.

| Cue | Track | Composer / required credit | Licence | SHA-256 of the source MP3 |
|---|---|---|---|---|
| Title | Unraveling | Scott Buckley, soundcloud.com/scottbuckley | CC BY 4.0 | `82ed105724e6ee31d2007615472cb4e3621000edbff9a4aaf6a64789c32bece5` |
| Act I | Lurking Sloth | Alexander Nakarada (CreatorChords), creatorchords.com | CC BY 4.0 | `bff6f93aefa42b04316ab6324180b64a438def4ce16cd26270f64208ffc44087` |
| Act II | The Long Dark | Scott Buckley, soundcloud.com/scottbuckley | CC BY 4.0 | `fa5bfe904dd4902b9eb66d8df518a91adde41fb6d1687aefb5af1a2de7da28da` |
| Act III | Fairy Of The Forest | Alexander Nakarada (CreatorChords), creatorchords.com | CC BY 4.0 | `ee08f8faa364619487cb3156fe2844f929b4efa6e2671da18766990996128621` |
| Act IV | The Legend of Narmer | WombatNoisesAudio, soundcloud.com/user-734462061 | CC BY 3.0 | `32230fceb774d6bd9d7bf7d3ea4f9410d88623d618f00101fc31a956a0bfe7c1` |
| Elite | Hearts Of Iron | Alex-Productions, onsound.eu | CC BY 3.0 | `73865f1af6db413dfe35dc941a18a5b23ae4f0f73fd8b2b95a23f21d37394a31` |
| Boss | Endless Storm | Makai Symphony, makai-symphony.com | CC BY-SA 3.0 | `d002fe34482c0063f82ccfe52eba40e5819f460721ff6405f61a5eaf5d27190e` |
| Shop | Adventure | Alexander Nakarada (CreatorChords), creatorchords.com | CC BY 4.0 | `7f0822aa0f6007cf49c26e871aa0619b872233d03a877c448729354eb4e2bf71` |
| Campfire | The Things That Keep Us Here | Scott Buckley, soundcloud.com/scottbuckley | CC BY 4.0 | `a8ac035dc83a29dce3924b68debea4c6bd9777ef2de18a303c52fd0267957db9` |

**Endless Storm is CC BY-SA 3.0:** the edited file (`music_boss_endless_storm.ogg`) is shared under
the same licence.

## The Act V theme, and the tracks it shipped with until 2026-11-08

Everything below is the record of the first ten tracks. Only *Land of the Great Gods* (Act V) is still
in the game; the other nine rows are kept as history.

### What was done to them

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

### Where the loop points came from

They were measured, not guessed. `tools/` in the build scratch holds the three scripts:
`analyse.py` (what each source is), `loopsmith.py` (find the loop point, render the cut),
`verify.py` (play the result twice and ask whether the join stands out). The last one is the
real test: it measures how much the sound changes at the wrap and compares that against every
other transition **in the same track**, because a seam is only audible if it changes more than
the music around it does. Every shipped file sits below the 90th percentile of its own music.

`godot --headless -- --smoke-music` re-checks the shipped result: ten files, all present, all
with the loop flag set, and the priority table from the design document.

### The sources

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

### Three things the source pages say that the integration document could not

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
