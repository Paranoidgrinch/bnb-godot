# bnb-godot

The player-facing frontend for **Bureaucrats & Broomsticks — Act I**, built with Godot 4.7 (.NET).

The game rules run entirely in the [RogueDeck engine](../RogueDeck-Core) (a sibling checkout, referenced
via `ProjectReference`); this project only renders state and forwards input, per the engine's
`docs/godot-export-contract.md`. The whole game is the content document `content/game.roguedeck.json`,
exported by [bnb-content](../bnb-content) — the frontend is **generic**, so any `game.roguedeck.json`
gets a title screen, character select, map, combat, events, shop and rewards from its blueprint +
presentation manifest.

## Layout
- `scripts/GameHost.cs` — autoload that loads the blueprint once and owns the engine's `RunPlayback`
  (the reference host object); turns its synchronous `onChanged` into a deferred `StateChanged` signal.
- `scripts/SessionScreen.cs` — the run screen: one dispatcher over the session state (event/shop/
  workbench choices, entity picks, path forks, interlude+save, combat, completion) plus inventory + log.
- `scripts/MapView.cs` — the ACT's map as a navigable graph: entry at the top, boss at the bottom, each room
  drawn as the role it was generated for. It draws `RunState.Map` (the map generated for the act being
  walked) — the blueprint's own `Map` is empty in a generated game.
  A room says who is IN it one hover away: the boss's name and the elite's, read off the encounter's own
  presentation entry. Names are only WRITTEN ACROSS a room in a gauntlet act (several boss rooms, where the
  order is the act's whole shape) — an elite never widens its room, because there are several per act.
- `scripts/Boot.cs` — the title screen (game identity + unlock-gated character select + New/Continue/Archive).
- `scripts/Archive.cs` + `scripts/ArchivePanel.cs` — **the archive**: the first layer of meta progression, and
  the only part of the game that survives a run. Six shelves — enemies, elites, cards, relics, bosses, gods —
  each entry a thing the player has MET, with what it is: a body's HP (a range when it varies by encounter),
  the act it stands in, what it opens with, and every move it has in the enemy's own telegraph words; a card's
  face, cost and rules, and what the same card says once improved; a relic on its pool frame; a god's Divine
  Rule. Two halves, deliberately apart:
  - **The catalogue** is built from the document and is the same for everybody. Nobody maintains a list: the
    bodies come out of the ACTS' OWN ENCOUNTER POOLS, so a slot exists exactly when a run can draw it (61 of
    the document's 294 encounters are in no pool — old drafts, and a slot no player can fill is a lie about
    how much there is to find). An improved card is filed under the card it improves: 134 of the 363 card
    definitions are the `+` form of one already there, so the shelf holds 229.
  - **The fund book** is the player's, in `user://archive.json`. ⚠⚠ NOT in `metastate.json`, and it must not
    be: `RunPlayback` loads the meta profile when a run STARTS and writes that whole loaded object back when
    the run ENDS, so anything written into that file during a run — which is when every discovery happens —
    would be overwritten by a snapshot taken before it.
  Recording happens in `SessionScreen.Rebuild`, for the same reason the music is asked there: a transition is
  something somebody has to remember to report and a redraw is not. "Met" means SEEN, not owned — the rare
  relic you could not afford is exactly the one worth looking up later. ⚠ A probe or a simulated run records
  in memory but never writes the file (`Archive.Robot`), or `tools/simulate.sh` would hand the player a
  finished archive they never earned. The gods' shelf reads `???` and does not open until the first one is
  met. Reachable from the title screen and from **Esc inside a run** (SettingsPanel's `onArchive`, supplied by
  the run screen the way "Save and quit" is — the title screen has its own button and passes nothing): the
  question "how much HP did that thing have" is asked during a fight far more often than on a menu.
  "Reset progress" at the foot of the screen throws away the fund book AND `metastate.json`, and asks twice
  before it does — ⚠ but it is NOT offered while a run is live, because `RunPlayback` rewrites the meta
  profile at the finish line from the copy it loaded at the start, so a reset there would be undone at the end
  of the run after the player had been told it happened.
- `scripts/Glossary.cs` — what every named thing MEANS, built once from the document: ask it about an id, or
  hand it any text and it names the terms that text uses. Every hover in the game goes through it.
