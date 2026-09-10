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
- `scripts/Boot.cs` — the title screen (game identity + unlock-gated character select + New/Continue).
- `scripts/Glossary.cs` — what every named thing MEANS, built once from the document: ask it about an id, or
  hand it any text and it names the terms that text uses. Every hover in the game goes through it.
- `scripts/MoonvineTheme.cs` — **the whole look**: one palette (a near-black page bled red, antique gold for
  everything you can touch, amber for everything that wants your attention) and one font hook. It is the only
  place in the frontend allowed to name a colour, and `theme/README.md` says how to swap the typeface in one
  line.
- `scripts/CardVisuals.cs` — **the card**. One widget printed on the master frame `assets/cards/card-frame.png`,
  whose holes ARE the fields: every one is a measured fraction of the card, so the same layout serves a hand
  card and a thumbnail. ⚠ Nothing on a card may report a minimum size — the root is a plain `Control` and each
  field sits in a fixed clipped window, because Godot clamps a Control's size *up* to its children's combined
  minimum and that is what used to change a card's shape when it was clicked (`--smoke-format` measures it).
  A card's picture is `assets/art/cards/<id>.png` — the path the document itself declares in
  `Presentation.Art`, so the contract's path IS the path on disk and there is nothing to register.
  An upgraded card has no picture of its own (`levy_stamp+` draws `levy_stamp.png`), so 413 cards ask
  for 254 pictures; relics ask for 210 more under `assets/art/relics/`. The whole list, with the design
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
- `scripts/SessionScreen.cs` — every room, and **the shelf** on the right edge: what you are wearing is
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
godot --headless -- --smoke-art     # the art census: how many of the 464 slots have a file
godot --headless -- --smoke-full    # auto-plays the first rooms and reports the state
godot --headless -- --smoke-timing  # per-action latency (~17 ms/action)
godot --headless -- --smoke-statuses # carried state reads as its authored name, not its id
godot --headless -- --smoke-marathon # play the WHOLE game (all five acts) and report rooms + latency
godot --headless -- --smoke-tooltips # audit a combat screen: is anything NAMED but not explained?
godot --headless -- --smoke-format   # every card in the hand is exactly the size it was handed,
                                     # ten clicks apart (windowed: it also takes the shot)
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
godot -- --smoke-map      # the act map at the entry fork
godot -- --smoke-shop     # the shelf, with prices, including what is unaffordable
godot -- --smoke-event    # a door
godot -- --smoke-ambush   # a multi-enemy fight
godot -- --smoke-elite    # an elite
godot -- --smoke-crowd    # the widest fight it can reach: does the enemy row still fit on the screen?
godot -- --smoke-boss 2   # walk to that act's BOSS and capture it (the phase banner, the dial, the chips)
godot -- --smoke-reward   # the card reward
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

## Presentation
Art/flavor/rarity come from the blueprint's presentation manifest (never from the engine): card rarity
tints the hand, card/relic flavor shows as tooltips, character flavor shows on the title. An `Art` path
resolves under `res://assets/art/`, which is why `cards/levy_stamp.png` in the document is
`assets/art/cards/levy_stamp.png` on disk. A slot with no file is a normal state, not an error: the card
draws an empty socket with its own code printed in it, and the relic draws its name.
