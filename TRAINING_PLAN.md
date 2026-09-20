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

⚠⚠ **CORRECTED 2026-09-20 AT T1 — THIS SECTION SAID EIGHT AND THE ANSWER IS TWO.** The claim was that
`WDamage`, `WBlock`, `WStatus`, `WDraw`, `WResource`, `WCost`, `EndTurnBelow` and `TargetLowestHp` are all
dead for a champion. Only the last two are. The first six decide not just what is PLAYED but what is TAKEN:
`BotMind.ScoreOffer` scores every reward, every card removal and every shop slot with them, `Weighted` scores
every relic, and `DoorHealth`/`DoorGold` are exchange rates against exactly that card score (B2). The
champion forks the fight — it builds its DECK with these numbers.

Measured rather than read, fair champion, seeds 1–3, 70 health, `--stop-after-act 1`:

| changed | what happened |
|---|---|
| `EndTurnBelow` −0.815 → 2.5 and `TargetLowestHp` 0.54 → 0.0 | **all three runs identical, line for line** |
| `WDamage` 1.387 → −2.0 and `WBlock` 0.743 → 3.0 | 411 / 670 / 580 lines differ; seed 3 goes from *died in room 13* to *act I cleared, 70/70* |

Both of the two sit behind `if (_options.Champion) return ChampionPlay(combat);` in `BotMind.ChoosePlay`, and
nothing else reads them. Those two are dropped from the gene set under `--champion`; the other six are bred.

⚠ **Had this section been believed, the night would have bred a champion that cannot judge a reward.**

What the policy still decides for a champion, and therefore what is worth breeding:

| gene | what it decides |
|---|---|
| `PathCombat/Elite/Shop/Rest/Event/Treasure` | which door |
| `Foresight` | how many rooms down a door is judged (O2 — measured worth +1.91 rooms *only together with* the path weights) |
| `RestBelow` | when a healing door outranks everything beside it |
| `RewardSkip`, `ShopBuy`, `EventLate` | what is taken and bought |
| `WDamage`, `WBlock`, `WStatus`, `WDraw`, `WResource`, `WCost` | what a card or relic on offer is WORTH — the deck the champion ends up fighting with |
| `Aggression` | the champion's single fighting knob |

⚠ **`DoorHealth` and `DoorGold` are in `BotPolicy` but have never been in the trainer's `GENES`** — this
table listed them and the trainer never bred them. Measured before deciding, same method as above: `DoorHealth`
3 → 0.2 and `DoorGold` 1 → 5 over seeds 1–3, **38 door choices between them including rest sites and three
shop visits — all three runs identical, line for line.** So there is no evidence they are worth a gene, and
two more genes in an 8-runner population is a known cost. Left out, and this is why. ⚠ Unlike the two genes
above, this is *no effect on these seeds*, not *nothing reads them*: `BotMind` line 850 does.

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

### T0 — bound the tail — ✔ **DONE 2026-09-20** (`Core` + `tools/train.py`)

`--stop-after-act N` ends the run at the gates of act N+1. It stops the walk BEFORE its next answer, by the
same throw every other guard uses, so both seats end in the same state; `Acts` moves up to the act whose
gates it stands at, which is what makes `cleared=N` true; and the clearance line carries `calledOff=asked`
so that nothing downstream has to guess at the wording of `result=Ongoing`.

**Gate — passed.** Four immortal seeds of the shipped game, `--stop-after-act 2` against the same seeds
played out: identical act lines, identical `actBossDamage`/`actBossHp` for acts I–II, identical per-act
receipts, `cleared=2 rooms=47`, and the bounded run's log **byte-equal** to the unbounded one for the first
1588–1961 lines — up to the line where it stops. The golden set is untouched through **both** seats
(`--console` and `--console --replay`, 15/15). Both seats also produce the same bounded run. Suites green:
Core 1488 · Scenario 769 · Sandbox 478 · Run 834.

**What it costs:** those four immortal runs, 29.8 s → 9.8 s (÷3.0). The saving on the champion is the tail,
which is where the risk was; T2 is what measures it.

⚠⚠ **Two defects the gate found, both fixed here:**

