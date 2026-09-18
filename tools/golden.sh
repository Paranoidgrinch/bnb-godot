#!/usr/bin/env bash
# THE INSTRUMENT, FROZEN. A fixed set of runs whose outcome is written down, so that a change meant to make
# the runner FASTER can be proved not to have changed what the runner FINDS.
#
#   tools/golden.sh --record     # play the set and write tools/golden-runs.txt (do this deliberately)
#   tools/golden.sh              # play it again and diff against that file; non-zero on any difference
#   tools/golden.sh --jobs 4     # fewer at a time (each immortal run holds ~900 MB)
#   tools/golden.sh --ui 0       # no run draws the screen (fastest); --ui 15 makes every run draw
#   tools/golden.sh --release    # play the set out of an EXPORTED binary, whose engine is optimized
#   tools/golden.sh --console    # play the set through the Godot-free console runner, one process (R4)
#   tools/golden.sh --console --replay   # …and the same set driven through the REPLAY model instead (R5)
#
# ⚠ ONE RUN OF THE SET DRAWS. Since R2a the runner only builds the screen when asked (`--sim-ui`), and the
# first immortal seed is asked. That is not decoration: the outcome lines below are recorded from a run
# that drew, so a drawn run and an undrawn one producing the SAME line is itself part of what the set
# proves. The systematic frontend check is `--smoke-screens`, not this file.
#
# ⚠ WHAT IS COMPARED IS THE OUTCOME, NOT THE CLOCK. `seconds=` is stripped before the diff and reported
# separately: the whole point of the arc this guards is to change that number, and a gate that failed when
# the runner got faster would be a gate against its own purpose.
#
# ⚠⚠ A RUN THAT DOES NOT REPORT IS A MISMATCH, NEVER A PASS. A crashed process, a missing fitness line, a
# walk that never ended — each is written into the file as the absence it is, and an absence that appears
# where a result was recorded fails the check. Silence must never read as agreement.
set -uo pipefail
cd "$(dirname "$0")/.."

GOLDEN=tools/golden-runs.txt
RELEASE_BIN=build/linux/bureaucrats-and-broomsticks.x86_64
mode=check; jobs=6; ui=1; release=no; console=no; replay=no
BOT=../RogueDeck-Core/src/RogueDeck.Bot.Cli
while [[ $# -gt 0 ]]; do
  case "$1" in
    --record) mode=record; shift ;;
    --jobs)   jobs=$2; shift 2 ;;
    --ui)     ui=$2; shift 2 ;;
    --release) release=yes; shift ;;
    # ⚠⚠ THE SECOND HALF OF THE GATE, AND THE POINT OF R4. The same fifteen runs, walked by the same brain
    # out of a process that has never heard of Godot. If both hosts reproduce this file, then the runner's
    # behaviour is in `RogueDeck.Bot` and not in either host — which is the whole claim R4 makes.
    --console) console=yes; shift ;;
    # ⚠⚠ THE GATE ON R5, AND THE MOST THAT CAN BE SAID ABOUT REPLAY. The console runner walks a run ONCE by
    # default — the bot answers the engine where it stands. `--replay` plays the same fifteen runs through
    # the replay model instead: parked at every prompt and re-executed from a baseline behind every answer,
    # which is the driver the frontend uses. If this file comes back out of BOTH, then deterministic replay
    # and direct play are the same game — a claim the project could never make with evidence before, and the
    # one that found three faults in the mid-fight save the day it was first asked.
    --replay) replay=yes; shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

# `--replay` is a property of the console runner; Godot's `--sim` has a screen to keep parked and always
# drives its run through the replay model. Asking for it without `--console` would quietly play the ordinary
# set and report a pass for something that was never tried.
if [[ $replay == yes && $console != yes ]]; then
  echo "--replay needs --console (Godot's runner already drives its run through the replay model)" >&2
  exit 2
fi

# THE SET. Twelve immortal runs, which walk all five acts and are where the content is, plus three on a
# mortal body, which die in act I — the defeat path, the run-end and the reward screens a winner never sees,
# for about a tenth of the cost of one immortal run. The seeds are 1..12 and 101..103 and are NOT chosen for
# their outcomes: a set picked for runs that win would be a set that cannot notice a run starting to lose.
IMMORTAL_SEEDS=(1 2 3 4 5 6 7 8 9 10 11 12)
MORTAL_SEEDS=(101 102 103)

desktop="$HOME/Desktop"; [[ -d "$HOME/Schreibtisch" ]] && desktop="$HOME/Schreibtisch"
out="$desktop/bnb-golden/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$out" || exit 1


