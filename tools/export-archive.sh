#!/usr/bin/env bash
# Export the whole archive — the same as the Export button in the archive: a folder on the desktop with
# index.html (a page that reads like the archive, every picture inside it) and archive.json (the same entries
# as data). Runs the game headless for a moment and quits.
set -euo pipefail
cd "$(dirname "$0")/.."
godot --headless --path . -- --export-archive 2>&1 | grep -E "export-archive|ERROR" || true
