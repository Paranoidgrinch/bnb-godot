#!/usr/bin/env bash
# THE INSTRUMENT, FROZEN. A fixed set of runs whose outcome is written down, so that a change meant to make
# the runner FASTER can be proved not to have changed what the runner FINDS.
#
#   tools/golden.sh --record     # play the set and write tools/golden-runs.txt (do this deliberately)
#   tools/golden.sh              # play it again and diff against that file; non-zero on any difference
#   tools/golden.sh --jobs 4     # fewer at a time (each immortal run holds ~900 MB)
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
mode=check; jobs=6
while [[ $# -gt 0 ]]; do
  case "$1" in
    --record) mode=record; shift ;;
    --jobs)   jobs=$2; shift 2 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

# THE SET. Twelve immortal runs, which walk all five acts and are where the content is, plus three on a
# mortal body, which die in act I — the defeat path, the run-end and the reward screens a winner never sees,
# for about a tenth of the cost of one immortal run. The seeds are 1..12 and 101..103 and are NOT chosen for
# their outcomes: a set picked for runs that win would be a set that cannot notice a run starting to lose.
IMMORTAL_SEEDS=(1 2 3 4 5 6 7 8 9 10 11 12)
MORTAL_SEEDS=(101 102 103)

desktop="$HOME/Desktop"; [[ -d "$HOME/Schreibtisch" ]] && desktop="$HOME/Schreibtisch"
out="$desktop/bnb-golden/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$out" || exit 1

dotnet build -v q --nologo >"$out/build.log" 2>&1 || { echo "build failed — see $out/build.log"; exit 1; }

echo "golden set: ${#IMMORTAL_SEEDS[@]} immortal + ${#MORTAL_SEEDS[@]} mortal runs, $jobs at a time"
echo "  logs -> $out"

export GOLDEN_OUT="$out"
play_one() {
  local seed=$1 body=$2 log="$GOLDEN_OUT/run-$2-$(printf %04d "$1").log"
  local health; [[ $body == immortal ]] && health="--sim-immortal" || health="--sim-health 400"
  local started; started=$(date +%s.%N)
  # shellcheck disable=SC2086
  timeout 3600 godot --headless -- --sim --sim-seed "$seed" $health >"$log" 2>&1
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

{
  for seed in "${IMMORTAL_SEEDS[@]}"; do echo "$seed immortal"; done
  for seed in "${MORTAL_SEEDS[@]}";   do echo "$seed mortal";   done
} | xargs -P "$jobs" -L1 bash -c 'play_one "$@"' _ > "$out/played.raw"

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
  echo "total run-seconds $total; slowest: $slowest"
  echo "  (recorded total was: $(grep '^# total run-seconds' "$GOLDEN" | awk '{print $4}'))"
  exit 0
fi

echo
echo "GOLDEN MISMATCH — the runner no longer finds what it found. Lines below: - recorded, + now."
sed -n '3,$p' "$out/diff.txt"
echo
echo "full logs: $out"
exit 1
