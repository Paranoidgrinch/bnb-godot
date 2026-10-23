# bnb-godot

The Godot 4.7 (.NET) frontend of Bureaucrats & Broomsticks, a satirical fantasy roguelike deckbuilder.

| Repository | Content |
| --- | --- |
| [RogueDeck-Core](https://github.com/Paranoidgrinch/RogueDeck-Core) | combat and run engine, bot, replay |
| [bnb-content](https://github.com/Paranoidgrinch/bnb-content) | game content, exported to `game.roguedeck.json` |
| bnb-godot (this repository) | frontend: screens, cards, art, music, settings, bug reports, run recording |
| [bnb-runs](https://github.com/Paranoidgrinch/bnb-runs) | recorded playtest runs |

The game has five acts on generated branching maps, four playable characters (Lieutenant, Bureaucrat, Hedge
Witch and Sorcerer's Apprentice), combat with telegraphed enemy intents, elites and gods as bosses, events, shops,
relics, an archive of everything encountered, a tutorial, ten music tracks and save/continue. It is in closed alpha (version 0.3.0,
distributed to testers through itch.io).

All game rules run in the RogueDeck engine, which is referenced as a sibling checkout via `ProjectReference`.
This project only renders state and forwards input, following the engine's `docs/godot-export-contract.md`.
The whole game is the content document `content/game.roguedeck.json` exported by bnb-content. The frontend
itself is generic: any `game.roguedeck.json` gets a title screen, character selection, map, combat, events,
shop and rewards from its blueprint and presentation manifest.

## Quick start

Requirements: .NET 10 SDK, Godot 4.7 (.NET) and RogueDeck-Core checked out next to this repository.

```bash
git clone https://github.com/Paranoidgrinch/RogueDeck-Core
git clone https://github.com/Paranoidgrinch/bnb-godot
cd bnb-godot
dotnet build
godot --path .        # or open the project in the Godot editor and press Play
```

`tools/sync-content.sh` refreshes `content/game.roguedeck.json` from bnb-content.

## Project layout

The scripts are in `scripts/`.

Host and screens

- `GameHost.cs`: autoload that loads the blueprint once and owns the engine's `RunPlayback`. It turns the
  synchronous `onChanged` callback into a deferred `StateChanged` signal.
- `Boot.cs`: title screen with game identity, unlock-gated character selection, New, Continue and Archive.
- `SessionScreen.cs`: the run screen. One dispatcher over the session state (event, shop and workbench
  choices, entity picks, path forks, interludes and saves, combat, completion), plus inventory and log.
  - The combat pane has six fixed regions (heading, divine rule, arena, hand, controls, draw pile), declared
    as constants and anchored. Containers distribute minimum heights first, which previously let an Act V boss
    with a rule panel and many statuses squeeze the hero's health bar; overflowing regions now scroll.
  - The draw pile is built once per fight and updated in place, because its card back is a video and a
    recreated video restarts at 0 (checked by `--smoke-deck`).
  - The relic shelf on the right is an `HFlowContainer`. Horizontal scrolling of the sidebar is disabled on
    purpose: a horizontally scrolling `ScrollContainer` gives its child only its minimum width, which for a
    wrapping container is one tile (checked by `--smoke-shelf` with 69 relics).
- `SessionWand.cs`: the Sorcerer's Apprentice's wand, a row of its own over the combat controls: Focus and
  Exhaustion, each slot with what it will do in the next cast (the engine's `WandCompiler` plan, so a fizzle or
  a Stop is shown before anything is paid), and the five wand actions with their Focus cost and, when greyed
  out, the reason. Cast is aimed like a card and previewed on a fork of the fight (checked by `--smoke-wand`).
- `MapView.cs`: the current act's map as a graph, entry at the top and boss at the bottom. It draws
  `RunState.Map`, because the blueprint's own `Map` is empty in a generated game. Hovering a room shows who is
  in it. Names are only written across rooms in a gauntlet act with several boss rooms.
- `Glossary.cs`: explanations for every named term, built from the document. All tooltips go through it.

Archive (meta progression)

- `Archive.cs` and `ArchivePanel.cs`: the only state that survives a run. Six shelves (enemies, elites,
  cards, relics, bosses, gods) list everything the player has encountered: HP (as a range if it varies), act,
  opening move and all moves in the enemy's own intent text; card face, cost, rules and upgraded form; relics
  on their pool frame; each god's divine rule.
- The catalogue comes from the document and is the same for every player. Slots are taken from the acts'
  encounter pools, so a slot exists only if a run can actually draw it (61 of the 294 encounters are in no
  pool). Upgraded cards are filed under their base card: 134 of the 363 card definitions are `+` forms, so
  the card shelf holds 229 entries.
- The player's discoveries are stored in `user://archive.json`, not in `metastate.json`. `RunPlayback` loads
  the meta profile when a run starts and writes the loaded object back when it ends, so anything written to
  that file during a run would be overwritten.
- Discoveries are recorded in `SessionScreen.Rebuild`, because a redraw always happens while a reported
  transition can be forgotten. "Encountered" means seen, not owned. Probes and simulated runs record only in
  memory (`Archive.Robot`), so `tools/simulate.sh` never fills the player's archive.
- The gods' shelf stays hidden until the first god is met. The archive opens from the title screen and from
  the Esc menu during a run.
- "Reset progress" deletes the archive and `metastate.json` after two confirmations. It is not offered during
  a run, because `RunPlayback` would write the old meta profile back at the end of the run.

Settings, audio, theme

- `DisplaySettings.cs`: the project uses a 1280 × 720 design canvas with `canvas_items` / `expand` stretch,
  so all frontend coordinates are design units (a card is always 134 × 190). The player setting covers window
  mode, size, V-Sync and interface scale (`ContentScaleFactor`), stored in `user://settings.cfg`. The window
  settings are skipped under `--smoke*` and `--sim`, because the screenshot probes compare against a 1280 × 720
  window.
- `SettingsPanel.cs`: the settings dialog, shown on the title screen and in the Esc menu.
- `MusicDirector.cs`: autoload with two `AudioStreamPlayer`s that crossfade between ten tracks. Screens only
  report where the player is (`MusicDirector.Want`); `MusicDirector.Cue` maps the run state to one track in
  the priority Act V, boss, elite, campfire, shop, act, title. `Want` is idempotent, so redraws do not restart
  the music. An act theme resumes at its previous position after a shop or elite. Act V uses one theme for its
  map, elites and final boss.
- `AudioSettings.cs`: one global music volume in `user://settings.cfg` on a dedicated bus. Both this file and
  `DisplaySettings` load before they save, because a fresh `ConfigFile` would otherwise drop the other section.
- `CreditsPanel.cs`: credits on the title screen. Nine of the ten tracks are CC BY, so attribution is
  required. Each entry carries the attribution the author asks for, the license and the source link.
  `assets/music/SOURCES.md` records URL, SHA-256 of the download and the edits made.
- `MoonvineTheme.cs`: the palette (near-black background tinted red, antique gold for interactive elements,
  amber for highlights) and the font hook. It is the only place in the frontend that names a colour;
  `theme/README.md` explains how to change the typeface.

Cards, relics and art

- `CardVisuals.cs`: the card widget, printed on `assets/cards/card-frame.png`. Every field is a measured
  fraction of the card, so the same layout serves hand cards and thumbnails.
  - No element on a card may report a minimum size. Godot grows a `Control` to its children's minimum, which
    used to change a card's shape when clicked (checked by `--smoke-format`).
  - The hand overlaps more as it grows. The hovered card is lifted out of the fan, scaled 1.5×, straightened
    and drawn in front via `ZIndex`. Its pivot stays at the centre (used by `AnimateDraws`), so the card is
    also moved up by half its growth to keep its bottom edge in place.
  - Hover is detected on the card's click overlay, not on the face, because the overlay is what receives the
    pointer (checked by `--smoke-hover`).
  - Relic tiles are 50 px squares in the frame of their pool (normal slate, shop copper, event pale violet,
    boss a doubled antique-gold line on dark purple, elite and mimic a plainer version). The pool comes from
    `Presentation.Relics[id].Frame`.
- Art paths come from `Presentation.Art` in the document and resolve under `res://assets/art/`:
  `cards/<id>.png` (upgraded cards reuse the base picture, so 413 cards need 229 pictures), 210 relics in
  `relics/`, all 269 enemies, elites and bosses in `enemies/`, and the player characters in `characters/`.
  Bodies are drawn facing left and are never mirrored. The kind of body travels as
  `Presentation.Enemies[id].Frame` (35 boss, 66 elite, 4 mimic, 164 standard). The full list of slots is
  generated in `bnb-content/ART_SLOTS.md`.
- New pictures are only visible after Godot has imported them: run `tools/import-art.sh`; `--smoke-art`
  reports how many slots are filled. `project.godot` turns on mipmaps for imported textures, because the
  pictures are drawn much smaller than they are painted.
- The card back is `assets/cards/card-back.ogv` (animated, on the deck) and `card-back.png` (static). Both
  are cut to their drawn size, since video textures cannot be mipmapped; the ffmpeg commands for re-cutting
  are in `VISUAL_OVERHAUL_PLAN.md` under D2.
- A slot without a file is a normal state: the card shows an empty socket with its code, a relic shows its
  name. Rarity tints hand cards; card, relic and character flavour text is shown in tooltips and on the title
  screen.

Other

- `GodotMetaStore.cs`: the cross-run profile in `user://` (unlocks and discoveries).
- `content/game.roguedeck.json`: the shipped game.

## Headless checks

```text
godot --headless -- --smoke              boot, print "loaded: ..." and quit
godot --headless -- --smoke-art          art census: how many of the 709 slots have a file
godot --headless -- --smoke-generators   both map generators with the same seed: rows and rooms per act
godot --headless -- --smoke-materials    material census: the six theme surfaces, the room each act declares,
                                         and that the veil is drawn over backgrounds
godot --headless -- --smoke-music        all ten tracks present, looping, and chosen correctly per state
godot --headless -- --smoke-full         auto-play the first rooms and report the state
godot --headless -- --smoke-timing       latency per action at the first fight (about 150 ms, 2026-09-17);
                                         grows with run length, since each action replays the run
godot --headless -- --smoke-statuses     carried state shows its authored name, not its id
godot --headless -- --smoke-archive      archive catalogue per shelf and painted entries, and whether a
                                         discovery survives a write to disk; rewrites user://archive.json
godot --headless -- --smoke-archive-run  walk a seed to an elite and list what the archive recorded; checks
                                         the player's archive file is untouched, then opens the archive from
                                         the Esc menu like a player
godot --headless -- --smoke-marathon     play all five acts and report rooms and latency
godot --headless -- --smoke-screens      build every kind of screen in every act along two walks (one
                                         unlosable, one mortal) and print a table: ok, FAILED, unreached,
                                         n/a. Non-zero exit on FAILED. --seed N / --mortal-seed N
godot --headless -- --smoke-tooltips     find names on the combat screen that have no explanation
godot --headless -- --smoke-format       every card keeps its size over ten clicks
godot --headless -- --smoke-quit         Esc, "Save and quit", and check the run is offered again
godot --headless -- --smoke-shelf        relic shelf with 69 relics: rows, tiles per row, overflow,
                                         scrolling (--shelf N for another count)
godot --headless -- --smoke-boss 5 --boss inanna --rounds 12
godot --headless -- --smoke-boss 5 --boss nanna_sin --rounds 6 --plays 3
godot --headless -- --smoke-boss 5 --boss nanna_sin --seed 1 --rounds 6 --plays 3
```

The boss probe reads the divine-rule area, the card stamps in hand and the intent forecast from the live
scene tree, since a headless run cannot take a screenshot. Every screenshot check also prints a tooltip
audit.

Windowed checks (each walks to a room and saves a screenshot to `user://`):

```text
godot -- --smoke-deck      the draw-pile video keeps playing over three card plays
godot -- --smoke-window    the same fight at three window sizes
godot -- --smoke-settings  the settings dialog
godot -- --smoke-newrun    the New run dialog
godot -- --smoke-map       the act map at the first fork, hovering the boss and elite rooms
godot -- --smoke-shop      the shop, including unaffordable items
godot -- --smoke-event     an event
godot -- --smoke-ambush    a fight against several enemies
godot -- --smoke-elite     an elite fight
godot -- --smoke-crowd     the widest reachable fight: does the enemy row fit?
godot -- --smoke-boss 2    the boss of an act (phase banner, dial, chips)
godot -- --smoke-reward    the card reward
godot -- --smoke-hover     the hovered card, measured with the real pointer
godot -- --smoke-bug       fill in and send a bug report from the title screen; lists the four files
godot -- --smoke-bug-run   the same from the Esc menu during a fight
```

## Bug reports

`scripts/BugReport.cs` and `scripts/BugReportPanel.cs` add a button on the title screen and in the Esc menu.
A report contains the player's message, the save file, a screenshot, diagnostics (seed, room, version,
content document) and the end of `user://logs/godot.log`.

- The screenshot is taken before the dialog opens (`BugReport.Remember`), so it shows the moment the player
  wanted to report. The dialog shows the thumbnail.
- The report is saved locally to `user://bug-reports/<timestamp>/` (the last ten are kept) before anything is
  uploaded, so a failed upload still leaves a report.

The Discord webhook is a write credential and is never committed. `bugreport.cfg` is gitignored and searched
in this order: environment variable `BNB_BUGREPORT_WEBHOOK`, `user://bugreport.cfg`, `res://bugreport.cfg`,
next to the executable (where `tools/export.sh` copies it). Without a file the feature stops at the local
copy. To enable uploads, put a webhook URL on one line in `bugreport.cfg` at the project root. To test without
Discord:

```bash
tools/bug-sink.py 8787 &
BNB_BUGREPORT_WEBHOOK=http://127.0.0.1:8787/hook godot -- --smoke-bug
```

## Simulated runs

`scripts/RunSimulator.cs` plays through the real screens with random answers (random forks, events, cards,
targets and rewards, occasionally ending turns early or skipping rewards). It plays badly on purpose; its
purpose is coverage of content a careful player would not reach.

```text
godot --headless -- --sim [--sim-seed N] [--sim-health N | --sim-immortal] [--sim-steps N]
tools/simulate.sh                  20 runs, 400 HP, 4 processes
tools/simulate.sh 100 --jobs 8     100 runs
tools/simulate.sh 50 --immortal    runs that cannot die, to reach the later acts
tools/simulate.sh 50 --real        the game's own HP (most runs end in act I)
tools/simulate.sh 100 --ui 0       no screens drawn (about six times faster; the default --ui 5 draws the
                                   first five seeds)
tools/simulate.sh 100 --godot      every run in its own Godot process; by default only the --ui runs use
                                   Godot and the rest run in roguedeck-bot (RogueDeck-Core/src/RogueDeck.Bot.Cli),
                                   which produces identical logs for the same seed
tools/simulate.sh 100 --release    play from an exported binary, the only build with optimized engine
                                   assemblies (40.4 s against 30.4 s for the same result)
```

Logs are written to `~/Desktop/bnb-run-logs/<timestamp>/run-<seed>.log`, one process per run. A log starts
with seed and character and lists every room, choice and card play with the engine's narration;
`--sim --sim-seed N` replays it. `summary.txt` lists each run's result and which runs need attention. A run is
flagged for engine errors, exceptions, steps that threw, or turns that never ended. Cards rejected by the
rules and cards that ask a follow-up question are not flagged.

The simulator measures coverage, not balance. Balance is measured per fight, see `bnb-content/BALANCE_PLAN.md`.

## Building and releasing

Install the Godot 4.7 (.NET) export templates once (Editor, Manage Export Templates, Download and Install).

```text
tools/export.sh               build Linux and Windows binaries into build/
tools/import-art.sh           import new pictures from assets/art/
tools/release.sh              export and package into dist/<version>/
tools/release.sh --no-export  package what is already in build/
tools/release.sh --publish    also upload all packages to itch.io (moonvine-forge/bnb-alpha, password-protected)
```

C# projects cannot be exported to the web from Godot, so the targets are Linux and Windows. A release contains
a Windows installer (NSIS, per user, no admin rights, Start menu entry, uninstaller), a portable Windows ZIP,
a Linux AppImage, a Linux tarball, `README-ALPHA.txt` for testers (English and German, from
`packaging/README-ALPHA.txt`) and `SHA256SUMS.txt`.

The version is `application/config/version` in `project.godot`. It is shown on the title screen and included
in bug reports and run recordings; raise it together with the two `*_version` entries in
`export_presets.cfg`. `tools/release.sh` requires `bugreport.cfg` and `runlog.cfg`. Packages contain both
webhooks, so `dist/` is gitignored and releases are not attached to this public repository. `makensis` and
`appimagetool` do not need root (see the header of `tools/release.sh`). The icon is generated by
`tools/make-icon.py`; replace `assets/brand/icon.png` and `icon.ico` to change it.

## License

Code and game content: [PolyForm Noncommercial 1.0.0](LICENSE). Noncommercial use with attribution is
permitted; commercial use requires permission. Third-party music and sound effects keep their own licenses,
see `assets/music/SOURCES.md`, `assets/sfx/SOURCES.md` and the in-game credits.
