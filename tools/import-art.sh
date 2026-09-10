#!/usr/bin/env bash
# Make Godot see the art files that were dropped into assets/art/.
#
# res:// holds what the importer has scanned and nothing else, so a PNG that was merely copied into the folder
# does not exist for the running game — the editor imports on focus, a headless run never does. Run this once
# after adding files; it is idempotent and only touches what is new.
set -euo pipefail
cd "$(dirname "$0")/.."
godot --headless --path . --import
echo "imported — $(find assets/art -name '*.png' | wc -l) art file(s) in assets/art"
