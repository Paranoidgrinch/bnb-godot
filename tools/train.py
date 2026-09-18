#!/usr/bin/env python3
"""Breed runners for the balance question: starting at 9999 hp, how much health does the game take off a
player on the way to a named act's boss?

WHICH act is `--target-act`, and it defaults to the last one the game has. It used to be act III because act
III was the end of the game; the simulator no longer names an act of its own either (it reports the whole
per-act table), so moving the measurement to a new act is this flag and nothing else.

A runner is a policy — the weights in scripts/RunSimulator.cs's SimPolicy — and a generation is a handful of
them, each played over the same content seeds so the comparison is fair. The ones that arrive at the target
boss having lost the least survive and are mutated into the next generation. A runner that never gets there
is worse than any that does, however little it lost on the way.

    tools/train.py                                   # 5 generations of 8, 2 seeds each, 4 at a time
    tools/train.py --target-act 3                    # measure to an EARLIER act's boss instead
    tools/train.py --generations 10 --population 12 --seeds 3 --jobs 8
    tools/train.py --resume ~/Desktop/bnb-balance-training/<stamp>   # keep breeding from its best

Everything lands in ~/Desktop/bnb-balance-training/<timestamp>/ — one folder per generation with the
policies and their run logs, a leaderboard.csv over all of them, and best-policy.json at the top.
"""
import argparse, csv, json, os, random, re, shutil, subprocess, sys, time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
# name: (low, high) — the space a runner is bred in.
GENES = {
    "WDamage": (-2, 3), "WBlock": (-2, 3), "WStatus": (-2, 3), "WDraw": (-2, 3), "WResource": (-2, 3),
    "WCost": (-3, 1), "EndTurnBelow": (-1, 3), "TargetLowestHp": (0, 1),
    "PathCombat": (0, 1), "PathElite": (0, 1), "PathShop": (0, 1), "PathRest": (0, 1),
    "PathEvent": (0, 1), "PathTreasure": (0, 1), "RewardSkip": (0, 1), "ShopBuy": (0, 1), "EventLate": (0, 1),
}
UNREACHED = 1_000_000   # never arriving at the target boss is worse than any arrival
# The last act the game has, and so the default thing to measure to. One number, kept next to the flag that
# reads it, because "the end of the game" is a fact about the content and moves when the content does.
#
# It moved to 5 at V-0, when Act V became a walkable act, and the placeholders it stood on are gone: V-1 … V-6
# replaced all six gods with the authored fights (2026-09-09). Breeding against act 5 is therefore breeding
# against the finished game, and `--target-act 5` — the default — is now the meaningful balance question.
LAST_ACT = 5


def random_policy(rng, name):
    p = {"Name": name}
    p.update({g: round(rng.uniform(lo, hi), 3) for g, (lo, hi) in GENES.items()})
    return p


def mutate(rng, parent, name, sigma):
    child = {"Name": name}
    for g, (lo, hi) in GENES.items():
        span = hi - lo
        child[g] = round(min(hi, max(lo, parent[g] + rng.gauss(0, sigma * span))), 3)
    return child


BOT = REPO.parent / "RogueDeck-Core" / "src" / "RogueDeck.Bot.Cli"
BOT_BIN = BOT / "bin" / "Release" / "net10.0" / "roguedeck-bot"


