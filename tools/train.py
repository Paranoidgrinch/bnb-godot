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
LAST_ACT = 4


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


def play(policy_file, seed, log_file, timeout, health, target_act):
    with open(log_file, "w") as log:
        try:
            subprocess.run(
                ["godot", "--headless", "--", "--sim", "--sim-seed", str(seed),
                 *health, "--sim-policy", str(policy_file)],
                cwd=REPO, stdout=log, stderr=subprocess.STDOUT, timeout=timeout, check=False)
        except subprocess.TimeoutExpired:
            log.write("\n!! the run was cut off by the trainer's timeout\n")
    text = Path(log_file).read_text(errors="replace")
    line = next((l for l in text.splitlines() if l.startswith("sim-fitness:")), None)
    if not line:
        return {"reached": False, "damage": UNREACHED, "rooms": 0, "note": "no fitness line — the run died"}
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
            "note": "" if reached else f"never reached the act-{target_act} boss"}


def evaluate(policies, seeds, gen_dir, jobs, timeout, health, target_act):
    """Every policy over every seed, in parallel; a policy's score is its mean hp lost."""
    work = []
    for policy in policies:
        path = gen_dir / f"{policy['Name']}.json"
        path.write_text(json.dumps(policy, indent=2))
        for seed in seeds:
            work.append((policy, path, seed, gen_dir / f"{policy['Name']}-seed{seed}.log"))
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        results = list(pool.map(lambda w: play(w[1], w[2], w[3], timeout, health, target_act), work))
    scored = {}
    for (policy, _, seed, _), result in zip(work, results):
        scored.setdefault(policy["Name"], []).append(result)
    table = []
    for policy in policies:
        runs = scored[policy["Name"]]
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
    ap.add_argument("--timeout", type=int, default=2400, help="seconds a single run may take")
    ap.add_argument("--sigma", type=float, default=0.18, help="mutation size, as a share of each gene's range")
    ap.add_argument("--out", default=None)
    ap.add_argument("--health", type=int, default=0,
                    help="a body of this size instead of the immortal 9999 — only for shaking the trainer out")
    ap.add_argument("--target-act", type=int, default=LAST_ACT,
                    help="which act's boss the runners are measured to (default: the game's last act)")
    ap.add_argument("--resume", default=None, help="a previous training folder to keep breeding from")
    args = ap.parse_args()

    desktop = Path.home() / ("Schreibtisch" if (Path.home() / "Schreibtisch").is_dir() else "Desktop")
    out = Path(args.out) if args.out else desktop / "bnb-balance-training" / time.strftime("%Y%m%d-%H%M%S")
    out.mkdir(parents=True, exist_ok=True)
    shutil.copy(REPO / "tools" / "training-README.md", out.parent / "ANLEITUNG.md")

    build = subprocess.run(["dotnet", "build", "-v", "q", "--nologo"], cwd=REPO, capture_output=True, text=True)
    if build.returncode != 0:
        print(build.stdout[-2000:]); sys.exit("build failed")

    health = ["--sim-health", str(args.health)] if args.health else ["--sim-immortal"]
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
                                f"score (mean damage taken to the act-{args.target_act} boss)",
                                "arrivals", "mean rooms", "note"])

    print(f"training in {out}  (measured to the act-{args.target_act} boss)")
    print(f"  {args.generations} generations × {args.population} runners × {len(seeds)} seeds "
          f"= {args.generations * args.population * len(seeds)} runs, {args.jobs} at a time")
    for generation in range(args.generations):
        gen_dir = out / f"gen-{generation:02d}"
        gen_dir.mkdir(exist_ok=True)
        started = time.time()
        table = evaluate(population, seeds, gen_dir, args.jobs, args.timeout, health, args.target_act)
        with board.open("a", newline="") as f:
            writer = csv.writer(f)
            for row in table:
                writer.writerow([generation, row["policy"]["Name"], row["score"], row["arrivals"],
                                 row["rooms"], row["note"]])
        print(f"\ngeneration {generation} ({time.time() - started:.0f}s)")
        for row in table:
            print(f"  {row['policy']['Name']:<10} score {row['score']:>10}  arrived {row['arrivals']}"
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
          f"--sim-policy {out / 'best-policy.json'}")


if __name__ == "__main__":
    main()
