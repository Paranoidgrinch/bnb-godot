# Training plan — breed a run layer for the player we now have

**Status:** proposed 2026-09-20, at the end of the V-7 arc (P1–P4 complete, `Core 69fa104`). Everything under
"What we have" was measured today on this machine (12 cores) against `content/game.roguedeck.json` with the
policy `g29-p0` from `~/Desktop/bnb-balance-training/20260919-155305/`.

---

## 1. What we have, measured

A **fair** deep player: above one sample the fight is forked several times with its own draw pile shuffled
differently in each, and an opening is scored by what it is worth on average across those worlds (C4). It no
longer sees what it has not drawn, so its numbers are results rather than upper bounds.

| 50 seeds, 70 health, fair champion (Horizon 3, Beam 4, Samples 6) | |
|---|---|
| runs | 50, **all defeats** |
| deepest act reached | act I ×34 · act II ×9 · act III ×4 · **act IV ×3** |
| acts cleared | I: 16/50 (32 %) · II: 7/50 · III: 3/50 · IV: 0 |
| rooms | 1831 total, **36.6 per run** (the bred h1 policy managed 16.5 on held-out seeds) |
| cost | median **470 s** of CPU per run; act-I death 423 s; the deepest run 4571 s |

And the gate that closed C4 — 67 routes through act I, seeds 1–8:

| player | cleared | solvable seeds |
|---|---|---|
| horizon 1 | 5 / 67 | 2 / 8 |
| horizon 3, prophet | 9 / 67 | 5 / 8 |
| horizon 3 + 6 decks, fair | **9 / 67** | **5 / 8** |

**Nobody has ever won a run.** The best this player does is clear three acts.

---

## 2. What training can and cannot move

⚠⚠ **The champion does not read the card weights.** `WDamage`, `WBlock`, `WStatus`, `WDraw`, `WResource`,
`WCost`, `EndTurnBelow`, `TargetLowestHp` decide a play only for the *policy* runner. The champion forks the
fight and looks, and the only knob it reads is `Aggression`. Breeding those eight genes against a champion
breeds noise, and that is worth knowing before a night is spent on it.

What the policy still decides for a champion, and therefore what is worth breeding:

| gene | what it decides |
|---|---|
| `PathCombat/Elite/Shop/Rest/Event/Treasure` | which door |
| `Foresight` | how many rooms down a door is judged (O2 — measured worth +1.91 rooms *only together with* the path weights) |
| `RestBelow` | when a healing door outranks everything beside it |
| `RewardSkip`, `ShopBuy`, `EventLate`, `DoorHealth`, `DoorGold` | what is taken and bought |
| `Aggression` | the champion's single fighting knob |

**The hypothesis this training exists to test, stated so it can fail:** the current policy was bred against a
weak fighter, and a strong fighter should want a different run. Specifically — **`PathElite` should rise**
(an elite this player can survive is a reward it can afford) and **`RestBelow` should fall** (a body that
loses 1.1 health to an ordinary fight needs the rest sites less). If neither moves and clearance does not
improve, the run layer was already near its optimum for this content and the answer is "no", which is a
result worth having.

### ⚠⚠ The fighter's knobs must NOT be bred

`Horizon`, `Beam` and `Samples` buy strength with compute. The fitness cannot see compute, so a search that
breeds them will always buy more of them, and the population will drift to whatever the timeout allows —
optimising the clock, not the play. **They are fixed for the whole run and written into every candidate**,
and a later experiment may change them deliberately. (If they are ever bred, the fitness must carry the run's
own seconds, and that is a different design.)

---

## 3. What it costs, from the measured numbers

One generation = population × seeds runs. At a median of 470 s per run on 12 cores:

| population × seeds | runs/gen | wall per generation | 12 generations |
|---|---|---|---|
| 16 × 16 (the old shape) | 256 | 2 h 47 | 33 h |
| 10 × 12 | 120 | 1 h 18 | 15 h |
| **8 × 8** | 64 | **42 min** | **8 h 20 — one night** |
| 8 × 8 at Horizon 2 | 64 | ~21 min | ~4 h |

