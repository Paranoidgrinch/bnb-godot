#!/usr/bin/env bash
# Build desktop binaries. Requires the Godot 4.7 (.NET) export templates installed
# (Godot editor → Editor → Manage Export Templates → Download and Install) and a
# BnbGodot.sln at the project root — Godot publishes the C# project itself during export.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build/linux build/windows
godot --headless --export-release "Linux"           build/linux/bureaucrats-and-broomsticks.x86_64
godot --headless --export-release "Windows Desktop"  build/windows/bureaucrats-and-broomsticks.exe
# THE WEBHOOK TRAVELS BESIDE THE BINARY, NOT INSIDE THE .PCK. It is a write credential for a channel, so it is
# not in git (see .gitignore) — and keeping it out of the package means the channel can be changed by replacing
# one small file rather than by exporting the game again. With no file, the game still takes bug reports; it
# writes them to the player's machine and says so (see scripts/BugReport.cs).
if [[ -f bugreport.cfg ]]; then
  cp bugreport.cfg build/linux/ && cp bugreport.cfg build/windows/
  echo "bug reports: webhook copied beside both binaries"
else
  echo "bug reports: NO bugreport.cfg — these builds will save reports locally and upload nothing"
fi

echo "built desktop binaries under build/"
