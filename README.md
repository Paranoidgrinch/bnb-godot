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
- `scripts/MapView.cs` — the act map as a navigable graph.
- `scripts/Boot.cs` — the title screen (game identity + unlock-gated character select + New/Continue).
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
```

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