- `scripts/DisplaySettings.cs` — **the window**: the project declares one design canvas (1280 × 720) and a
  stretch mode (`canvas_items` / `expand`), so every coordinate in the frontend is a design unit the engine
  scales to whatever the window is — a card is 134 × 190 at every resolution and nothing here multiplies by a
  scale factor. This file is the player's half of that: window mode (windowed / borderless / exclusive), size,
  V-Sync and an interface scale on top of it all (`ContentScaleFactor` — a different thing from the window
  size), stored in `user://settings.cfg` and applied before the first screen is built. ⚠ The window half is
  skipped under `--smoke*` and `--sim`: every screenshot probe here compares against numbers taken from a
  1280 × 720 window.
- `scripts/SettingsPanel.cs` — that dialog, on the title screen and on **Esc** inside a run.
- `scripts/MusicDirector.cs` — **the music**: an autoload (it has to outlive the scene change from the
  title screen into a run) holding two `AudioStreamPlayer`s that crossfade between ten tracks. A screen never
  names a file — it says WHERE THE PLAYER IS (`MusicDirector.Want`), and `MusicDirector.Cue` reduces the run
  state to exactly one track in the priority order Act V ▸ boss ▸ elite ▸ campfire ▸ shop ▸ act ▸ title. Asked
  from `SessionScreen.Render`, so every route into a shop, a fight or the next act reports itself without
  anyone remembering to. ⚠ `Want` is idempotent — a redraw must not restart the music, and a fight redraws
  several times a second. An act theme resumes AT THE BAR IT WAS ON after a shop or an elite; everything else
  starts from the top. ⚠ Act V is the exception the priority table exists to state: the gauntlet has one theme
  for its map, its elites and its final boss alike.
- `scripts/AudioSettings.cs` — **how loud**, and the one place it is decided: a single global Music Volume in
  `user://settings.cfg`, on an audio bus the two players route through. The title screen's slider and the Esc
  menu's slider are not two settings kept in step — `SettingsPanel` is one object shown in both places, so
  they are the same control. ⚠ Both this and `DisplaySettings` **Load before they Save**: a fresh `ConfigFile`
  writes only its own section and would take the other one with it.
- `scripts/CreditsPanel.cs` — the **Credits** button on the title screen. Not a nicety: nine of the ten tracks
  are CC BY and attribution is the condition they are used on. Each entry carries what that author's own page
  asks for (three of them ask for more than a name), a clickable licence, a clickable **Source**, and the note
  that the file was edited to loop. `assets/music/SOURCES.md` is the full record — URL, SHA-256 of the exact
  download, and what was done to it.
- `scripts/MoonvineTheme.cs` — **the whole look**: one palette (a near-black page bled red, antique gold for
  everything you can touch, amber for everything that wants your attention) and one font hook. It is the only
  place in the frontend allowed to name a colour, and `theme/README.md` says how to swap the typeface in one
  line.
