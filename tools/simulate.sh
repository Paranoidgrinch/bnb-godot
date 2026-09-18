#!/usr/bin/env bash
# Play a batch of RANDOM runs through the real game and file every log.
#
#   tools/simulate.sh              # 20 runs, tough body (400 hp), 4 at a time
#   tools/simulate.sh 100          # 100 runs
#   tools/simulate.sh 50 --real    # 50 runs at the game's own health (most die early)
#   tools/simulate.sh 50 --immortal   # nothing can kill them: the deepest content coverage
#   tools/simulate.sh 30 --seed-from 500 --jobs 8 --out ~/somewhere
#   tools/simulate.sh 50 --legacy      # walk the OLD maps (v0.0.0) instead of the design's v0.0.1
#   tools/simulate.sh 100 --ui 0       # draw nothing at all (fastest); --ui 100 draws every run
#   tools/simulate.sh 100 --release    # the DRAWING runs come out of an EXPORTED binary (optimized engine)
#   tools/simulate.sh 100 --godot      # every run through Godot, one process each, the way it was before R4
#
# TWO HOSTS, ONE BRAIN (R4). The runs that draw go through Godot, because that is the only host with a
# screen to check. Everything else goes through `roguedeck-bot` — the same `RogueDeck.Bot` answer loop, in
# ONE process on M threads, which is where the per-process five seconds of boot + document parse stop being
# paid N times. Measured 2026-09-18 on this machine: 48 short runs, 72.0 s the old way and 32.6 s this way;
# 12 whole-game runs, 133.2 s and 121.2 s — a long run drowns its own boot, a short one does not.
#
# THE SCREEN. A run that draws costs about SIX TIMES a run that does not (measured: 228.0 s against 38.7 s
# for immortal seed 1), and the walk is identical either way -- the screen reads the session, it never
# answers for it. So the first `--ui N` runs of the batch draw and the rest do not: the frontend is still
# walked every day, at a price the balance question can afford. A redraw that throws is now counted into
# that run's `problems` and fails it, so the drawing runs test something for the first time.
#
# One Godot process per run, so a crash costs that run and not the batch. Logs land in
#   ~/Desktop/bnb-run-logs/<timestamp>/run-<seed>.log
# with a summary.txt naming every run worth looking at.
set -uo pipefail
cd "$(dirname "$0")/.."

count=${1:-20}; [[ $count == --* ]] && count=20 || shift 2>/dev/null || true
health="--sim-health 400"; seed_from=1; jobs=4; out=""; maps=""; ui=5; release=no; godot_only=no
RELEASE_BIN=build/linux/bureaucrats-and-broomsticks.x86_64
BOT=../RogueDeck-Core/src/RogueDeck.Bot.Cli
while [[ $# -gt 0 ]]; do
  case "$1" in
    --real)      health=""; shift ;;
    --immortal)  health="--sim-immortal"; shift ;;
    --health)    health="--sim-health $2"; shift 2 ;;
    --seed-from) seed_from=$2; shift 2 ;;
    --jobs)      jobs=$2; shift 2 ;;
    --out)       out=$2; shift 2 ;;
    # The runner walks v0.0.1, the design's maps, unless this says otherwise. It is passed to the game
    # rather than read from the player's settings so a batch means the same thing on every machine.
    --legacy)    maps="--legacy"; shift ;;
    # How many runs of the batch draw the screen (the first N seeds). 0 = none, >= count = all.
    --ui)        ui=$2; shift 2 ;;
    # ⚠⚠ THE ENGINE IS NOT OPTIMIZED IN A DEV BUILD, AND `-c Release` DOES NOT REACH IT EITHER. Godot runs
    # a project from `.godot/mono/temp/bin/Debug`; the only build of this game whose engine assemblies are
    # optimized is the EXPORTED one. Measured 2026-09-18, immortal seed 1: 40.4 s of loop against 30.4 s,
    # same result line, same fitness line. The export costs about 12 s once for the whole batch.
    --release)   release=yes; shift ;;
    # Every run through Godot, one process each. Slower, and no better at anything `--ui <count>` cannot do.
    --godot)     godot_only=yes; shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

desktop="$HOME/Desktop"; [[ -d "$HOME/Schreibtisch" ]] && desktop="$HOME/Schreibtisch"
out=${out:-$desktop/bnb-run-logs/$(date +%Y%m%d-%H%M%S)}
mkdir -p "$out" || exit 1
# The instructions live NEXT TO the logs, refreshed on every batch, so the folder explains itself.
[[ -f tools/run-logs-README.md ]] && cp tools/run-logs-README.md "$(dirname "$out")/ANLEITUNG.md"

