#!/usr/bin/env bash
# Play a batch of RANDOM runs through the real game and file every log.
#
#   tools/simulate.sh              # 20 runs, tough body (400 hp), 4 at a time
#   tools/simulate.sh 100          # 100 runs
#   tools/simulate.sh 50 --real    # 50 runs at the game's own health (most die early)
#   tools/simulate.sh 50 --immortal   # nothing can kill them: the deepest content coverage
#   tools/simulate.sh 30 --seed-from 500 --jobs 8 --out ~/somewhere
#
# One Godot process per run, so a crash costs that run and not the batch. Logs land in
#   ~/Desktop/bnb-run-logs/<timestamp>/run-<seed>.log
# with a summary.txt naming every run worth looking at.
set -uo pipefail
cd "$(dirname "$0")/.."

count=${1:-20}; [[ $count == --* ]] && count=20 || shift 2>/dev/null || true
health="--sim-health 400"; seed_from=1; jobs=4; out=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --real)      health=""; shift ;;
    --immortal)  health="--sim-immortal"; shift ;;
    --health)    health="--sim-health $2"; shift 2 ;;
    --seed-from) seed_from=$2; shift 2 ;;
    --jobs)      jobs=$2; shift 2 ;;
    --out)       out=$2; shift 2 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

desktop="$HOME/Desktop"; [[ -d "$HOME/Schreibtisch" ]] && desktop="$HOME/Schreibtisch"
out=${out:-$desktop/bnb-run-logs/$(date +%Y%m%d-%H%M%S)}
mkdir -p "$out" || exit 1
# The instructions live NEXT TO the logs, refreshed on every batch, so the folder explains itself.
[[ -f tools/run-logs-README.md ]] && cp tools/run-logs-README.md "$(dirname "$out")/ANLEITUNG.md"

dotnet build -v q --nologo >"$out/build.log" 2>&1 || { echo "build failed — see $out/build.log"; exit 1; }
echo "simulating $count runs (seeds $seed_from..$((seed_from + count - 1)), ${health:-authored health}, $jobs at a time)"
echo "  -> $out"

export SIM_OUT="$out" SIM_HEALTH="$health"
run_one() {
  local seed=$1 log="$SIM_OUT/run-$(printf %04d "$1").log"
  # shellcheck disable=SC2086
  timeout 1800 godot --headless -- --sim --sim-seed "$seed" $SIM_HEALTH >"$log" 2>&1
  local code=$?
  printf 'seed %-5s exit %-3s %s\n' "$seed" "$code" \
    "$(grep -m1 '^sim-result:' "$log" || echo 'no result line — the process died')"
}
export -f run_one

seq "$seed_from" "$((seed_from + count - 1))" \
  | xargs -P "$jobs" -I{} bash -c 'run_one {}' \
  | tee "$out/summary.txt"

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