1. **The trainer would have scored every good candidate as a standstill.** A called-off run reports
   `result=Ongoing`, and `_stood()` reads exactly that — so the stall penalty (0 rooms, 0 health) would have
   landed on precisely the runners that CLEARED the target act, wiping out the rooms-and-health tie-break
   that carries the whole gradient once several candidates get through. `tools/train.py` now reads
   `calledOff=`, and `--stop-after-act` is threaded through to the runner (it refuses `--godot`, which has
   no such flag). Smoke: 1 generation, 2 runners, `--target-act 1 --stop-after-act 1` — the clearing runner
   scores −10229 on 23 rooms, the genuinely stalled one still scores 20000 as `STOOD STILL in 2 of 2`.
2. **The damage receipt was losing the closing exchange of every fight but the run's last.** The ledger is
   read on every ANSWER, and after the killing blow there are no more answers in that fight — only `Finish`
   looked again, and only at the fight the run ended in. It showed up as the bounded run naming 23 MORE
   points in act II than the unbounded one. A fight is now read once more as it ends: over the four immortal
   seeds `unnamed` falls from **1685 of 34758 to 533** — two thirds of the hole §5 leaves open.

⚠ **And one finding NOT fixed, because it is nobody's bug in this step:** under the REPLAY seat the damage
ledger reads almost nothing — seed 1 names 17 of 2054, seed 4 names 1 of 1900, `blocked=0` throughout. The
receipt is a statement about the DIRECT seat only. Every balance sweep this project has published used the
direct seat (the console runner's default), so nothing measured is wrong; but a receipt taken out of Godot's
`--sim`, which drives through the replay model, is empty. The two report lines the golden set diffs agree
between the seats — it is only the ledger that does not.

### T1 — teach the trainer the fighter — ✔ **DONE 2026-09-20** (`tools/train.py`)

`--horizon`, `--beam` and `--samples` are stamped into **every** policy that is written — the candidates a
generation plays and `best-policy.json` — by one `written()` helper at the moment of saving, and nowhere
else, so no `mutate` can touch them and no `--resume` can drag old values along. They are not in `GENES`.
They go into non-champion policies too: a policy that cannot name the fighter it was bred against is
comparable with nothing, which is the same reason every run prints `maps=`.

Dropped from the gene set under `--champion`: **`EndTurnBelow` and `TargetLowestHp`, and those two only** —
see the correction in §2, which is the real finding of this step.

`--resume` now carries over the genes alone, and a gene the resumed file does not have (a champion-bred
policy continued *without* `--champion`) starts from the middle of its range **and says so** instead of
raising a KeyError or inventing a number quietly.

**Gate — passed.** `--champion --horizon 3 --beam 4 --samples 6`: every candidate file and `best-policy.json`
carry `Horizon: 3.0, Beam: 4.0, Samples: 6.0`, carry the six card weights and carry neither dropped gene. The
trainer's header names the fighter, and the runner's own header confirms it played as one — *"champion: 3
turns of lookahead, beam 4, 6 shuffled decks per decision — a fair player: it does not see what it has not
drawn"*. The non-champion path is unchanged: the same two-runner smoke scores −10229 and 20000 as before,
with all 20 genes in the file.

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

- **A gene that does nothing costs nothing — except a night.** ⚠ And a gene wrongly BELIEVED to do nothing
  costs the whole answer: this plan said all eight card weights were dead for a champion, and six of them
  are what it judges every reward, relic, shop slot and door with. Read the branch, then change the gene and
  play the seed — a run that comes back line-for-line identical is the only proof a knob is dead.
- **Standing still must lose.** The trainer already scores a stalled run as zero rooms and zero health; it
  was added because a stall with full health once ranked above an honest death at the same depth.
- **Never grade on the breeding seeds.** The 5× improvement claimed for the last policy held up on 200
  held-out seeds; that is why it is believed.
- **A mean that rises with depth is survivor bias.** True of the health curve and true of any per-room cost
  table — see §4/T4.
- **`unnamed` is 15 %.** The damage receipt cannot yet name a seventh of the health. Measured out so far:
  it is not the opening bell, not in-fight healing, not `SetHealth`, not max-health changes, and it is inside
  the fights (the run's own health does not move at all while a fight runs — `duringFight=0` on every seed).
  Next probe would be per fight rather than per run. ⚠ **T0 found most of it**: the ledger was never reading
  what a fight wrote after its last decision, and closing that took `unnamed` from 1685 of 34758 to 533 on
  the four immortal seeds. The 15 % figure above is from the fair champion at 70 health and has not been
  re-measured since — do that before quoting it again.