if [[ $release == yes ]]; then
  echo "exporting a release binary (its engine is the optimized one) ..."
  # It fails LOUDLY rather than quietly handing the batch back the slow build.
  godot --headless --export-release "Linux" "$RELEASE_BIN" >"$out/export.log" 2>&1 \
    || { echo "export failed — see $out/export.log"; \
         echo "(--release needs the Godot export templates: editor → Editor → Manage Export Templates)"; \
         exit 1; }
  [[ -x $RELEASE_BIN ]] || { echo "export produced no binary at $RELEASE_BIN"; exit 1; }
  GAME="./$RELEASE_BIN --headless"
else
  dotnet build -v q --nologo >"$out/build.log" 2>&1 || { echo "build failed — see $out/build.log"; exit 1; }
  GAME="godot --headless"
fi
which_build="dev build"; [[ $release == yes ]] && which_build="exported release build"
[[ $godot_only == yes ]] && ui=$count
drawing=$(( ui < count ? ui : count )); (( drawing < 0 )) && drawing=0
ui_last=$(( seed_from + drawing - 1 ))
by_bot=$(( count - drawing ))

# The same body and the same maps, said in the console runner's words.
bot_health=""
case "$health" in
  --sim-immortal)   bot_health="--immortal" ;;
  "--sim-health "*) bot_health="--health ${health#--sim-health }" ;;
esac
bot_maps=""; [[ -n $maps ]] && bot_maps="--legacy"

if (( by_bot > 0 )); then
  dotnet build "$BOT" -c Release -v q --nologo >>"$out/build.log" 2>&1 \
    || { echo "the console runner would not build — see $out/build.log"; exit 1; }
fi

echo "simulating $count runs (seeds $seed_from..$((seed_from + count - 1)), ${health:-authored health}, \
maps ${maps:+v0.0.0}${maps:-v0.0.1}, $jobs at a time)"
echo "  $drawing through Godot with the screen on ($which_build), $by_bot through the console runner"
echo "  -> $out"

export SIM_OUT="$out" SIM_HEALTH="$health" SIM_MAPS="$maps" SIM_UI_LAST="$ui_last" SIM_GAME="$GAME"
run_one() {
  local seed=$1 log="$SIM_OUT/run-$(printf %04d "$1").log"
  local ui=""
  (( seed <= SIM_UI_LAST )) && ui="--sim-ui"
  # shellcheck disable=SC2086
  timeout 1800 $SIM_GAME -- --sim --sim-seed "$seed" $SIM_HEALTH $SIM_MAPS $ui >"$log" 2>&1
  local code=$?
  printf 'seed %-5s exit %-3s %s\n' "$seed" "$code" \
    "$(grep -m1 '^sim-result:' "$log" || echo 'no result line — the process died')"
}
export -f run_one

{
  # The drawing runs: Godot, one process each, so a crash costs that run and not the batch.
  if (( drawing > 0 )); then
    seq "$seed_from" "$ui_last" | xargs -P "$jobs" -I{} bash -c 'run_one {}'
  fi
  # The rest: one process, M threads, the same brain. Its summary line per run is already in this format —
  # it IS the same `sim-result:` line, printed by the same BotReport.
  if (( by_bot > 0 )); then
    # shellcheck disable=SC2086
    "$BOT/bin/Release/net10.0/roguedeck-bot" --game content/game.roguedeck.json \
      --runs "$by_bot" --seed-from "$((ui_last + 1))" $bot_health $bot_maps \
      --jobs "$jobs" --out "$out" | grep '^seed '
  fi
} | tee "$out/summary.txt"

{
  echo
  echo "── outcomes ──"
  grep -ho 'result=[A-Za-z]*' "$out"/run-*.log | sort | uniq -c | sort -rn
  echo
  echo "── runs worth reading (a problem, an error, a wall, or a crash) ──"
  grep -l -E '!! (PROBLEM|CRASH)|error=[^n]|stopped because (a turn|the fight|nothing|an )' \
    "$out"/run-*.log 2>/dev/null | while read -r log; do
      echo "$(basename "$log"): $(grep -m1 -E '!! (PROBLEM|CRASH)|stopped because' "$log" | cut -c1-160)"
    done
  echo
  echo "── content the batch touched ──"
  echo "rooms (encounter/event/shop id × visits):"
  sed -n 's/.*ROOM act [0-9]* [^ ]* (\([^)]*\)).*/\1/p' "$out"/run-*.log | sort | uniq -c | sort -rn
  echo
  echo "cards played (distinct): $(sed -n 's/.*play \([^ ]*\) ->.*/\1/p' "$out"/run-*.log | sort -u | wc -l)"
  echo "event choices taken (distinct): $(sed -n 's/.*choice \[\([^]]*\)\] -> \([^ ]*\).*/\1 \2/p' "$out"/run-*.log | sort -u | wc -l)"
  echo "offers picked (distinct): $(sed -n 's/.*pick \[\([^]]*\)\] -> \(.*\) (of.*/\1: \2/p' "$out"/run-*.log | sort -u | wc -l)"
} >>"$out/summary.txt" 2>&1

echo
echo "summary: $out/summary.txt"
tail -n 30 "$out/summary.txt"
