#!/usr/bin/env bash
# Pull the current exported game document from the sibling bnb-content checkout.
set -euo pipefail
cd "$(dirname "$0")/.."
cp ../bnb-content/game.roguedeck.json content/game.roguedeck.json
echo "synced content/game.roguedeck.json"
