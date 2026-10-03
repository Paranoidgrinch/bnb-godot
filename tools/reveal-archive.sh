#!/usr/bin/env bash
# Open the whole archive: every enemy, elite, card, relic, boss and god written into the player's fund book
# (user://archive.json). Runs the game headless for a moment and quits. To start the game with it instead:
#   godot --path . -- --reveal-archive
set -euo pipefail
cd "$(dirname "$0")/.."
godot --headless --path . -- --reveal-archive 2>&1 | grep -E "reveal-archive|ERROR" || true