# ⚠⚠ THE ENGINE IS NOT OPTIMIZED IN A DEV BUILD, AND `-c Release` DOES NOT REACH IT EITHER. Godot runs a
# project from `.godot/mono/temp/bin/Debug`, and the only build of this game whose engine assemblies are
# optimized is the EXPORTED one. `--release` exports first and plays every run out of that binary: measured
# 2026-09-18 on immortal seed 1, 40.4 s of loop against 30.4 s, same result line and same fitness line.
# The export costs about 12 s once per batch. Without export templates installed it fails LOUDLY rather than
# quietly handing back the slow build.
build_release() {
  echo "exporting a release binary (its engine is the optimized one) ..."
  if ! godot --headless --export-release "Linux" "$RELEASE_BIN" >"$1/export.log" 2>&1; then
    echo "export failed -- see $1/export.log" >&2
    echo "(--release needs the Godot export templates: editor -> Editor -> Manage Export Templates)" >&2
    exit 1
  fi
  [[ -x $RELEASE_BIN ]] || { echo "export produced no binary at $RELEASE_BIN" >&2; exit 1; }
}

if [[ $console == yes ]]; then
  dotnet build "$BOT" -c Release -v q --nologo >"$out/build.log" 2>&1 \
    || { echo "build failed — see $out/build.log"; exit 1; }
elif [[ $release == yes ]]; then
  build_release "$out"
  GAME=(./"$RELEASE_BIN" --headless)
else
  dotnet build -v q --nologo >"$out/build.log" 2>&1 || { echo "build failed — see $out/build.log"; exit 1; }
  GAME=(godot --headless)
fi

which_build="dev build"; [[ $release == yes ]] && which_build="exported release build"
drawn="$ui drawing the screen"
if [[ $console == yes ]]; then
  which_build="console runner, one process"
  which_build="console runner, one process, answering the engine inline"
  [[ $replay == yes ]] && which_build="console runner, one process, through the replay model"
  drawn="none drawing the screen (the console runner has no screen)"
fi
echo "golden set: ${#IMMORTAL_SEEDS[@]} immortal + ${#MORTAL_SEEDS[@]} mortal runs, $jobs at a time, \
$drawn, $which_build"
echo "  logs -> $out"

# The seeds that draw: the first $ui of the set as it is played below (immortal first, then mortal).
DRAWN=$(printf '%s\n' "${IMMORTAL_SEEDS[@]}" "${MORTAL_SEEDS[@]}" | head -n "$ui" | tr '\n' ' ')

export GOLDEN_OUT="$out" GOLDEN_DRAWN=" $DRAWN " GOLDEN_GAME="${GAME[*]}"
play_one() {
  local seed=$1 body=$2 log="$GOLDEN_OUT/run-$2-$(printf %04d "$1").log"
  local health; [[ $body == immortal ]] && health="--sim-immortal" || health="--sim-health 400"
  local ui=""
  [[ $GOLDEN_DRAWN == *" $seed "* ]] && ui="--sim-ui"
  local started; started=$(date +%s.%N)
  # shellcheck disable=SC2086
  timeout 3600 $GOLDEN_GAME -- --sim --sim-seed "$seed" $health $ui >"$log" 2>&1
  local elapsed; elapsed=$(awk "BEGIN{printf \"%.1f\", $(date +%s.%N) - $started}")
  # Strip the clock from what is compared; keep it beside the line as a comment for the timing report.
  local fitness result
  fitness=$(grep -m1 '^sim-fitness:' "$log" | sed 's/ seconds=[0-9.]*//')
  result=$(grep -m1 '^sim-result:' "$log" | sed 's/ seconds=[0-9.]*//')
  [[ -z $fitness ]] && fitness="sim-fitness: THE RUN PRODUCED NO FITNESS LINE"
  [[ -z $result  ]] && result="sim-result: THE RUN PRODUCED NO RESULT LINE"
  printf '%s %-9s %s\n' "$body" "seed$seed" "$fitness"
  printf '%s %-9s %s\n' "$body" "seed$seed" "$result"
  printf '#time %s %-9s %s s\n' "$body" "seed$seed" "$elapsed"
}
export -f play_one