- `scripts/CardVisuals.cs` — **the card**. One widget printed on the master frame `assets/cards/card-frame.png`,
  whose holes ARE the fields: every one is a measured fraction of the card, so the same layout serves a hand
  card and a thumbnail. ⚠ Nothing on a card may report a minimum size — the root is a plain `Control` and each
  field sits in a fixed clipped window, because Godot clamps a Control's size *up* to its children's combined
  minimum and that is what used to change a card's shape when it was clicked (`--smoke-format` measures it).
  The hand CLOSES UP as it grows — an overlap is what makes a row of cards read as a hand — so the card under
  the pointer is pulled out of the fan: scaled 1.5×, straightened, and put in front with `ZIndex`, free to
  cover the arena above it. ⚠ The pivot is left alone (it is the centre, which is where `AnimateDraws` flips a
  freshly-dealt card), so the card is also moved UP by half its growth — that keeps its bottom edge where the
  fan put it and sends all the extra size upward. ⚠⚠ And the hover is listened for on the card's CLICK
  OVERLAY, not on the face: the overlay covers the whole card and is what the pointer actually lands on, so
  wiring the face is a gesture that silently does nothing (`--smoke-hover`).
  A card's picture is `assets/art/cards/<id>.png` — the path the document itself declares in
  `Presentation.Art`, so the contract's path IS the path on disk and there is nothing to register.
  An upgraded card has no picture of its own (`levy_stamp+` draws `levy_stamp.png`), so 413 cards ask
  for 229 pictures; relics ask for 210 more under `assets/art/relics/`, every **body** — all 269
  enemies, elites and bosses — asks for one under `assets/art/enemies/`, drawn in its column in place of the
  stick figure, and the player's own body asks for one under `assets/art/characters/`, shown on the title
  screen beside the name. ⚠ A body is never mirrored by the game (a flipped body wears its sash on the wrong side), so
  it is drawn facing LEFT, towards the player. Which KIND of body it is travels in the document as
  `Presentation.Enemies[id].Frame` (35 boss · 66 elite · 4 mimic · 164 standard) — worked out from the fights
  an id is met in, because that is not a property of the enemy. The whole list, with the design
  canon's brief beside every relic, is generated: `bnb-content/ART_SLOTS.md`. ⚠ A dropped file is
  invisible until Godot has imported it — `tools/import-art.sh` once, and `--smoke-art` says how many
  slots are filled. ⚠ New textures import WITHOUT mipmaps by default and every picture here is drawn much
  smaller than it is painted, so `project.godot` sets `[importer_defaults] texture={"mipmaps/generate": true}`.
  The back is `assets/cards/card-back.ogv` (moving, deck top) and `card-back.png` (still, everywhere
  else) — a whole card already, border and rounded corners painted in, so it is drawn at full bleed with
  no chrome. ⚠ Both are cut to the size they are DRAWN at, because a video texture cannot be mipmapped;
  re-cut them from the master if the card ever resizes (the two ffmpeg lines are in
  `VISUAL_OVERHAUL_PLAN.md` under D2).
  The same file also draws **the relic tile**: a 50 px framed square wearing its POOL's canon frame
  (§10.4 — normal slate, shop copper, event pale violet, boss a doubled antique-gold line on near-black
  purple, elite and mimic that same frame run plainer), the relic's picture inside it if the file exists
  and its slot code if it does not. The pool travels in the document as `Presentation.Relics[id].Frame`,
  so the frontend never has to know which relic came from where.
