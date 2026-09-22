#!/usr/bin/env bash
# ── WHAT A HUMAN RUN DID, OUT OF THE SAVE THE GAME ALREADY WRITES ────────────────────────────────────────
# The game autosaves at every point the player has settled something — a turn handed over, an option taken,
# a room chosen (GameHost.AutoSave). It overwrites ONE file, so the trajectory is thrown away as it is made.
# This keeps every distinct state that file ever holds, which is the trajectory.
#
# ⚠ WHY IT HAS TO BE A WATCHER AND NOT A READ AT THE END. `Visited` is CLEARED by RunState.BeginNextAct, so
# the last save of a five-act run knows only act five's rooms. Every earlier act exists only in a snapshot
# taken before the crossing.
#
#   tools/watch-player-run.sh            # start it, then play; ctrl-C when done
#   tools/watch-player-run.sh --out DIR  # somewhere other than ~/Desktop/bnb-player-runs/<stamp>
SAVE="$HOME/.local/share/godot/app_userdata/Bureaucrats & Broomsticks/run-save.json"
desktop="$HOME/Desktop"; [[ -d "$HOME/Schreibtisch" ]] && desktop="$HOME/Schreibtisch"
out="$desktop/bnb-player-runs/$(date +%Y%m%d-%H%M%S)"
[[ ${1:-} == --out ]] && out=$2
mkdir -p "$out" || exit 1
echo "watching the autosave -> $out   (ctrl-C to stop)"
last=""; n=0
while true; do
  if [[ -f $SAVE ]]; then
    now=$(md5sum "$SAVE" 2>/dev/null | cut -d' ' -f1)
    if [[ -n $now && $now != "$last" ]]; then
      last=$now
      printf -v i '%05d' $n
      cp "$SAVE" "$out/save-$i-$(date +%H%M%S).json" 2>/dev/null && n=$((n + 1))
    fi
  fi
  sleep 0.5
done
