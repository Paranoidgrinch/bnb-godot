# bnb-godot

The player-facing frontend for **Bureaucrats & Broomsticks — Act I**, built with Godot 4.7 (.NET).
The game rules run entirely in the [RogueDeck engine](../RogueDeck-Core) (sibling checkout,
`ProjectReference`); this project renders state and forwards input, per the engine's
`docs/godot-export-contract.md`. The game content is `content/game.roguedeck.json`, exported by
[bnb-content](../bnb-content) (`tools/sync-content.sh` pulls the current document).

## Running
```
dotnet build
godot --path .            # or open in the Godot 4.7 (.NET) editor
godot --headless -- --smoke   # boot check: prints "loaded: …" and quits
```
