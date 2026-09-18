#!/usr/bin/env python3
"""Welche Seeds bekommt keiner unserer Läufer klein?

Das ist der Bericht, den V-7 wollte — nicht eine Schadenszahl, sondern: *diese 7 von 500 Seeds schafft kein
Runner, den wir haben, und hier ist der Raum, in dem jeder gestorben ist.*

    tools/unbeaten.py --runs 50 --policies ~/Desktop/bnb-balance-training/*/best-policy.json
    tools/unbeaten.py --runs 200 --seed-from 1 --target-act 4 --jobs 6 --policies a.json b.json

Jede Politik spielt JEDEN Seed mit dem Leben, das das Spiel autoriert hat — kein 9999-HP-Körper. Ein Seed
gilt als geschafft, sobald IRGENDEINE Politik ihn durch den Ziel-Akt bringt; der Bericht listet den Rest.

⚠ EIN SEED, DEN KEINER SCHAFFT, IST ZWEIERLEI GLEICHZEITIG: entweder ist der Seed zu hart, oder unsere
Läufer sind zu schwach. Diese Liste beantwortet das nicht — sie sagt, WO man hinschauen muss. Solange der
beste Runner nicht einmal Akt I räumt, ist sie eine Aussage über den Runner.
"""
import argparse, json, re, subprocess, sys, tempfile
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
BOT = REPO.parent / "RogueDeck-Core" / "src" / "RogueDeck.Bot.Cli"
BOT_BIN = BOT / "bin" / "Release" / "net10.0" / "roguedeck-bot"


def play(policy, seed_from, runs, out_dir, jobs, maps, timeout):
    """Ein Prozess, ein Block Seeds — derselbe Läufer, den golden.sh durch beide Fahrer schickt."""
    out_dir.mkdir(parents=True, exist_ok=True)
    args = [str(BOT_BIN), "--game", "content/game.roguedeck.json",
            "--runs", str(runs), "--seed-from", str(seed_from), *maps,
            "--jobs", str(jobs), "--out", str(out_dir)]
    if policy:
        args += ["--policy", str(policy)]
    with open(out_dir / "bot.log", "w") as log:
        try:
            subprocess.run(args, cwd=REPO, stdout=log, stderr=subprocess.STDOUT,
                           timeout=timeout, check=False)
        except subprocess.TimeoutExpired:
            log.write("\n!! der Block wurde abgeschnitten\n")


def name_of(path):
    """Wie eine Politik im Bericht heißt: ihr eigener Name, wenn sie einen trägt."""
    try:
        return json.loads(path.read_text()).get("Name") or path.stem
    except (OSError, ValueError):
        return path.stem


def clearance(out_dir, seed):
    log = out_dir / f"run-{seed:04d}.log"
    if not log.exists():
        return {"cleared": -1, "died": "der Lauf hat nichts berichtet", "rooms": 0}
    line = next((l for l in log.read_text(errors="replace").splitlines()
                 if l.startswith("sim-clearance:")), None)
    if not line:
        return {"cleared": -1, "died": "keine sim-clearance-Zeile — der Lauf ist abgestürzt", "rooms": 0}
    f = dict(re.findall(r"(\w+)=(\S+)", line))
    return {"cleared": int(f.get("cleared", 0)),
            "rooms": int(f.get("rooms", 0)),
            # `at=` steht am Zeilenende und trägt Leerzeichen.
            "died": line.split(" at=", 1)[1].strip() if " at=" in line else ""}


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--runs", type=int, default=50, help="wie viele Seeds")
    ap.add_argument("--seed-from", type=int, default=1)
    ap.add_argument("--jobs", type=int, default=6)
    ap.add_argument("--target-act", type=int, default=4,
                    help="der Akt, der geschafft sein muss (Standard 4 — das Versprechen des Entwurfs)")
    ap.add_argument("--policies", nargs="*", default=[],
                    help="eine oder mehrere gezüchtete Politiken; ohne eine spielt der Würfelspieler")
    ap.add_argument("--legacy", action="store_true", help="gegen die alten Karten (v0.0.0)")
    ap.add_argument("--timeout", type=int, default=7200)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    if not BOT_BIN.exists():
        build = subprocess.run(["dotnet", "build", str(BOT), "-c", "Release", "-v", "q", "--nologo"],
                               cwd=REPO, capture_output=True, text=True)
        if build.returncode != 0:
            print(build.stdout[-2000:]); sys.exit("build failed")

    maps = ["--legacy"] if args.legacy else []
    seeds = list(range(args.seed_from, args.seed_from + args.runs))
    runners = [(name_of(Path(p)), Path(p)) for p in args.policies] or [("random", None)]
    seen = {}
    for i, (name, path) in enumerate(runners):
        seen[name] = seen.get(name, 0) + 1
        if seen[name] > 1:
            # Zwei Züchtungen heißen beide "best-policy" — dann sagt der Ordner, welche gemeint ist.
            runners[i] = (f"{name}-{path.parent.name}", path)
    out = Path(args.out) if args.out else Path(tempfile.mkdtemp(prefix="unbeaten-"))
    out.mkdir(parents=True, exist_ok=True)

    print(f"{len(runners)} Läufer × {len(seeds)} Seeds, echtes Leben, Ziel: Akt {args.target_act} geräumt")
    print(f"  Logs -> {out}")

    at_once = max(1, args.jobs // max(1, min(len(seeds), args.jobs)))
    with ThreadPoolExecutor(max_workers=max(1, len(runners) if at_once > 1 else 1)) as pool:
        list(pool.map(lambda r: play(r[1], seeds[0], len(seeds), out / r[0], args.jobs, maps, args.timeout),
                      runners))

    best = {}
    for name, _ in runners:
        got = 0
        for seed in seeds:
            reading = clearance(out / name, seed)
            got += reading["cleared"] >= args.target_act
            kept = best.get(seed)
            if kept is None or reading["cleared"] > kept["cleared"] or (
                    reading["cleared"] == kept["cleared"] and reading["rooms"] > kept["rooms"]):
                best[seed] = dict(reading, by=name)
        print(f"  {name:<28} durch Akt {args.target_act}: {got}/{len(seeds)}")

    unbeaten = [s for s in seeds if best[s]["cleared"] < args.target_act]
    print(f"\n{len(unbeaten)} von {len(seeds)} Seeds schafft KEIN Läufer durch Akt {args.target_act}.")
    for seed in unbeaten:
        b = best[seed]
        print(f"  seed {seed:<6} weitester Versuch: Akt {b['cleared']} geräumt, {b['rooms']} Räume, "
              f"gestorben in {b['died']}  ({b['by']})")

    report = out / "unbeaten.json"
    report.write_text(json.dumps(
        {"targetAct": args.target_act, "seeds": len(seeds), "unbeaten": unbeaten,
         "best": {str(k): v for k, v in best.items()}}, indent=2, ensure_ascii=False))
    print(f"\n{report}")
    return 0 if not unbeaten else 1


if __name__ == "__main__":
    sys.exit(main())
