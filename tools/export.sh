#!/usr/bin/env bash
# Build desktop binaries. Requires the Godot 4.7 (.NET) export templates installed
# (Godot editor → Editor → Manage Export Templates → Download and Install) and a
# BnbGodot.sln at the project root — Godot publishes the C# project itself during export.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build/linux build/windows
godot --headless --export-release "Linux"           build/linux/bureaucrats-and-broomsticks.x86_64
godot --headless --export-release "Windows Desktop"  build/windows/bureaucrats-and-broomsticks.exe
echo "built desktop binaries under build/"