def reading(text, target_act):
    """Was aus dem Lauf-Log herausgelesen wird — dieselben Zeilen, egal welcher Wirt sie schrieb.

    `sim-fitness:` traegt die alte Frage (was hat der Weg zum Boss an Schaden gekostet, bei 9999 HP),
    `sim-clearance:` die echte (ist ein WIRKLICHER Koerper durchgekommen, und wenn nicht, wo blieb er).
    """
    lines = text.splitlines()
    clear = next((l for l in lines if l.startswith("sim-clearance:")), None)
    cleared, died, hp = 0, "", 0
    if clear:
        c = dict(re.findall(r"(\w+)=(\S+)", clear))
        cleared = int(c.get("cleared", 0))
        hp = int(c.get("hp", "0/0").split("/")[0])
        # `at=` ist das letzte Feld und traegt Leerzeichen ("act 4 r12c0 (…)"), also bis Zeilenende lesen.
        died = clear.split(" at=", 1)[1].strip() if " at=" in clear else ""
    line = next((l for l in lines if l.startswith("sim-fitness:")), None)
    if not line:
        return {"reached": False, "damage": UNREACHED, "rooms": 0, "cleared": cleared, "hp": hp,
                "died": died, "note": "no fitness line — the run died"}
    f = dict(re.findall(r"(\w+)=(\S+)", line))
    # actBossDamage="1:120,2:310,3:604" — what the run had lost, added up, when it entered each act's boss
    # room. The target act's entry is the measurement; its absence is the miss.
    table = dict(
        (int(a), int(b))
        for a, b in (pair.split(":") for pair in f.get("actBossDamage", "").split(",") if ":" in pair))
    reached = target_act in table
    return {"reached": reached,
            # Damage ADDED UP, not health remaining: the content heals, and one act-II door heals to full.
            "damage": table[target_act] if reached else UNREACHED,
            "rooms": int(f.get("rooms", 0)),
            "cleared": cleared, "hp": hp, "died": died,
            "note": "" if reached else f"never reached the act-{target_act} boss"}


# ── EIN RUNNER, ALLE SEEDS, EIN PROZESS (der schnelle Weg) ───────────────────────────────────────────────
# ⚠⚠ DER TRAINER HAT R4/R5 JAHRELANG NICHT MITBEKOMMEN. Er startete fuer JEDEN Lauf ein eigenes Godot — mit
# Bootzeit, 11-MB-Dokument und kaltem JIT pro Lauf, und dazu dem Replay-Modell, das den Lauf hinter jeder
# Antwort neu ausfuehrt. Der Konsolen-Laeufer spielt einen ganzen Block Seeds in EINEM Prozess und antwortet
# der Engine direkt: gemessen 2026-09-18 rund 20 s statt 40 s je Lauf, und die festen Kosten fallen einmal
# statt einmal pro Lauf. Gezuechtet wird damit dasselbe Spiel — das Golden-Set beweist, dass beide Wirte
# denselben Lauf gehen; `--godot` bleibt als Rueckweg, wenn genau das einmal bezweifelt wird.
def play_block(policy_file, seeds, out_dir, timeout, health, target_act, maps):
    out_dir.mkdir(parents=True, exist_ok=True)
    with open(out_dir / "bot.log", "w") as log:
        try:
            subprocess.run(
                [str(BOT_BIN), "--game", "content/game.roguedeck.json",
                 "--runs", str(len(seeds)), "--seed-from", str(seeds[0]),
                 *health, *maps, "--policy", str(policy_file),
                 "--jobs", str(len(seeds)), "--out", str(out_dir)],
                cwd=REPO, stdout=log, stderr=subprocess.STDOUT,
                timeout=timeout * len(seeds), check=False)
        except subprocess.TimeoutExpired:
            log.write("\n!! the block was cut off by the trainer's timeout\n")
    results = []
    for seed in seeds:
        run_log = out_dir / f"run-{seed:04d}.log"
        results.append(reading(run_log.read_text(errors="replace") if run_log.exists() else "", target_act))
    return results


# ── DERSELBE LAUF DURCH DEN BILDSCHIRM (--godot) ─────────────────────────────────────────────────────────
def play(policy_file, seed, log_file, timeout, health, target_act, maps, draw=False):
    with open(log_file, "w") as log:
        try:
            subprocess.run(
                ["godot", "--headless", "--", "--sim", "--sim-seed", str(seed),
                 *health, *maps, "--sim-policy", str(policy_file),
                 *(["--sim-ui"] if draw else [])],
                cwd=REPO, stdout=log, stderr=subprocess.STDOUT, timeout=timeout, check=False)
        except subprocess.TimeoutExpired:
            log.write("\n!! the run was cut off by the trainer's timeout\n")
    return reading(Path(log_file).read_text(errors="replace"), target_act)


