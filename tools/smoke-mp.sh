#!/usr/bin/env bash
# TWO MACHINES, ONE RUN (multiplayer M6). Starts a headless host and headless joiners on localhost, lets each
# play its own seat for a few hundred answers, and checks that every process ends standing in the same place.
#
#   tools/smoke-mp.sh                       # host as the Bureaucrat, one guest as the Hedge Witch, seed 1
#   tools/smoke-mp.sh --seats 4 --seed 7    # a full table
#   tools/smoke-mp.sh --answers 800
#   tools/smoke-mp.sh --screen              # …with the run screen drawn in every process
set -uo pipefail
cd "$(dirname "$0")/.."

seats=2; seed=1; answers=300; port=24650; screen=(); hostgame=(godot --headless)
while [[ $# -gt 0 ]]; do
  case "$1" in
    --seats) seats=$2; shift 2 ;;
    --seed) seed=$2; shift 2 ;;
    --answers) answers=$2; shift 2 ;;
    --port) port=$2; shift 2 ;;
    # Every process draws the run screen as it plays: the party's fight, rooms and waits are built for real.
    --screen) screen=(--mp-screen); shift ;;
    # …and the host in a WINDOW, saving a picture of each kind of moment (a display is needed).
    --shots) screen=(--mp-screen); hostgame=(godot --rendering-driver opengl3); shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

out=$(mktemp -d)
dotnet build -v q --nologo >"$out/build.log" 2>&1 || { echo "build failed — see $out/build.log"; exit 1; }
common=(--mp-seed "$seed" --mp-answers "$answers" --mp-seats "$seats" "${screen[@]}")

timeout 600 "${hostgame[@]}" -- --smoke-mp-host "$port" --mp-character bureaucrat "${common[@]}" >"$out/seat0.log" 2>&1 &
pids=($!)
# A host in a window takes its time to come up; nobody knocks before it says it is open.
for ((t = 0; t < 240; t++)); do grep -q "^net: Hosting on port" "$out/seat0.log" 2>/dev/null && break; sleep 0.5; done
characters=(bureaucrat hedge_witch bureaucrat hedge_witch)
for ((i = 1; i < seats; i++)); do
  timeout 600 godot --headless -- --smoke-mp-join "$port" --mp-character "${characters[$i]}" "${common[@]}" >"$out/seat$i.log" 2>&1 &
  pids+=($!)
  sleep 1
done
for pid in "${pids[@]}"; do wait "$pid"; done

lines=()
for ((i = 0; i < seats; i++)); do
  line=$(grep -m1 '^smoke-mp:' "$out/seat$i.log" || echo "smoke-mp: seat $i SAID NOTHING")
  echo "$line"
  lines+=("$(sed -E 's/^smoke-mp: seat=[0-9]+ //' <<<"$line")")
done
# A screen that threw while drawing a state is a failure even when the run itself stayed in step.
if grep -lE "Unhandled exception|System\.[A-Za-z]+Exception|PROBLEM screen" "$out"/seat*.log >/dev/null; then
  grep -hE -m3 "Unhandled exception|System\.[A-Za-z]+Exception|PROBLEM screen" "$out"/seat*.log
  echo "smoke-mp: a screen threw — logs in $out"; exit 1
fi
if [[ "${lines[*]}" == *FAILED* || "${lines[*]}" == *"SAID NOTHING"* ]]; then
  echo "smoke-mp: FAILED — logs in $out"; exit 1
fi
for line in "${lines[@]}"; do
  if [[ "$line" != "${lines[0]}" ]]; then echo "smoke-mp: OUT OF STEP — logs in $out"; exit 1; fi
done
echo "smoke-mp: all $seats seats stand in the same place"
