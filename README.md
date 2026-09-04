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
- `scripts/MoonvineTheme.cs` — the Moonvine Forge look (tokens mirrored from the Studio's `studio.css`).
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
godot --headless -- --smoke-full    # auto-plays the first rooms and reports the state
godot --headless -- --smoke-timing  # per-action latency (~17 ms/action)
godot --headless -- --smoke-statuses # carried state reads as its authored name, not its id
godot --headless -- --smoke-marathon # play the WHOLE game (both acts) and report rooms + latency
godot --headless -- --smoke-tooltips # audit a combat screen: is anything NAMED but not explained?
```
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
tools/simulate.sh 50 --immortal    # nothing can kill them: the deepest reach into acts II/III
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
to the last one the game has (`LAST_ACT` in `train.py`); the simulator's own fitness line names no act, only
the per-act table (`actBossDamage`). Least taken wins; never arriving is worse than any arrival. Damage ADDED UP, not health remaining: no act heals you at its end, but the content heals
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
```
C# has no Godot web export, so the targets are desktop (Linux / Windows).

## Presentation
Art/flavor/rarity come from the blueprint's presentation manifest (never from the engine): card rarity
tints the hand, card/relic flavor shows as tooltips, character flavor shows on the title. `Art` paths
would resolve under `res://assets/…`; with no assets shipped yet, entities fall back to their styled
text panels — swapping in art later needs no code change.
