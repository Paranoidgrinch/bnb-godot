#!/usr/bin/env bash
# THE CHARACTER BENCH: the same seeds played by every character, side by side.
#
#   tools/bench.sh                         # 50 runs each at the game's own health, 10 immortal each
#   tools/bench.sh --runs 200 --immortal 20 --jobs 8
#   tools/bench.sh --characters "hedge_witch sorcerers_apprentice"
#
# Every character walks the SAME seeds, so a difference between two columns is the character and not the dice.
# Two bodies:
#   mortal     the game's own health — how far does each get, and where does it fall
#   immortal   nothing kills it — all five acts are walked, so the per-turn numbers cover the whole game
#
# ⚠⚠ THE PLAYER IS THE DICE BOT (BotMind): random cards at random targets, the turn sometimes ended early, the
# hero's own actions by WandBotPolicy's plain order. These numbers compare the characters UNDER THAT PLAYER. They
# say where a character's floor is and whether its machinery runs at all; they are not what a human gets out of it.
#
# ⚠ THE REPLAY SEAT. The runs go through `roguedeck-bot --replay`, the driver Godot uses, because that is the one
# whose runs the golden set records; the direct seat plays a different game from the same seed (found 2026-10-20,
# before the Apprentice) and is not used here until it is brought back in line.
#
# Logs and summary land in ~/Desktop/bnb-balance/bench/<timestamp>/.
set -uo pipefail
cd "$(dirname "$0")/.."

runs=50; immortal=10; seed_from=1; jobs=6; out=""
characters="bureaucrat hedge_witch sorcerers_apprentice lieutenant"
BOT=../RogueDeck-Core/src/RogueDeck.Bot.Cli
while [[ $# -gt 0 ]]; do
  case "$1" in
    --runs)       runs=$2; shift 2 ;;
    --immortal)   immortal=$2; shift 2 ;;
    --seed-from)  seed_from=$2; shift 2 ;;
    --jobs)       jobs=$2; shift 2 ;;
    --characters) characters=$2; shift 2 ;;
    --out)        out=$2; shift 2 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

desktop="$HOME/Desktop"; [[ -d "$HOME/Schreibtisch" ]] && desktop="$HOME/Schreibtisch"
out=${out:-$desktop/bnb-balance/bench/$(date +%Y%m%d-%H%M%S)}
mkdir -p "$out" || exit 1
dotnet build "$BOT" -c Release -v q --nologo >"$out/build.log" 2>&1 \
  || { echo "the console runner would not build — see $out/build.log"; exit 1; }
bot="$BOT/bin/Release/net10.0/roguedeck-bot"

echo "bench: $characters"
echo "  $runs mortal + $immortal immortal runs each, seeds from $seed_from, $jobs at a time -> $out"
for character in $characters; do
  for body in mortal immortal; do
    n=$runs; health=""
    [[ $body == immortal ]] && { n=$immortal; health="--immortal"; }
    (( n > 0 )) || continue
    dir="$out/$character-$body"
    start=$SECONDS
    # shellcheck disable=SC2086
    "$bot" --game content/game.roguedeck.json --runs "$n" --seed-from "$seed_from" --character "$character" \
      $health --replay --jobs "$jobs" --out "$dir" >"$dir.txt" 2>&1
    echo "  $character $body: $n runs in $((SECONDS - start)) s"
  done
done

# ── the summary ──────────────────────────────────────────────────────────────────────────────────────────
summarize() {
  local body=$1
  echo
  echo "── $body ──"
  printf '%-22s %5s %6s %7s %7s %9s %10s %11s\n' character runs wins cleared fights turns/fgt cards/turn actions/turn
  for character in $characters; do
    local dir="$out/$character-$body"
    [[ -d $dir ]] || continue
    cat "$dir"/*.log 2>/dev/null | awk -v who="$character" '
      /^sim-bench:/ {
        n++
        for (i = 2; i <= NF; i++) { split($i, kv, "="); v[kv[1]] = kv[2] }
        if (v["result"] == "Victory") wins++
        cleared += v["cleared"]; fights += v["fights"]; turns += v["turns"]
        fturns += v["turns/fight"] * v["fights"]; cards += v["cards/turn"] * v["turns"]; acts += v["actions/turn"] * v["turns"]
      }
      END {
        if (n == 0) { printf "%-22s no runs\n", who; exit }
        printf "%-22s %5d %5.0f%% %7.2f %7.1f %9.2f %10.2f %11.2f\n", who, n, 100 * wins / n, cleared / n, fights / n,
          fturns / (fights ? fights : 1), cards / (turns ? turns : 1), acts / (turns ? turns : 1)
      }'
  done
  # The wand's own numbers, turn-weighted over every run of the body.
  for character in $characters; do
    local dir="$out/$character-$body"
    grep -h '^sim-bench:.* wand:' "$dir"/*.log 2>/dev/null | awk -v who="$character" '
      {
        n++
        for (i = 2; i <= NF; i++) { split($i, kv, "="); v[kv[1]] = kv[2] }
        t = v["turns"]; turns += t
        casts += v["casts/turn"] * t; idle += v["idle"] * t; focus += v["focus-left"] * t; fill += v["fill"] * t
        ins += v["inscribed"]; exh += v["exh/cast"] * v["casts/turn"] * t; burn += v["burnouts"]
        if (v["fill5+"] != "—") { big++; fill5 += v["fill5+"] }
      }
      END {
        if (n == 0) exit
        c = casts ? casts : 1
        fmt = "  wand (%s): casts/turn %.2f · idle turns %.0f%% · focus left %.2f · fill %.0f%% · fill at 5-6 slots %s"
        fmt = fmt " · inscribed %.0f%% of cards used · exhaustion/cast %.2f · burn outs/run %.1f\n"
        printf fmt, who, casts / turns, 100 * idle / turns, focus / turns, 100 * fill / turns,
          big ? sprintf("%.0f%% (%d runs)", 100 * fill5 / big, big) : "— (never had 5)", 100 * ins / n, exh / c, burn / n
      }'
  done
  # Where the bodies stopped, by act and kind of room.
  echo "  where the runs stopped (act:room kind × runs):"
  for character in $characters; do
    local dir="$out/$character-$body"
    [[ -d $dir ]] || continue
    printf '    %-20s %s\n' "$character" "$(grep -h '^sim-clearance:' "$dir"/*.log 2>/dev/null \
      | sed -n 's/.*reached=\([0-9]*\) result=\([A-Za-z]*\).*stopped=\([^ ]*\).*/\2 \1:\3/p' \
      | awk '{ k = ($1 == "Victory") ? "won" : $2; c[k]++ } END { for (k in c) printf "%s×%d ", k, c[k] }' \
      | tr ' ' '\n' | sort | tr '\n' ' ')"
  done
  # Anything a run could not answer for.
  local bad
  bad=$(grep -l -E '!! (PROBLEM|CRASH)|error=[^n]|stopped because (a turn|the fight|nothing|an )' \
    "$out"/*-"$body"/*.log 2>/dev/null | wc -l)
  echo "  runs worth reading (a problem, an error, a wall): $bad"
}

{
  echo "bench $(date '+%Y-%m-%d %H:%M') · seeds $seed_from..$((seed_from + runs - 1)) mortal, $seed_from..$((seed_from + immortal - 1)) immortal"
  echo "player: the dice bot (random plays, WandBotPolicy for actions) — a floor, not a human"
  summarize mortal
  summarize immortal
} >"$out/summary.txt"
echo
cat "$out/summary.txt"