- `scripts/SessionScreen.cs` — every room. The combat pane is **six fixed regions** (heading · divine rule ·
  arena · hand · controls · the draw pile's corner), declared as constants in one block and anchored: a
  container hands out its children's minimum heights first and the leftovers afterwards, which is how an
  Act V boss with a rule panel and a dozen statuses used to cut the hero's own health bar in half. What
  overflows a region scrolls inside it. ⚠ The draw pile is built ONCE per fight and updated in place — its
  back is a video, and a video that is re-created starts at 0.00 (`--smoke-deck` measures exactly that).
  And **the shelf** on the right edge: what you are wearing is
  drawn as objects in an `HFlowContainer` that wraps into rows, with the name and the rules text one hover
  away. ⚠ The sidebar's horizontal scrolling is switched OFF on purpose — a `ScrollContainer` that may
  scroll sideways hands its child the child's *minimum* width, and a wrapping container's minimum is one
  tile, so the shelf would come out as a single column. `--smoke-shelf` proves it at 69 relics.
- `scripts/GodotMetaStore.cs` — the cross-run profile in `user://` (permanent unlocks / discoveries).
- `content/game.roguedeck.json` — the shipped game (refresh with `tools/sync-content.sh`).

## Running
```
dotnet build
godot --path .                 # or open in the Godot 4.7 (.NET) editor and press Play
```
Headless checks (no window):
```
godot --headless -- --smoke        # boot: prints "loaded: …" and quits
godot --headless -- --smoke-art     # the art census: how many of the 709 slots have a file
godot --headless -- --smoke-generators # both map generators, same seed, side by side: rows and rooms per act
godot --headless -- --smoke-materials # the material census: the six surfaces the theme asks for by name,
                                     # the ONE room each act's own fights declare, and the rule that keeps
                                     # a background safe — the veil is over the picture, never under it
godot --headless -- --smoke-music   # the ten tracks: present, LOOPING, and the right one for each state
                                     # — what "New run ▸" is actually asking the player to choose between
godot --headless -- --smoke-full    # auto-plays the first rooms and reports the state
godot --headless -- --smoke-timing  # per-action latency at the FIRST fight (~150 ms/action, measured
                                     # 2026-09-17). An action re-executes the whole run under the replay
                                     # model, so this is a floor that GROWS with the run — the "~17 ms"
                                     # that stood here was measured in July, when the game was act I
godot --headless -- --smoke-statuses # carried state reads as its authored name, not its id
godot --headless -- --smoke-archive # the ARCHIVE, both halves: what the document offers to be found (per
                                     # shelf, and how many of them are painted) and whether a discovery
                                     # survives being written down — asked THROUGH the file, not in memory.
                                     # ⚠ the one probe that rewrites user://archive.json; windowed, it also
                                     # photographs the shelf and one plate of each kind
godot --headless -- --smoke-archive-run # what a RUN teaches the archive: walks a seed to an elite and names
                                     # every enemy, card and relic the recorder picked up on the way. Its
                                     # stop condition IS the archive's state, so it can only end if the
                                     # recording happened during the walk — and it checks that a probe left
                                     # the player's own fund book alone — and then walks in from the ESC MENU
                                     # the way a player does (press Esc, find the button by its words, press
                                     # it) and reports what came up
godot --headless -- --smoke-marathon # play the WHOLE game (all five acts) and report rooms + latency
godot --headless -- --smoke-screens  # EVERY KIND OF SCREEN, act by act: two walks (an unlosable body to the
                                     # last god, a mortal one to the defeat screen), and at each distinct
                                     # state x act the screen is BUILT and the result written into a table.
                                     # Four words, and only one is good news: ok · FAILED · unreached (the
                                     # walk never got there, so nothing is claimed) · no room / n/a (this act
                                     # has no such room). Non-zero exit on any FAILED.
                                     #   --seed N / --mortal-seed N pick the two walks
godot --headless -- --smoke-tooltips # audit a combat screen: is anything NAMED but not explained?
godot --headless -- --smoke-format   # every card in the hand is exactly the size it was handed,
                                     # ten clicks apart (windowed: it also takes the shot)
godot --headless -- --smoke-deck     # (windowed) the draw pile's clip keeps playing across three card plays
godot --headless -- --smoke-window   # (windowed) the same fight at three window sizes, measured in canvas units
godot --headless -- --smoke-settings # (windowed) a picture of the settings dialog
godot --headless -- --smoke-newrun   # (windowed) a picture of the New run dialog and its two answers
godot --headless -- --smoke-quit     # Esc mid-fight, "Save and quit to title", and then the title screen
                                     # itself checks the run came back: on disk, offered, same room
godot --headless -- --smoke-shelf    # the relic strip at a HOSTILE count: wears 69 relics (one from every
                                     # pool before a second from any), switches every seventh off, and
                                     # reports rows, tiles per row, whether anything sits outside the
                                     # panel and whether the sidebar scrolls. `--shelf N` for another count
godot --headless -- --smoke-boss 5 --boss inanna --rounds 12   # a NAMED boss, N rounds in
godot --headless -- --smoke-boss 5 --boss nanna_sin --rounds 6 --plays 3  # …playing 3 cards a round,
                                                              # for a fight whose state is what the player did
godot --headless -- --smoke-boss 5 --boss nanna_sin --seed 1 --rounds 6 --plays 3  # …on a KNOWN seed: one
                                                              # walk instead of one per seed searched
                                                              # (nanna_sin is on 1, inanna on 5)
```
The boss probe reads three things back out of the LIVE tree rather than out of the document, because a
headless run cannot take a screenshot and "the method returned" is not "it is on screen": the **Divine Rule
Area**, the **card stamps** in hand, and the **forecast** — how many of the enemy's coming actions are drawn
against how many the engine is willing to project for a hero who has been granted the sight.

Every screenshot check below also prints its own tooltip audit, so "a name with no explanation" cannot
quietly reappear on any screen.
Windowed screenshot checks (each walks to the room it names, then captures it to `user://`):
```
godot -- --smoke-map      # the act map at the entry fork — and hovers the boss room AND the elite room,
                          # reporting the name each one answers with
godot -- --smoke-shop     # the shelf, with prices, including what is unaffordable
godot -- --smoke-event    # a door
godot -- --smoke-ambush   # a multi-enemy fight
godot -- --smoke-elite    # an elite
godot -- --smoke-crowd    # the widest fight it can reach: does the enemy row still fit on the screen?
godot -- --smoke-boss 2   # walk to that act's BOSS and capture it (the phase banner, the dial, the chips)
godot -- --smoke-reward   # the card reward
godot -- --smoke-hover    # the card under the POINTER: pulled out of the fan, square and half again as
                          # large, in front of everything — measured through the real pointer, because the
                          # face's own MouseEntered never fires (the click overlay is what the pointer hits)
godot -- --smoke-bug      # the bug-report window on the title screen: fills it in, SENDS it, and says what
                          # landed in the folder — four files or it is not a report
godot -- --smoke-bug-run  # the same as a player makes one: mid-fight, Esc, the button in the menu
```

## Bug reports
The one diagnostic the player writes. Everything else in this frontend is a measurement; none of it can notice
that an intent read as nonsense or that a card did the opposite of its words. `scripts/BugReport.cs` +
`scripts/BugReportPanel.cs`: a button on the title screen and in the Esc menu, one text box, and with the
message go **the save** (so the bug can be resumed rather than guessed at), **a screenshot**, **the diagnostics**
(seed, room, version, content document) and **the tail of `user://logs/godot.log`**.

Two rules it is built around:
- **The picture is taken before the window opens.** A player presses Esc *because* something on screen is
  wrong; one step later that something is behind a dimmed sheet. `BugReport.Remember` is called one line before
  any overlay exists, and the dialog shows the thumbnail so the player can see which moment they caught.
- **The local copy is written before the network is touched.** A report that fails to upload is still a report:
  it lands in `user://bug-reports/<timestamp>/` (the last ten are kept) and the window says where.

**The webhook is never committed.** It is a write credential for a channel and this repository is public, so
`bugreport.cfg` is gitignored and looked for in four places, in this order: the `BNB_BUGREPORT_WEBHOOK`
environment variable · `user://bugreport.cfg` · `res://bugreport.cfg` (a dev checkout) · beside the executable
(where `tools/export.sh` copies it, so changing the channel does not mean exporting the game again). With no
file anywhere the feature still works and stops at the local copy.

To take reports, put a Discord webhook URL (Channel → Edit → Integrations → Webhooks → New Webhook → Copy
Webhook URL) on one line in `bugreport.cfg` at the project root. To prove the upload without a channel:
```
tools/bug-sink.py 8787 &                                    # takes the multipart body apart BY HAND
BNB_BUGREPORT_WEBHOOK=http://127.0.0.1:8787/hook godot -- --smoke-bug
```

## Simulating runs (bug hunting)
A player made of dice: `scripts/RunSimulator.cs` walks the REAL screens — every answer goes through the same
session/driver the mouse drives — but answers at random (random fork, random door, random card at a random
enemy, random pick from every offer, and now and then an early end of turn or a skipped reward). It plays
badly on purpose; what it is for is BREADTH, so a batch touches content no careful player would reach.
```
godot --headless -- --sim [--sim-seed N] [--sim-health N | --sim-immortal] [--sim-steps N]
tools/simulate.sh                  # 20 runs, 400 hp, 4 processes at a time
tools/simulate.sh 100 --jobs 8     # a hundred of them
tools/simulate.sh 50 --immortal    # nothing can kill them: the deepest reach into the later acts
tools/simulate.sh 50 --real        # the game's own health (most runs die in act I)
tools/simulate.sh 100 --ui 0       # draw no screen at all (a drawn run costs about SIX times an undrawn
                                   #   one, and the walk is identical — the screen reads the session, it
                                   #   never answers for it). The default is --ui 5: the first five seeds
                                   #   draw, so the frontend is still walked every day, and a redraw that
                                   #   throws now fails that run.
tools/simulate.sh 100 --godot      # every run through Godot, one process each, the way it was before R4.
                                   #   By default only the --ui runs go that way and the rest are played by
                                   #   `roguedeck-bot` (RogueDeck-Core/src/RogueDeck.Bot.Cli) — the SAME
                                   #   answer loop, in one process on M threads. Two hosts, one brain: a
                                   #   seed walked by either produces the same log, line for line.
tools/simulate.sh 100 --release    # play out of an EXPORTED binary. ⚠ It is the only build of this game
                                   #   whose ENGINE assemblies are optimized: a dev build leaves them in
                                   #   Debug, and so did every export before RogueDeck-Core gained its
                                   #   Directory.Build.props. 40.4 s of loop against 30.4 s, same result.
```
Logs land in `~/Desktop/bnb-run-logs/<timestamp>/run-<seed>.log`, one process per run so a crash costs that
run and not the batch — the log IS the reproduction (seed + character at the top, then every room, choice
and play in order, with the engine's own narration folded in; `--sim --sim-seed N` replays it). `summary.txt`
lists each run's verdict, the outcomes, **which runs are worth reading**, and what the batch touched.

A run is flagged when something could not be answered for: an engine error, a step that threw, a turn that
never ended, a wall, an exception. NOT flagged (they are the engine working): a card the rules refuse — a
random player will try a curse — and a card that parks to ask its own question.

## Training a runner for balance
The same simulator can be given a POLICY — 17 weights that decide which card is worth playing, when a turn
is over, which enemy to hit, which room to walk into and what to buy (`SimPolicy` in `RunSimulator.cs`).
`tools/train.py` breeds them against the balance question itself: starting at 9999 hp, **how much damage does
the game take off a runner on the way to a named act's boss?** Which act is `--target-act`, and it defaults
to the last one the game has (`LAST_ACT` in `train.py`, Act V since V-0 — and since V-6 that act is the six
authored gods and no longer placeholders, so the default IS the real question); the simulator's own fitness line names
no act, only the per-act table (`actBossDamage`). Least taken wins; never arriving is worse than any arrival. Damage ADDED UP, not health remaining: no act heals you at its end, but the content heals
plenty (one act-II door heals to full), and remaining health would credit a runner for the door it happened
to walk through — on seed 1000 that reads 540 lost where 1075 was actually taken.
```
tools/train.py                                    # 5 generations × 8 runners × 2 seeds
tools/train.py --generations 10 --population 12 --seeds 3 --jobs 8
tools/train.py --target-act 3                     # measure to an earlier act's boss instead
tools/train.py --resume ~/Desktop/bnb-balance-training/<stamp>
tools/train.py --health 220 --generations 2 --population 3 --seeds 1   # a fast shakedown, not a training
godot --headless -- --sim --sim-immortal --sim-policy <policy.json>    # watch one runner play
```
Output in `~/Desktop/bnb-balance-training/<timestamp>/`: `best-policy.json`, `leaderboard.csv`, and one
folder per generation holding each runner's policy, ranking and full run log. Every ROOM line carries
`cost=` — the health that room took — so the logs answer the other half of the balance question: which
encounter is expensive. Reckon 4–10 minutes per immortal run.

## Building desktop binaries
Requires the **Godot 4.7 (.NET) export templates** — install them once via the editor
(*Editor → Manage Export Templates → Download and Install*), then:
```
tools/export.sh        # → build/linux/… and build/windows/…
tools/import-art.sh    # after dropping PNGs into assets/art/ — Godot only reads what it imported
```
C# has no Godot web export, so the targets are desktop (Linux / Windows).

## Releasing to testers
```
tools/release.sh              # export, then package into dist/<version>/
tools/release.sh --no-export  # package what is already in build/
tools/release.sh --publish    # … and push every package to itch.io (moonvine-forge/bnb-alpha, password-protected)
```
Output: a Windows installer (NSIS: per user, no admin prompt, Start menu entry, uninstaller), a Windows
portable zip, a Linux AppImage, a Linux tarball, `README-ALPHA.txt` for the testers (English + German, from
`packaging/README-ALPHA.txt`) and `SHA256SUMS.txt`. The version is `application/config/version` in
`project.godot`. It is shown on the title screen and named in every bug report and run recording. Raise it
together with the two `*_version` lines in `export_presets.cfg`.

The script refuses to run without `bugreport.cfg` and `runlog.cfg`. The packages carry both webhooks, so
`dist/` is gitignored and a release is never attached to this public repository. `makensis` and
`appimagetool` do not need root: see the header of `tools/release.sh`. The icon comes from
`tools/make-icon.py`. Save a better `assets/brand/icon.png` and `icon.ico` over it to replace it.

## Presentation
Art/flavor/rarity come from the blueprint's presentation manifest (never from the engine): card rarity
tints the hand, card/relic flavor shows as tooltips, character flavor shows on the title. An `Art` path
resolves under `res://assets/art/`, which is why `cards/levy_stamp.png` in the document is
`assets/art/cards/levy_stamp.png` on disk. A slot with no file is a normal state, not an error: the card
draws an empty socket with its own code printed in it, and the relic draws its name.