def evaluate(policies, seeds, gen_dir, jobs, timeout, health, target_act, maps, ui=0, godot=False,
             question="clearance"):
    """Every policy over every seed, in parallel; a policy's score is its mean hp lost."""
    paths = {}
    for policy in policies:
        paths[policy["Name"]] = gen_dir / f"{policy['Name']}.json"
        paths[policy["Name"]].write_text(json.dumps(policy, indent=2))

    scored = {}
    if not godot:
        # Ein Aufruf je Runner, der alle seine Seeds in EINEM Prozess spielt; mehrere Runner nebeneinander,
        # bis die Auftraege alle sind. `jobs` bleibt die Zahl gleichzeitiger LAEUFE, nicht Prozesse.
        at_once = max(1, jobs // max(1, len(seeds)))
        with ThreadPoolExecutor(max_workers=at_once) as pool:
            blocks = list(pool.map(
                lambda policy: play_block(paths[policy["Name"]], seeds,
                                          gen_dir / policy["Name"], timeout, health, target_act, maps),
                policies))
        for policy, runs in zip(policies, blocks):
            scored[policy["Name"]] = runs
        return _rank(policies, scored, question, target_act)

    work = []
    for policy in policies:
        for seed in seeds:
            work.append((policy, paths[policy["Name"]], seed, gen_dir / f"{policy['Name']}-seed{seed}.log"))
    # The screen costs about six times the run it draws and teaches the SEARCH nothing — the fitness line
    # comes from the session, not from the nodes. So the trainer draws nothing unless asked (--ui N draws
    # the first N runs of each generation); the daily frontend check is tools/simulate.sh, which draws 5.
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        results = list(pool.map(
            lambda iw: play(iw[1][1], iw[1][2], iw[1][3], timeout, health, target_act, maps,
                            draw=iw[0] < ui),
            enumerate(work)))
    for (policy, _, seed, _), result in zip(work, results):
        scored.setdefault(policy["Name"], []).append(result)
    return _rank(policies, scored, question, target_act)


# ⚠⚠ ZWEI FRAGEN, UND NUR EINE DAVON IST DIE ECHTE (B6).
#
#   "damage"    — die alte: 9999 HP, gewertet wird der aufsummierte Schaden bis zum Boss eines Akts. Sie ist
#                 ein STELLVERTRETER, und ein schlechter: ein unsterblicher Laeufer muss nie ueberleben, nie
#                 blocken, nie gewinnen. Seit der Bewerter Groessen sieht (B3), findet die Suche den Ausweg
#                 sofort — die beste Zucht gegen diese Frage lernte `WDamage = -1.84`, also "greif nicht an":
#                 wer nichts toetet, wird nicht zurueckgeschlagen, er braucht nur laenger.
#   "clearance" — die echte, und die Frage, die V-7 beantworten soll: ECHTES Leben, echter Tod, gewertet wird
#                 pro Seed, ob der Akt geschafft wurde. Ein Runner, der nicht angreift, raeumt keinen Akt.
#
# Gleichstand wird nach Strecke und Restleben gebrochen, nie nach genommenem Schaden: wie teuer ein Sieg war,
# ist eine Frage an den BALANCE-Bericht, nicht an die Auslese.
def _rank(policies, scored, question, target_act):
    table = []
    for policy in policies:
        runs = scored[policy["Name"]]
        if question == "clearance":
            through = sum(1 for r in runs if r["cleared"] >= target_act)
            rooms = sum(r["rooms"] for r in runs) / len(runs)
            health = sum(r["hp"] for r in runs) / len(runs)
            score = (len(runs) - through) * 10_000 - rooms * 10 - health
            note = "; ".join(sorted({r["died"] for r in runs if r["cleared"] < target_act and r["died"]}))
            table.append({"policy": policy, "score": round(score, 1),
                          "arrivals": f"{through}/{len(runs)}", "rooms": round(rooms, 1), "note": note})
            continue
        arrivals = sum(1 for r in runs if r["reached"])
        # A miss is penalised by how far it got, so a runner that walks further ranks above one that stalls.
        score = sum(r["damage"] if r["reached"] else UNREACHED - r["rooms"] * 100 for r in runs) / len(runs)
        table.append({"policy": policy, "score": round(score, 1), "arrivals": f"{arrivals}/{len(runs)}",
                      "rooms": round(sum(r["rooms"] for r in runs) / len(runs), 1),
                      "note": "; ".join(sorted({r["note"] for r in runs if r["note"]}))})
    return sorted(table, key=lambda row: row["score"])


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--generations", type=int, default=5)
    ap.add_argument("--population", type=int, default=8)
    ap.add_argument("--survivors", type=int, default=3)
    ap.add_argument("--seeds", type=int, default=2, help="content seeds every runner is judged on")
    ap.add_argument("--seed-from", type=int, default=1000)
    ap.add_argument("--jobs", type=int, default=4)
    ap.add_argument("--ui", type=int, default=0,
                    help="runs per generation that draw the screen (about 6x slower each; the trainer "
                         "does not need it -- tools/simulate.sh is where the frontend gets walked)")
    ap.add_argument("--timeout", type=int, default=2400, help="seconds a single run may take")
    ap.add_argument("--sigma", type=float, default=0.18, help="mutation size, as a share of each gene's range")
    ap.add_argument("--out", default=None)
    ap.add_argument("--health", type=int, default=0,
                    help="a body of this size instead of the immortal 9999 — only for shaking the trainer out")
    ap.add_argument("--question", choices=("clearance", "damage"), default="clearance",
                    help="clearance (default, B6): a REAL body, and the score is whether it got through the "
                         "target act -- the question V-7 asks. damage: the old proxy, 9999 hp and the damage "
                         "added up on the way to that act's boss, which a runner that never attacks wins")
    ap.add_argument("--target-act", type=int, default=None,
                    help="the act that has to be cleared (clearance) or measured to (damage). Default: 4 for "
                         "clearance -- the design's promise is that every seed is beatable through act IV, "
                         f"act V is the cherry -- and {LAST_ACT} for damage")
    ap.add_argument("--legacy", action="store_true",
                    help="breed against the OLD maps (v0.0.0) instead of the design's v0.0.1")
    ap.add_argument("--resume", default=None, help="a previous training folder to keep breeding from")
    ap.add_argument("--godot", action="store_true",
                    help="breed through the GAME instead of the console runner: one Godot process per run, "
                         "driven through the replay model. About twice the wall clock per run plus a boot "
                         "each time -- the way back if the two hosts are ever doubted (tools/golden.sh is "
                         "what says they agree)")
    args = ap.parse_args()
    if args.target_act is None:
        args.target_act = 4 if args.question == "clearance" else LAST_ACT

    desktop = Path.home() / ("Schreibtisch" if (Path.home() / "Schreibtisch").is_dir() else "Desktop")
    out = Path(args.out) if args.out else desktop / "bnb-balance-training" / time.strftime("%Y%m%d-%H%M%S")
    out.mkdir(parents=True, exist_ok=True)
    shutil.copy(REPO / "tools" / "training-README.md", out.parent / "ANLEITUNG.md")

    if args.godot:
        build = subprocess.run(["dotnet", "build", "-v", "q", "--nologo"],
                               cwd=REPO, capture_output=True, text=True)
    else:
        build = subprocess.run(["dotnet", "build", str(BOT), "-c", "Release", "-v", "q", "--nologo"],
                               cwd=REPO, capture_output=True, text=True)
    if build.returncode != 0:
        print(build.stdout[-2000:]); sys.exit("build failed")

    # ⚠⚠ DIE ECHTE FRAGE BRAUCHT EINEN ECHTEN KOERPER. Auf die Schaden-Frage wird unsterblich gezuechtet
    # (nur so kommt jeder Runner ueberhaupt bis zum Boss und ist vergleichbar); auf die Raeum-Frage mit dem
    # Leben, das das Spiel AUTORIERT hat — sonst wird Sterben kostenlos und die Auslese misst nichts.
    # Dieselbe Frage, zwei Wirte, zwei Schreibweisen: das Spiel nimmt `--sim-*`, der Konsolen-Laeufer nicht.
    body = ["--sim-health", str(args.health)] if args.health else []
    if args.godot:
        health = body or (["--sim-immortal"] if args.question == "damage" else [])
    else:
        health = (["--health", str(args.health)] if args.health else []) \
            or (["--immortal"] if args.question == "damage" else [])
    # WHICH MAPS THE RUNNERS ARE BRED AGAINST. Passed to the game, never read from the player's settings:
    # a policy bred on one generator is not a policy for the other, and a leaderboard that cannot say which
    # one it walked is a leaderboard about an unknown act.
    maps = ["--legacy"] if args.legacy else []
    rng = random.Random(7)
    seeds = list(range(args.seed_from, args.seed_from + args.seeds))
    population = []
    if args.resume:
        best = json.loads((Path(args.resume) / "best-policy.json").read_text())
        population = [dict(best, Name="g0-p0")] + [mutate(rng, best, f"g0-p{i}", args.sigma)
                                                   for i in range(1, args.population)]
    else:
        population = [random_policy(rng, f"g0-p{i}") for i in range(args.population)]

    board = out / "leaderboard.csv"
    with board.open("w", newline="") as f:
        csv.writer(f).writerow(["generation", "policy",
                                f"score (seeds cleared through act {args.target_act}, then distance)"
                                if args.question == "clearance"
                                else f"score (mean damage taken to the act-{args.target_act} boss)",
                                "cleared" if args.question == "clearance" else "arrivals",
                                "mean rooms", "died in" if args.question == "clearance" else "note"])

    asked = (f"can a real body clear act {args.target_act}?" if args.question == "clearance"
             else f"what does the act-{args.target_act} boss cost to reach at 9999 hp?")
    print(f"training in {out}  ({asked} "
          f"maps {'v0.0.0' if args.legacy else 'v0.0.1'}, "
          f"{'through the game' if args.godot else 'through the console runner'})")
    print(f"  {args.generations} generations × {args.population} runners × {len(seeds)} seeds "
          f"= {args.generations * args.population * len(seeds)} runs, {args.jobs} at a time")
    for generation in range(args.generations):
        gen_dir = out / f"gen-{generation:02d}"
        gen_dir.mkdir(exist_ok=True)
        started = time.time()
        table = evaluate(population, seeds, gen_dir, args.jobs, args.timeout, health, args.target_act,
                         maps, ui=args.ui, godot=args.godot, question=args.question)
        with board.open("a", newline="") as f:
            writer = csv.writer(f)
            for row in table:
                writer.writerow([generation, row["policy"]["Name"], row["score"], row["arrivals"],
                                 row["rooms"], row["note"]])
        print(f"\ngeneration {generation} ({time.time() - started:.0f}s)")
        for row in table:
            print(f"  {row['policy']['Name']:<10} score {row['score']:>10}  "
                  f"{'cleared' if args.question == 'clearance' else 'arrived'} {row['arrivals']}"
                  f"  rooms {row['rooms']:>5}  {row['note']}")
        (out / "best-policy.json").write_text(json.dumps(table[0]["policy"], indent=2))
        (gen_dir / "ranking.json").write_text(json.dumps(
            [{k: v for k, v in row.items()} for row in table], indent=2))

        parents = [row["policy"] for row in table[:args.survivors]]
        population = [dict(p, Name=f"g{generation + 1}-p{i}") for i, p in enumerate(parents)]
        while len(population) < args.population:
            parent = parents[rng.randrange(len(parents))]
            population.append(mutate(rng, parent, f"g{generation + 1}-p{len(population)}", args.sigma))

    best = json.loads((out / "best-policy.json").read_text())
    print(f"\nbest runner: {json.dumps(best, indent=2)}")
    print(f"\nreplay it:  godot --headless -- --sim --sim-seed {seeds[0]} --sim-immortal "
          f"{' '.join(maps)}{' ' if maps else ''}--sim-policy {out / 'best-policy.json'}")
    print(f"or faster:  {BOT_BIN} --game content/game.roguedeck.json --runs 1 "
          f"--seed-from {seeds[0]} --immortal --policy {out / 'best-policy.json'}")


if __name__ == "__main__":
    main()