⚠ **The cost rises as the runner improves.** A run that dies in act I costs 423 s; the run that reached act
IV cost 4571 s. A generation late in the breeding is dearer than a generation early in it, so a night that
fits at generation 1 may not fit at generation 12. **T0 below removes exactly that risk.**

---

## 4. The plan

### T0 — bound the tail (do this first, it pays for itself)

Add `--stop-after-act N`: the run ends when act N is cleared. The clearance fitness asks "did it clear act
N?" and everything the runner does afterwards is paid for and never read. Today the answer costs up to 4571 s
of tail that no gene is judged on.

**Gate:** a run with `--stop-after-act 2` reports the same `cleared`, `rooms` and `hp` for acts I–II as the
same seed run without it, and the golden set is untouched (the flag is off by default).

### T1 — teach the trainer the fighter

`tools/train.py` writes `Horizon`, `Beam` and `Samples` into every candidate from command-line values, and
prints them in its header. They are **not** in `GENES`. The eight card weights are dropped from `GENES` when
`--champion` is given, with a line in the log saying why (§2).

**Gate:** the written policy files contain the three fields; a breeding run's header states the fighter.

### T2 — breed the run layer overnight

```
tools/train.py --champion --question clearance --target-act 2 \
    --horizon 3 --beam 4 --samples 6 \
    --population 8 --seeds 8 --seed-from 1000 --generations 12 \
    --health 70 --stop-after-act 2 --jobs 12
```

Breeding seeds **1000–1007**, held-out seeds **1–50** (the sweep above is the baseline for exactly those).
`--target-act 2` because 16 of 50 runs clear act I and 7 clear act II: a question this population can both
fail and pass is the only one that carries a gradient. Asking for act IV would score 47 of 50 candidates
identically at zero.

**Gate:** the bred policy beats `g29-p0` on the held-out seeds 1–50, paired, on acts cleared. Anything else
is a null result and is reported as one.

### T3 — judge it honestly

Re-run the 50-seed sweep with the winner and compare against §1 line for line: acts cleared, rooms per run,
and the ranks the map oracle gives its doors (`--oracle`). Report the paired difference and its t, not the
averages alone.

⚠ **The old policy must be re-measured on the same day with the same build**, not compared against the table
in §1, if anything in the player changed in between.

### T4 — re-draw the balance map with the winner

The balance map is a statement about a player, and this arc has already been caught by that once: with the
weak runner the act-I boss looked like the cheapest named fight in the game, and with the fair champion the
curve is monotone (combat 1.1 < multi-combat 10.3 < elite 15.6 < boss 18.0). Whatever the content decision
is, it should be taken against the strongest player we have.

**What survives both players and is therefore content, not player:** elites and multi-combats are where the
health goes, `city_elite_appeal_01` and `_03` kill the most runs, and act II has a multi-combat median
(17.0) **above its boss** (11.7).

---

## 5. Traps already paid for

- **A gene that does nothing costs nothing — except a night.** The card weights are the case in point.
- **Standing still must lose.** The trainer already scores a stalled run as zero rooms and zero health; it
  was added because a stall with full health once ranked above an honest death at the same depth.
- **Never grade on the breeding seeds.** The 5× improvement claimed for the last policy held up on 200
  held-out seeds; that is why it is believed.
- **A mean that rises with depth is survivor bias.** True of the health curve and true of any per-room cost
  table — see §4/T4.
- **`unnamed` is 15 %.** The damage receipt cannot yet name a seventh of the health. Measured out so far:
  it is not the opening bell, not in-fight healing, not `SetHealth`, not max-health changes, and it is inside
  the fights (the run's own health does not move at all while a fight runs — `duringFight=0` on every seed).
  Next probe would be per fight rather than per run.