if [[ $console == yes ]]; then
  # One process per BODY, not per run: the console runner plays a contiguous block of seeds itself.
  bot="$BOT/bin/Release/net10.0/roguedeck-bot"
  : > "$out/played.raw"
  play_block() {  # <body> <first seed> <how many> <health flag>
    local body=$1 from=$2 many=$3 health=$4 started elapsed
    started=$(date +%s.%N)
    # shellcheck disable=SC2086
    local how=""; [[ $replay == yes ]] && how="--replay"
    "$bot" --game content/game.roguedeck.json --runs "$many" --seed-from "$from" $health $how \
      --jobs "$jobs" --out "$out" >"$out/bot-$body.log" 2>&1
    elapsed=$(awk "BEGIN{printf \"%.1f\", $(date +%s.%N) - $started}")
    for ((i = 0; i < many; i++)); do
      local seed=$((from + i)) log fitness result
      log="$out/run-$(printf %04d "$seed").log"
      fitness=$(grep -m1 '^sim-fitness:' "$log" 2>/dev/null | sed 's/ seconds=[0-9.]*//')
      result=$(grep -m1 '^sim-result:'  "$log" 2>/dev/null | sed 's/ seconds=[0-9.]*//')
      [[ -z $fitness ]] && fitness="sim-fitness: THE RUN PRODUCED NO FITNESS LINE"
      [[ -z $result  ]] && result="sim-result: THE RUN PRODUCED NO RESULT LINE"
      printf '%s %-9s %s\n' "$body" "seed$seed" "$fitness" >>"$out/played.raw"
      printf '%s %-9s %s\n' "$body" "seed$seed" "$result"  >>"$out/played.raw"
    done
    # The console runner plays the block in parallel, so a per-run clock would be a share of the block's.
    # What is reported is the block's own wall time, spread evenly — informational, never compared.
    for ((i = 0; i < many; i++)); do
      printf '#time %s %-9s %s s\n' "$body" "seed$((from + i))" \
        "$(awk "BEGIN{printf \"%.1f\", $elapsed / $many}")" >>"$out/played.raw"
    done
  }
  play_block immortal "${IMMORTAL_SEEDS[0]}" "${#IMMORTAL_SEEDS[@]}" "--immortal"
  play_block mortal   "${MORTAL_SEEDS[0]}"   "${#MORTAL_SEEDS[@]}"   "--health 400"
else
{
  for seed in "${IMMORTAL_SEEDS[@]}"; do echo "$seed immortal"; done
  for seed in "${MORTAL_SEEDS[@]}";   do echo "$seed mortal";   done
} | xargs -P "$jobs" -L1 bash -c 'play_one "$@"' _ > "$out/played.raw"
fi

# The recorded lines are sorted so the order the jobs happened to finish in is not part of the contract.
grep -v '^#time ' "$out/played.raw" | sort > "$out/played.txt"
grep    '^#time ' "$out/played.raw" | sort > "$out/times.txt"

total=$(awk '{ sum += $4 } END { printf "%.1f", sum }' "$out/times.txt")
slowest=$(sort -k4 -g "$out/times.txt" | tail -1)

if [[ $mode == record ]]; then
  {
    echo "# THE GOLDEN RUN SET — what these runs did, so that making them faster can be proved harmless."
    echo "# Recorded $(date -Iseconds) by tools/golden.sh --record. Re-record ONLY when a change to the GAME"
    echo "# is intended, and say in the commit which lines moved and why."
    echo "# 'seconds=' is stripped: the clock is meant to change. Timings at the bottom are informational."
    echo "#"
    cat "$out/played.txt"
    echo "#"
    echo "# ── how long it took when recorded (informational, never compared) ──"
    cat "$out/times.txt"
    echo "# total run-seconds $total (wall clock depends on --jobs)"
  } > "$GOLDEN"
  echo
  echo "recorded $(grep -c 'sim-result:' "$out/played.txt") results into $GOLDEN"
  echo "total run-seconds $total; slowest: $slowest"
  exit 0
fi

if [[ ! -f $GOLDEN ]]; then
  echo "no $GOLDEN yet — run: tools/golden.sh --record" >&2
  exit 2
fi

grep -v '^#' "$GOLDEN" | grep -v '^[[:space:]]*$' > "$out/expected.txt"
if diff -u "$out/expected.txt" "$out/played.txt" > "$out/diff.txt"; then
  echo
  echo "GOLDEN OK — all $(grep -c 'sim-result:' "$out/played.txt") runs match what was recorded."
  if [[ $console == yes ]]; then
    # ⚠ NOT THE SAME QUANTITY. The console runner plays a whole block of seeds at once, so what is divided
    # up here is the BLOCK's wall time, not each run's own. It is printed because a reader wants to know how
    # long this took; it is deliberately NOT set beside the recorded total, which counts something else.
    echo "block wall time, spread over the runs: $total s (not comparable with the recorded total)"
  else
    echo "total run-seconds $total; slowest: $slowest"
    echo "  (recorded total was: $(grep '^# total run-seconds' "$GOLDEN" | awk '{print $4}'))"
  fi
  exit 0
fi

echo
echo "GOLDEN MISMATCH — the runner no longer finds what it found. Lines below: - recorded, + now."
sed -n '3,$p' "$out/diff.txt"
echo
echo "full logs: $out"
exit 1
