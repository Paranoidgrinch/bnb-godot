# V-7 Runner Plan — make the runners fast, then make them play well

**Status:** proposed 2026-09-18, before V-7's stress test. Everything under "What was measured" was measured
today on this machine (12 cores, 15 GB, Godot 4.7.stable.mono, .NET 10) against `Core 38d8447` /
`bnb-content 613409c` / `bnb-godot 5d63552`. Projections are marked as such.

**Why now.** V-7 asks a question the current runners cannot afford to answer: *is every seed beatable through
Act IV by a player who plays perfectly?* That is not one run — it is hundreds of seeds × several candidate
players, repeated every time the content moves. At today's price (one whole-game run = **5 m 24 s**) a
1000-seed sweep is 3 days of wall clock. At the price this plan reaches (**~15 s**) it is 20 minutes.

**What this plan may not cost.** Not one rule, number, pool or fight may change. Every step below is gated on
a golden run set reproducing **byte-identically** — the numbers in §0 are the anchor. Where a step trades away
coverage (the screen, the replay path), it names the replacement probe that keeps that coverage.

---

## 0. The anchor: what a run does today

One immortal random run, `godot --headless -- --sim --sim-seed 1 --sim-immortal`:

```
Victory   acts=5  rooms=110  fights=67  hp=7323/9996  problems=1
damageTaken=8300  healed=6277
actBossDamage=1:790,2:1939,3:3269,4:6280,5:8300
actBossHp=1:9623,2:8474,3:9620,4:9996,5:7976
```

Every phase below reproduced this line unchanged in the prototypes. **This is the gate.** Any change that
moves a single number in it is a change to the game and must be argued as one, not slipped in as speed.

**Exactly one intended change to the anchor is planned:** `problems=1` → `problems=0` at **R0b**, when Enlil's
decree stops throwing. Nothing else in the line may move — the damage totals and the room count must survive
that fix untouched, which is itself the check that the fix clamped rather than swallowed.

⚠ The anchor was recorded with the map generator this machine's `settings.cfg` happens to name. **R0a** pins
that down before R0c freezes anything; until then the anchor is a fact about this machine, not about the game.

---

## 1. What was measured

### 1.1 Wall clock, one run at a time

| what | time | note |
|---|---|---|
| `godot --headless --quit` | **1.73 s** | Godot boot + autoloads alone |
| `--sim --sim-steps 1` (nothing played) | **5.15 s** | **the fixed cost of every run**: boot + 11.31 MB document parse + Glossary + map generation + JIT |
| `--sim --sim-health 400` (dies in act I, 14 rooms) | **27.3 s** wall / 22.7 s loop | |
| … same, screen rebuild off | **13.5 s** wall / 9.1 s loop | same result, same rooms, same fights |
| `--sim --sim-immortal` (Victory, 110 rooms, 67 fights) | **324.5 s** wall / 319.6 s loop, 882 MB RSS | |
| … same, screen rebuild off | **102.0 s** wall / 97.7 s loop, 481 MB RSS | **identical outcome** |
| … same, + compiled-scenario cache (prototype) | **54.4 s** wall / 49.8 s loop | **identical outcome** |
| `Converter --playtest 1` (pure .NET, **Debug**) | **203.0 s** | Victory, 110 rooms, 528 steps |
| `Converter --playtest 1` (pure .NET, **Release**) | **147.0 s** | same result |

### 1.1b ⚠⚠ The screen the runner draws is not being checked

Measured by breaking it on purpose: a `Rebuild()` that throws on **every** redraw (378 throws in one run)
produced

```
sim-result: seed=1 result=Defeat acts=1 rooms=14 fights=9 problems=0 error=none
            stopped because the run finished                              EXIT=0
```

The exception goes to stderr, Godot swallows it inside the deferred signal call, and neither the simulator nor
`simulate.sh`'s summary (which greps for `!! PROBLEM|CRASH|error=|stopped because`) looks at it. The batch's
exit code is **0**.

**So the ÷3.3 the runner pays for drawing buys no detection whatsoever.** Separating the frontend check is
therefore not a trade — it is the first time the runner would test the frontend at all.

⚠ *Re-measured at R2a: the true price of drawing is **÷5.9**, not ÷3.3 — the smaller figure came from a
prototype measured before R1, with 60 s of compiling sitting in both columns.*

### 1.2 Where the engine time goes

Instrumented counters over one immortal run (~4818 answers), `--sim-noui`, total loop 110.0 s:

```
replays=4818  checkpoints=949  refused=0  entriesWalked=11493  compiles=4586
makeRunMs=755  runnerMs=108937  snapshotMs=34  captureMs=91
buildPlaythroughMs=575  compileMs=60697  restoreCombatMs=925  driveRestMs=1731
```

Read that line carefully, because it overturns the obvious suspicion:

- **`ScenarioBlueprint.Compile()` ran 4586 times and cost 60.7 s — 55 % of the whole run.**
- The replay machinery everyone would blame is *cheap*: the run snapshot costs 34 **ms** in total, the combat
  capture 91 ms, the combat restore 925 ms. The turn-boundary checkpoint (`InteractiveRunSession.TryCheckpoint`)
  is doing its job: 11493 entries walked over 4818 replays is **2.39 entries per answer**, not a growing tail.
- What is left after the compile (≈45 s) is the engine actually playing the game.

**Why Compile() is the whole cost.** `Encounters.Build` (`Encounters.cs:172-180`) copies the **entire library**
into every fight's blueprint, and `Compile()` re-registers all of it into a fresh registry. The shipped
document holds **1155 statuses + 1046 enemy actions + 363 cards = 2564 definitions**. 4586 compiles × 2564
definitions ≈ **11.8 million definition compiles per run**, to play 67 fights.

**Prototype, to prove the diagnosis** (memoise the compiled scenario for the life of a fight):

```
compiles=4586 → 83      compileMs=60697 → 1381      loop 110.0 s → 49.8 s
```

— and the anchor line in §0 came back identical, field for field.

**This is not only a runner cost.** The real game runs the same replay: **every click a player makes in Act V
recompiles 2564 definitions.** Fixing this is a player-facing latency win as much as a runner win.

### 1.3 What the batch scripts cost today

| batch | runs | today |
|---|---|---|
| `tools/simulate.sh 100 --immortal --jobs 8` | 100 | **~68 min** |
| `tools/train.py` (defaults: 5 gen × 8 pop × 2 seeds, jobs 4) | 80 | **~1 h 50 m** |
| `tools/train.py --generations 10 --population 12 --seeds 3 --jobs 8` | 360 | **~4 h** |
| `Converter --playtest 8` (**sequential**, Debug) | 8 | **~27 min** |
| `--smoke-marathon` | 1 | **~400 s** |

---

## 2. The screws, ranked by what they are worth

| # | screw | worth | where |
|---|---|---|---|
| **S1** | `Compile()` per answer, whole library every time | **÷2.2** (measured) | `ScenarioBlueprint.Compile`, `Encounters.Build`, `InteractiveCombatDriver.Drive` |
| **S2** | the simulator draws the screen it never looks at | **÷3.3** (measured) | `SessionScreen.cs:120` — `StateChanged += Rebuild` is subscribed under `--sim` too |
| **S3** | everything runs Debug | **÷1.38** (measured) | `dotnet build` with no `-c Release`; Godot headless loads the Debug assembly |
| **S4** | every answer re-executes 2.39 answers | **÷~2.4** (projected) | the replay model — correct for a UI, pure waste for a bot |
| **S5** | 5.15 s of fixed cost per run, one process per run | **5.15 s × N** | `simulate.sh` / `train.py` fork Godot per run |
| **S6** | 4 jobs on 12 cores; `--playtest` fully sequential | **÷2–3** on batches | `simulate.sh` default `jobs=4`, `train.py` default `--jobs 4`, `Program.cs:238` `for` loop |
| **S7** | a generation barrier + a 2400 s per-run timeout | tail latency | `train.py evaluate()` waits for its slowest run before breeding |
| **S8** | two runners that are drifting apart | correctness | `scripts/RunSimulator.cs` (Godot, policy-capable) vs `Converter/Playtest/RunWalker.cs` (pure .NET, no policy) |
| **S10** | the simulator's map comes from a file outside the repo | reproducibility | `--sim` inherits `RunPreferences.MapGenerator` → `user://settings.cfg`. `--playtest` names v0.0.1 outright, and the smoke probes do too (`Boot.cs:164`). On this machine the file says `v0.0.1`, so they agree **by accident**, not by contract |
| **S9** | `saveEvery: 5` round-trips a save every 5 answers | small | `RunWalker.Walk` — real coverage, but it belongs in its own probe, not in every balance walk |

**The screen rebuild (S2), in detail**, because it is the one that looks harmless: `Rebuild()` frees every
child of `_main`, `_combatRoot` and `_sidebar`, rebuilds the whole combat scene, re-renders the sidebar,
re-joins 60 log lines, asks the music director, and runs `Archive.Observe` (walking the deck and the shelf) —
**once per answer, ~4800 times per run**, with nobody watching. It also doubles peak memory (882 MB vs 481 MB),
which is what caps the job count.

---

## 3. What is lost if we just switch things off — and what replaces it

The two fast paths look like they drop coverage. Measurement says only one of them actually does.

1. **The screen. Nothing is lost, because nothing was being checked** (§1.1b): a screen that throws every
   single time still finishes the run clean, with exit code 0. → **What goes in instead** (the user's call,
   2026-09-18: both halves):
   - **exceptions from `Rebuild()` are caught and fail the run** — a `problems++`, a `!! PROBLEM screen …`
     line, and a non-zero exit, so the batch summary names it like any other finding;
   - **a new `--smoke-screens` probe** that builds every *kind* of screen state once, systematically, and
     reports which one does not build (§4, R2b).
   - the balance runner never draws; a named share of the batch (`--ui N`, default 5 in `simulate.sh`, 1 in
     `golden.sh`, 0 in `train.py`) still does, and now actually fails when the screen does.
2. **The replay path. This one is real.** The replay restores the run from a snapshot ~950 times per run, and
   that is exactly where the save-and-quit bug (`2dee963`) lived. → **Replacement:** a `--sim-resume` runner
   that checkpoints *every answer* through the replay path, over a handful of seeds per batch — more
   save/restore pressure than today, aimed where it is being looked for rather than smeared thinly over every
   balance run.

## 4. The plan

Each phase ends at a gate. The gate is always: **suites green + the §0 anchor line identical.**

### R0a — Every runner names its map, out loud ✔ **DONE** *(bnb-godot + bnb-content)*
`--sim` must stop inheriting `RunPreferences.MapGenerator`, i.e. a machine-local `user://settings.cfg`
(**S10**). It gets exactly what the smoke probes already have (`Boot.cs:164`):
- **v0.0.1 (`MapGenerators.Strategic`) is what a runner walks**, named explicitly at `StartNewRun`;
- `--legacy` still walks v0.0.0, because old saves resume on it and it stays offered in the new-run dialog;
- `--playtest` already names v0.0.1 — its `--generator` flag stays, its default is confirmed, not changed;
- every report line (`sim-result:`, `sim-fitness:`, the playtest header) states the generator, so no report can
  be read against the wrong act.
- `--legacy` is threaded through `simulate.sh` and `train.py` too, or the flag would be unreachable from the
  batch tools that are the only things that ever run a hundred of these.

⚠ This is first for a reason: a golden set whose map depends on an untracked file is not a golden set.

**Proved, not assumed:** with `settings.cfg` flipped to `v0.0.0` by hand, the runner still reported
`maps=v0.0.1`, and `--legacy` still reached `v0.0.0`. **Gate passed:** the full anchor run came back identical
field for field (`damageTaken=8300 healed=6277 actBossDamage=1:790,2:1939,3:3269,4:6280,5:8300`, 110 rooms,
67 fights, `hp=7323/9996`), with the new `maps=` field added to every report line.

### R0b — an overfilled pool may be spent from ✔ **DONE** *(RogueDeck-Core)*

⚠ **The diagnosis in the first draft of this plan was wrong, and the wrong name cost the most time.** It read
`contempt_finding` + *NO WALL SHALL RISE ABOVE THIRTY* and concluded the block clamp threw. The card has
nothing to do with it, and neither does Block.

**What actually happens.** Under Enlil's *WHAT IS UNSPENT SHALL PASS INTO TOMORROW*
(`CombatRule.UnspentResourceCarries`), `ResourceEffects.cs:96` refills energy to `max + whatever was left`
with `allowExceedingMax: true` — deliberately, correctly, e.g. **6 over a ceiling of 3**. Then `PayCosts`
(`CardPlay.cs:511`) writes the pool back through the **ordinary** setter: `SetCurrent(6 - 1)` → 5 is still
above 3 → **throw**. `contempt_finding` was merely the first card played on such a turn; any card would have
done it, which is exactly why it read as a card fault.

**The fix**, one line in `ValuePoolState.SetCurrent` (`CombatPrimitives.cs`): the guard refuses a write that
*raises* the value above the ceiling — `value > Max && value > Current`. Lowering an already-overfilled pool
is not creating an overfill, it is paying one down toward the ceiling the guard protects. Growing an overfill
is still refused, filling past the ceiling is still refused, and `allowExceedingMax` keeps its meaning for the
refill that legitimately raises.

⚠⚠ **Both existing tests for this decree — `CombatDecreeTests.What_is_unspent_passes_into_tomorrow` and
`ActFiveBossEnlilTests.What_is_unspent_passes_into_tomorrow` — stop exactly one step short.** They prove the
pool may be *filled* past its ceiling and never ask whether it may then be *spent*. That is the whole bug, and
it is the shape to watch for: a decree has two halves, and testing the half that is easy to observe leaves the
half the player actually uses unproven.

**Built:** the guard; `ValuePoolCeilingTests` (6 tests naming the ceiling's contract both ways); the missing
half of each of the two decree tests.
**Measured, not assumed:** with the fix reverted, exactly **2 fail** — both the new "may be spent" tests;
with it, 0. **Gate:** Core **1482 / 755 / 827 / 387** green; the anchor reports `problems=0`.

### R0c — The golden run set ✔ **DONE**
Freeze the instrument before moving it. `tools/golden.sh` (+ `tools/golden-README.md`, + the recording in
`tools/golden-runs.txt`, which is the contract and lives in git).

**The set:** 12 immortal runs (seeds 1–12) that walk all five acts, plus 3 on a mortal body (101–103) that
die in act I — the defeat path, the run-end and the screens a winner never sees, for about a tenth of the
cost of one immortal run. ⚠ The seeds are **not chosen for their outcomes**: a set of runs that all win could
not notice runs starting to lose.

**What is compared:** the two report lines per run — result, acts, rooms, fights, hp, damage taken and healed,
the per-act damage table, `problems`, `error`, and why the run stopped.

⚠ **`seconds=` is stripped before the diff.** The clock is what this whole arc exists to change; a gate that
failed when the runner got faster would be a gate against its own purpose. The timings are kept at the foot
of the file so the speed-up stays visible, and never compared.

⚠⚠ **A run that reports nothing FAILS.** A crashed process, a missing fitness line, a walk that never ended —
each is written in as the absence it is (`THE RUN PRODUCED NO RESULT LINE`) and fails against a recording that
holds a result. Silence must never read as agreement. (D7's lesson, where three screenshots were the same
defeat screen and all three reported success.)

**All three behaviours were proved, not assumed:** unchanged → pass, exit 0; one number tampered with → the
diff, exit 1; the runs killed before they could report → failure naming the absence, exit 1.

**Re-record only when a change to the GAME is intended**, and say in the commit which lines moved and why. A
change meant only to make the runner faster must never need one.

### R1 — Compile once per fight, not once per answer *(RogueDeck-Core)* ✔ **DONE**
`Compile()` ran **4586 times per run**, and each time built the game's whole authored library — 2564
definitions — into a fresh registry and re-validated every effect program in it. The library is the same in
every fight; only the roster, the projected deck and what the run adds on top differ.

**What was built.** `CompiledCombatLibrary` (RogueDeck.Scenario) compiles the authored half once.
`ScenarioBlueprint.Library` names it. `CombatDefinitionRegistryBuilder` gained a constructor that starts from
an already-built registry. `EncounterCatalog` holds one library per per-turn draw count and hands it to every
fight it builds.

⚠ **There is no cache key.** The plan's one dangerous idea — a fingerprint of everything `ApplyRunProjection`
reads — was not needed and was not built. Nothing run-dependent is shared: no deck, no relic, no hero, no HP.
What is shared is the AUTHORED content, which is the same in every fight of the game by construction. The
danger this step was supposed to carry does not exist in what was built.

**The first attempt was fast in the wrong place.** It copied the base registry into the new builder's mutable
dictionaries — still O(library) per fight, just with a cheaper constant. Golden set: 5032 s → 4188 s, and
`Compile()` still expensive. The second attempt REFERENCES the base and lets `Build()` add its handful of
deltas onto the immutable collections it already holds. That is what actually removed the cost.

**A real fault the new tests caught.** Once the base was referenced rather than copied, build-time validation
only saw this builder's own registrations — so a card or trigger belonging to one fight was judged against an
empty library and rejected for naming a status or a damage handler that was right there. Every bnb fight
would have failed to compile. Fixed by `KnowsStatus` / `KnowsCard` / `KnowsEffectRequestHandler`, which ask
the base too. It was `A_fight_may_bring_content_of_its_own_on_top_of_a_library` that found it.

**Measured, with the instrument R0c froze:**
- `Compile()`: **~60 s → 0.1 s** over the 4586 calls of one run (temporary meter, removed again).
- One run alone, seed 1: **286.3 s → 228.2 s** (÷1.25).
- The golden set: **5032 s → 4015 s** run-seconds.

⚠ **The ÷2.2 this section used to promise was wrong, and the mistake is worth keeping.** It came from a
prototype measured against a **110-second** run, not against the real anchor. The ABSOLUTE saving matches the
audit exactly — about 60 seconds, which is what `Compile()` cost — but as a share of a 286-second run that is
21 %, not 55 %. **Every other ratio in §2 and §4 was measured the same way and should be re-checked against
the 228 s anchor before it is believed.**

**Gate:** Core 1488 (+6) · Scenario 762 (+7) · Run 827 · Sandbox 387 · bnb-content 1527 · `GOLDEN OK`, all
15 runs identical field for field.

**Bonus, as predicted:** this is also the fix for player-facing click latency — the real game recompiled the
whole library on every click too.


### R2a — The runner stops drawing, and a screen fault becomes a finding ✔ **DONE** *(bnb-godot)* — **÷5.9**
- `SessionScreen._Ready` subscribes `Rebuild` only when someone is watching: a human always, every `--smoke*`
  probe always (that is what those probes are *for*), and a `--sim` run only with `--sim-ui`.
- ⚠ **First, the part that is a bug fix and not a speed-up:** `Rebuild()` is now a wrapper. It catches, counts
  the fault in `_screenFaults`, prints `!! PROBLEM screen at <where>: <exception>`, and folds the count into
  the run's `problems` — which is what makes the process exit non-zero. For a human it also toasts, because a
  half-drawn screen that says so beats the game vanishing. The body moved to `RebuildScreen()` untouched.
- `simulate.sh` gains `--ui N` (**default 5** — the first five seeds of the batch draw).
- `golden.sh` gains `--ui N` (**default 1**): the first immortal seed draws, so the recorded lines are proved
  to be the same whether the screen is built or not. That equality is part of what the set now states.
- `train.py` gains `--ui N` but **defaults to 0**, deliberately against the first draft of this plan: the
  trainer is a search, its fitness line comes from the session and never from the nodes, and its runs are the
  expensive immortal kind. `simulate.sh` is where the frontend gets walked daily.

**Measured, one run at a time, immortal seed 1** (the §0 anchor run, on `4b66a3e`/R1):

| | wall | loop | RSS |
|---|---|---|---|
| drawing (`--sim-ui`) | 232.8 s | **228.0 s** | 956 MB |
| not drawing (the new default) | 43.0 s | **38.7 s** | 732 MB |

**÷5.9 on the loop, ÷5.4 wall, −224 MB** — and the result line is identical field for field
(`Victory acts=5 rooms=110 fights=67 hp=7323/9996 problems=0`).

⚠ **The ÷3.3 this section promised was too small**, and for the same reason R1's ÷2.2 was too large: it came
from a prototype measured against the pre-R1 runner, where 60 s of compiling sat in *both* columns and flattened
the ratio. With that 60 s gone, what drawing costs stands out at its true size. **This is the second of the
three ratios corrected; only R3's ÷1.38 is still unmeasured against the 228 s anchor.**

**The deliberate-fault experiment of §1.1b, re-run** — `RebuildScreen()` made to throw on every redraw, then
reverted:

```
before R2a:  sim-result: … problems=0   error=none … EXIT=0     (426 throws, all silent)
after  R2a:  sim-result: … problems=423 error=none … EXIT=1
             !! PROBLEM screen at act 1 r0c0 (city_normal_enforcement_05): InvalidOperationException: …
```

Same walk either way — `Defeat acts=1 rooms=14 fights=9` — which is the point: the screen reads the session,
it never answers for it. (423 counted against 426 printed: three redraws fire between the count and the quit.
The count is a floor; every one of them still prints the line `simulate.sh` greps for.)

**Gate passed:** `GOLDEN OK`, all 15 runs identical field for field — total run-seconds **5032 → 4015 (R1) →
1318.9**, with seed 1 still drawing. `--smoke` / `--smoke-run` / `--smoke-full` / `--smoke-tooltips` /
`--smoke-statuses` unchanged and green; `--smoke-marathon` still Victory, 5 acts, 110 rooms (334.9 s — it
draws, by design, so R2a does not touch it).

### R2b — `--smoke-screens`: every kind of screen, built once, on purpose ✔ **DONE** *(bnb-godot)*
The systematic half of the frontend check, separated from the balance question entirely — it does not walk a
run for its content, it walks it for its *screen states*. **Two walks**, because one body cannot see
everything: an unlosable one to the last god (all five acts, every kind of fight, the victory screen) and a
mortal one that dies in Act I (the defeat screen). Both name v0.0.1 explicitly.

**Fifteen rows**, one per branch of `RebuildScreen()` and of the combat screen's bottom band: node fork ·
door · shop · rest · reward pick · entity pick · interlude · combat · combat: ambush · combat: elite ·
combat: boss · combat: card choice · combat: option choice · victory · defeat.

**Four words, and only one of them is good news** — the D7 lesson written into a probe:

| word | what it means |
|---|---|
| `ok` | the screen was built for that state, in that act |
| `FAILED` | it threw while building — the probe exits non-zero |
| `unreached` | the walk could have gone there and did not. **Nothing is claimed.** |
| `no room` / `n/a` | that act's map holds no such room / it cannot happen there |

⚠ The fourth word is not decoration. Act V is a gauntlet of gods — no shop, no campfire, no door, no ordinary
fight — so without it nine cells of Act V read as holes, and nine false holes hide a true one as thoroughly
as silence does. It is answered **from the map the walk actually walked**, never from a belief about the
design. The steering follows from the same thought: at a fork the walk prefers a room this act has not shown
yet, because a fork answered at random walks past the shop three acts running and then reports the shop as
unreached — a finding about the walker, dressed as a finding about the screen.

**What the first run found, before it had proved anything about the screen:**

```
smoke-screens: immortal seed 7 — the run ended: Defeat (hero at 0/9999, in r19c0 against Cornerstone Oath-Stone)
  combat: boss   ok  ok  ok  unreached  unreached
  victory        unreached × 5
```

⚠⚠ **`9999` was called immortal for months and is not.** The greedy walker plays worse than the simulator's
random one, takes more damage over four acts than a whole immortal `--sim` run takes over five, and dies. That
is also the whole of the **pre-existing Act-V boss gap** this step was asked to fix: `--smoke-boss 5` printed
`act=4 boss=NO … error=none` at `hero at 0/9999` and **exited 0** — a sentence that reads like a note beside a
success. The budget was never the problem; it was blamed for two sessions. Fixed in three places:
- `SessionScreen.ProbeBody` (9 999 999) — a body a *screenshot* probe cannot lose. The balance question keeps
  its honest 9999 in `--sim-immortal`, where the golden set measures it. Nothing else shares the number.
- `--smoke-boss N` **exits 1 and prints `!! PROBLEM`** when the walk did not stand in that act's boss fight.
  Not arriving is a failure, not a report.
- `--smoke-crowd` / `--smoke-boss` re-rolls now **name their map generator**. They called `StartNewRun` without
  one, which falls through to `RunPreferences.MapGenerator` — a file on this machine. R0a fixed exactly this
  for `--sim` and the probe *re-rolls* were missed.

**Now:** `--smoke-boss 5` stands in front of **Nisaba, Keeper of the First Tablet**, round 1, with the Divine
Rule Area on screen — the first time that probe has reached what it is named after since the map rework.

**Gate passed.** `58 built, 0 FAILED, 7 unreached, 10 not in that act (of 75 state × act)`. The seven are
named, not smoothed over: reward pick and entity pick in Act V, an in-combat option choice in Act I, and a
defeat in Acts II–V (one mortal walk dies in Act I, which is the only defeat it can show).

**Proved, not assumed** — `RebuildScreen()` made to throw only on the shop screen, then reverted:

```
shop    FAILED  FAILED  FAILED  FAILED  no room
smoke-screens: 54 built, 4 FAILED, 7 unreached, 10 not in that act          EXIT=1
```

Exactly the four acts that have a shop, nothing else disturbed, and the probe fails. The rest of the battery
is green on the new body (`--smoke-shop/-event/-rest/-upgrade/-ambush/-elite/-reward/-crowd/-archive-run`
all STANDING IN IT, `--smoke-marathon` Victory 5 acts 110 rooms), and `GOLDEN OK` — R2b does not touch `--sim`.

### R3 — Release ✔ **DONE** *(all three repos)* — **÷1.3, and a shipping bug came out with it**

⚠⚠ **The engine was never being built in Release. By anyone. Including the game that ships.**

`RogueDeck.Core` and its siblings are `ProjectReference`s from a sibling checkout, and MSBuild builds a
referenced project in the consumer's configuration **only if that project declares the configuration**.
Undeclared, it falls back to Debug and says nothing. Two consumers were hitting that, both invisibly:

| consumer | what it asked for | what it got |
|---|---|---|
| Godot export (`ExportRelease`) | a release build of the game | engine in **Debug** — every shipped binary |
| bnb-content `dotnet build -c Release` | a release converter | engine in **Debug**, copied into `bin/Release` |

The second has its own twist: `dotnet build -c Release` there builds `BnbContent.slnx`, and the engine
projects are **not members of that solution**. A solution assigns configurations only to its own projects, so
the engine came out Debug and was copied into a folder named `Release`, where it read as a Release build to
anyone who checked the folder name rather than the file size. Building the **project**
(`dotnet build Converter/BnbContent.Converter.csproj -c Release`) works; building the solution does not.

**The fix, one new file:** `RogueDeck-Core/Directory.Build.props` declares
`<Configurations>Debug;Release;ExportRelease</Configurations>` and turns the optimizer on for
`ExportRelease` (MSBuild does that by itself only for a configuration literally named `Release` — which is
exactly how `ExportRelease` came to mean *Debug in a different folder*).

**Measured, one run at a time, immortal seed 1** — every row the same result line and the same fitness line:

| build | loop | wall |
|---|---|---|
| dev build (what `godot --headless` runs) | 40.4 s | 44.8 s |
| exported binary, **before** the fix | 38.5 s | 42.4 s |
| exported binary, **after** the fix | **30.4 s** | **34.3 s** |
| dev build with `-p:Optimize=true` (for comparison) | 31.3 s | 35.6 s |

and `Converter --playtest 1`, pure .NET: **110.9 s → 88.1 s**, report identical once the clock is stripped.

So **÷1.3** (÷1.26–1.33 depending on which baseline reading), against the ÷1.38 this section promised. That
is the closest any of the three projected ratios came — **and it is now the last of them to be re-measured
against a post-R1 anchor.** (R1: ÷2.2 promised, ÷1.25 real. R2a: ÷3.3 promised, ÷5.9 real.)

⚠ `Optimize` is the *whole* of Release here: neither repo contains a single `#if DEBUG` or `Debug.Assert`,
so the DEBUG constant changes nothing at all. That is worth knowing before anyone reaches for it.

⚠⚠ **A measurement trap that cost two readings, written down so it costs nobody a third.** `-p:Optimize=true`
is a *global* property: it reaches the referenced engine projects (which is why it works) and writes optimized
assemblies into the engine's `bin/Debug`, where the next repo to build picks them up. Two of this step's
measurements were silently taken against each other that way. **And MSBuild's up-to-date check does not notice
a changed property**, so `dotnet build -p:Optimize=true` after a normal build is frequently a no-op that
reports "Build succeeded" and changes nothing. Both times the giveaway was the file size, not the log.

**Delivered:**
- `RogueDeck-Core/Directory.Build.props` — the configurations, and the optimizer for `ExportRelease`.
- `golden.sh --release` / `simulate.sh --release` — export once (≈12 s), then play every run out of
  `build/linux/bureaucrats-and-broomsticks.x86_64`. It fails **loudly** when the export templates are missing
  rather than quietly handing the batch back the slow build. Default stays the dev build: R4 takes Godot out
  of the batch path entirely, so this is the transitional half of R3.
- bnb-content's README carries the solution-vs-project trap and the command that actually works.

**Gate passed:** Core 1488 / Scenario 762 / Run 827 / Sandbox 387 green under the new props file;
bnb-content 1527 green; **`GOLDEN OK` through the exported release binary**, all 15 runs identical field for
field. Total run-seconds **5032 → 4015 (R1) → 1312 (R2a) → 976.5**. (Seed 1 is still 276 s of that: it is the
one run that draws, and drawing is not what Release makes cheaper.)

### R4 — One runner, out of Godot, many runs per process ✔ **DONE** — **÷2.2 short, ÷1.10 long**

**`RogueDeck.Bot` (new project, beside `RunPlayback`) holds the brain.** The answer loop, the four guards, the
policy, the card scoring, the report format — all of it, with no host in it. `RogueDeck.Bot.Cli`
(`roguedeck-bot`) plays N runs in one process on M threads; Godot's `--sim` is now ~120 lines that read the
command line, yield a frame every twenty answers and set the exit code.

**The strongest statement this arc has made:** the same seed, walked by the two hosts, produces
**the same log, line for line** — not the two report lines, the whole thing, every room, every choice, every
play, with only the clock stripped. And `GOLDEN OK` through **both**: Godot with the screen on, and the
console runner.

**Measured, this machine, 12 cores** (the runs identical in every column):

| batch | process per run | one process | |
|---|---|---|---|
| 48 short runs (400 hp, die in Act I), 12 jobs | 72.0 s | **32.6 s** | **÷2.21** |
| 12 whole-game runs (immortal), 12 jobs | 133.2 s | **121.2 s** | ÷1.10 |
| 12 whole-game runs, 6 jobs | 148.0 s | **134.2 s** | ÷1.10 |

⚠ **The ÷ is small for long runs, and the plan's "removes 5.15 s × N" is the reason it looked bigger.** That
fixed cost is paid in PARALLEL — at jobs = cores it is 5 s per wave, which a 30-second run drowns and a
3-second run does not. So the honest statement is: **R4 is worth ÷2.2 wherever runs are short** (the mortal
half of every batch, the whole defeat-path sweep) **and ÷1.10 where they are long**. The rest of its value is
structural, and that was always half the point.

⚠⚠ **A finding that nearly buried the step: the first version was SLOWER than what it replaced** — 184.6 s
against 148.0 s for the same twelve runs. Six runs in one process share one heap, and this walk is
allocation-heavy by construction (under the replay model every answer re-runs the run from its checkpoint),
so they spent their time collecting each other's garbage. `ServerGarbageCollection` turned 184.6 s into
134.2 s **and** cut peak memory from 1.39 GB to 0.97 GB. A batch tool that moves work into one process has to
be told the process is now a server.

**S8 is two-thirds dead, and the last third is named rather than pretended.** Godot's `--sim` and the console
runner are one brain. `MapRole.Of`, `RunBot.Where`, `TableState`, `Refused`, `FullCosts`, `CanPay` and the two
walk guards now have exactly one definition each, which the frontend calls. ⚠ **bnb-content's `RunWalker` was
deliberately NOT converted**, against the first draft of this plan: it checkpoints and restores every five
rooms, and that save/resume pressure is precisely the coverage §3 says must not be lost — `RunBot` does not
checkpoint, so swapping its brain in would have quietly dropped the one thing that walker is for. It walks for
a different question and keeps its own loop. The two probe walkers inside `SessionScreen` (`WalkUntil`,
`SmokeMarathon`) are likewise probe behaviour, not runner behaviour; they are the remaining duplication and
they are written down here rather than left to be discovered again.

**S6 is dead:** `Converter --playtest` is a `Parallel.For` with `--jobs` (default cores). Each walk writes into
its own buffer and the buffers print in seed order — interleaved progress from a dozen walks is a report
nobody can read. **Four walks: 307.1 s → 121.4 s, same report line for line.**

**`tools/simulate.sh` now uses both hosts, which is what R2a and R4 together are for:** the `--ui N` runs go
through Godot *because that is the only host with a screen to check*, and the rest go through `roguedeck-bot`
in one process. `--godot` forces the old shape.

**Delivered:** `src/RogueDeck.Bot` (BotPolicy · BotOptions · BotResult · **BotReport**, the one definition of
the two report lines three tools parse · CardFeatures · MapRole · InMemoryMetaStore · RunBot) ·
`src/RogueDeck.Bot.Cli` · `golden.sh --console` · `simulate.sh --godot` and the two-host split ·
`Converter --jobs`.

⚠ **`InMemoryMetaStore`, and why it is not a detail.** The cross-run meta profile is mirrored into every run
as `meta.<flag>` flags and saved back when a run completes — so a batch pointed at the player's real profile
plays a game that depends on which machine it is on (R0a's fault again) and writes a hundred run-completions
into the archive of somebody who was not playing. The console runner starts from an empty profile. Checked
before relying on it: today's document reads no `meta.` flag anywhere and fields exactly one character with no
unlock flag, so the profile cannot change a run — it is the DEPENDENCE that is removed, not a difference.
(Godot's `--sim` still uses the player's store; that is the next small thing to take, not a claim this step
makes.)

**Gate passed:** Core 1488 / Scenario 762 / Run 827 / Sandbox 387 · **`GOLDEN OK` through the console runner**
(130.8 s wall for the whole set at 12 jobs) **and `GOLDEN OK` through Godot with the screen on** · the smoke
battery unchanged.

### R5 — The bot drives directly, without the replay — ✔ DONE (2026-09-18), **÷2.05 per run, ÷2.31 per set**
The replay model exists so a single-threaded UI can park at a prompt. A bot never parks: it answers. So
`BotSeat` simply IS the collaborator — `IRunChoiceProvider` + `IRunEntityChooser` + `IRunInterlude` +
`ICombatDriver` + both in-combat choosers at once — and `RunRunner` walks the run exactly once. No park, no
restore, no re-execution.

**Measured, this machine** (the runs identical in every column):

| | replay model | walked once | |
|---|---|---|---|
| one immortal whole-game run, alone | 42.5 s | **20.7 s** | **÷2.05** |
| the golden set, 15 runs, 12 jobs | 183.3 s | **79.2 s** | **÷2.31** |

Peak memory is unchanged (471 MB either way): what is saved is work, not space.

**ONE BRAIN, TWO SEATS.** Every decision and every counter moved into `BotMind`; `RunBot.Play` is now the
REPLAY seat (the poll loop that hands answers back through the very methods a mouse click calls) and
`BotSeat` is the DIRECT one. Both build their run through the same `RunPlayback` — same blueprint, content,
registry, meta profile and labeler (`RunPlayback.Prepare` / `StartDirect`) — so the comparison below is
between two ways of ASKING, and nothing else. ⚠ The order the Random is drawn in is part of that contract: a
card before its target, a target only when a card was chosen.

**Gate passed:** Core 1488 / Scenario 762 / Run 828 / Sandbox 389 · bnb-content 1527 (the whole content
suite against the changed engine) · the smoke battery unchanged
(`--smoke-screens` 58 built / 0 FAILED, `--smoke-quit` resumes into a playable fight, `--smoke-boss 5`
arrives) · **`GOLDEN OK` through all three: Godot with a screen, the console runner through the replay model
(`--console --replay`), and the console runner walking once (`--console`).**

#### ⚠⚠ What the gate found: three faults in the mid-fight save, and none of them were the runner's

The equality did not hold at first — eleven of the twelve immortal runs came out different. Every difference
was the SAME kind of thing, and every one of them is a bug a player meets by pressing "save and quit" inside
a fight. The replay model takes that same capture at every turn boundary, which is why a bot walking one run
two ways could find in an afternoon what months of playing had not.

1. **The Queue was not in the capture.** `CombatantCardZonesSnapshot` carried five zones and not the sixth —
   so every card the Bureaucrat had QUEUED (played, paid for, targeted, waiting) was silently destroyed by a
   save. The cost had been paid for nothing and no message said so. The locked target was not captured
   either. *(Fix: `QueuePile` + `QueuedTargetId` in the snapshot, restore and hash, all defaulted so an older
   save reads as an empty queue. Test: `QueueTortureTests.A_save_taken_with_a_card_waiting_…`.)*
2. **Queueing opened an action and never closed it.** `CardPlay` returns early on the queue path, past the
   `CloseAction`. "Once per action" is claimed against whatever action is OPEN, and outside one no claim may
   succeed at all — so a stale scope standing for the rest of the fight let once-per-action rules fire at
   status ticks and turn boundaries. Measured before the fix: a scope was open at **1397** hero turn
   boundaries in one run. *(Fix: close it behind the effect list, exactly as a program-less card does. Test:
   `Queueing_closes_its_action_instead_of_leaving_the_scope_standing`.)*
3. **Resuming a fight spent the player's free step.** A run resumed inside a fight re-enters the room it is
   already standing in, and a node is never its own successor — so `AdvanceToNode` read the re-entry as a
   step off the paths and decremented `UnrestrictedSteps`. Every mid-fight save cost one. *(Fix: `from !=
   nodeId`. Test: `Re_entering_the_room_it_is_already_in_does_not_spend_the_step`.)*

A fourth difference was the runner's own: a card that PARKS to ask its own question records the park as a
problem step, and `RunBot.Refused` read that as "the rules said no" and struck the card off the turn — while
the direct seat, where nothing parks, kept offering it. A park is the engine working; only a refusal counts.

**The golden set was therefore re-recorded** (2026-09-18, through Godot, one run drawing). **Eight of its
thirty lines did not move: the three mortal runs and immortal seed 3.** Those die in act I or hold nothing in
the queue — short fights, few turn boundaries, no free step in hand — which is exactly the shape the three
faults needed in order not to bite. The other eleven immortal runs are different runs now, and they are
different because the game they were recorded from had three bugs in it.

**Delivered:** `src/RogueDeck.Bot/BotMind.cs` + `BotSeat.cs` · `RunPlayback.Prepare` / `StartDirect` ·
`RunEntityLabeler.Display` (one spelling of a picked thing's name, for both seats) ·
`roguedeck-bot` walks once by default, `--replay` for the other driver · `golden.sh --console --replay` ·
the three engine fixes above with their tests.

⚠ The replay path keeps its coverage: it is still the driver Godot's `--sim` uses, still half of what
`golden.sh --console` checks, and still what `--smoke-quit` walks.

**After R1–R5, measured** (2026-09-18, this machine, 12 cores; the "today" column is §0's anchor):

| | today | after | |
|---|---|---|---|
| one immortal whole-game run | 324.5 s | **20.7 s** | ÷15.7 |
| the golden set (12 immortal + 3 mortal), 12 jobs | 5032 s of run-time | **79.2 s wall** | — |

⚠ The promised "~15 s" is not quite reached and the honest number is 20.7. The projections were multiplied
together; the real factors were ÷1.25 (R1), ÷5.9 (R2a), ÷1.3 (R3), ÷1.10 for a long run (R4) and ÷2.05 (R5),
and a long run is exactly the case where R4 pays least.

⚠ And a run alone is not a run in a batch: the golden set puts twelve whole games through twelve cores in
79 s, which is about **40 s of WALL per run** once they are all competing for the machine — not 20.7. So a
500-seed immortal sweep is about **half an hour at 12 jobs**, against the three days §0 would have cost. That
is the instrument §5.3 asked for, and the champion runner's ~5× on top of it is what has to be budgeted
next, not assumed away.

---

## 5. Then: make them play well

Speed is the enabler; the balance question is the goal. **A seed is "fine" when a perfect player always
reaches the end of Act IV.** Today's runner cannot answer that, and not because it is slow.

### 5.1 What the trained runner actually does not do

- ~~**It does not build a deck.**~~ **✔ B1, 2026-09-18.** It was random even under a policy — the brain was
  handed display STRINGS, and a string cannot be turned back into a card. It now receives each offer's
  identity (`EntityArt`, the same one a reward screen draws its card face from) and scores it with the
  evaluator that scores a card in hand. *Read B1 below before believing this made the runner stronger: it
  made the runner ANSWERABLE, which is not the same thing.*
- **It picks doors by their ordinal position.** `EventLate` is one scalar, clamped into the choice list's
  index (`PickChoice`). A door is chosen by *where it is printed*, not by what it does.
- ~~**It cannot see magnitudes.**~~ **✔ B3, 2026-09-18.** It counted how often `node.dealDamage` appeared as
  a **substring of the card's JSON**, so a 3-damage jab and a 30-damage haymaker scored the same. The
  evaluator now walks the authored program and adds up what it actually applies.
- ~~**It cannot see what is coming.**~~ **✔ B4, 2026-09-19.** `ActionIntent` carried only `Label` + `Kind`,
  so blocking the right amount was not computable from what the engine exposed. `InteractiveCombat.Foresee()`
  now answers it exactly — by forking the fight and letting the enemies take their turn on the copy.
- **Shop buying is a coin flip** (`ShopBuy` picks a random `buy-` choice).
- ~~**The fitness is a proxy for the wrong question.**~~ **✔ B6, 2026-09-18.** It bred on *damage taken at
  9999 HP to an act's boss* — an immortal runner never has to survive, never has to block, never faces a
  death spiral. `tools/train.py` now breeds on the real question by default, and asking it produced the
  finding that reorders the rest of this plan (see B6).

### 5.2 What to do about it, in order

- **B1 — Let the policy choose what it takes. ✔ DONE 2026-09-18 — and it did NOT win.** Reward cards,
  relics and shelf slots now arrive with their identity beside their name (`BotMind.EntityPicks` takes an
  `EntityArt` list; the direct seat reads it off the candidate, the replay seat off the request it parks
  with) and are scored by the same evaluator that scores a card in hand. A relic is weighed without the cost
  term. `RewardSkip` stopped being a coin flip and became a threshold on the DECK RANK — walk away from a
  card that beats less than this share of the deck it would join. In a shop, the best slot on the shelf is
  bought and price only breaks a tie.

  **Measured, 8 immortal seeds, the same hand-written policy on both sides** (the six seeds that ran to the
  end on both): **42388 health left → 37960**, i.e. the runner takes about **10 % MORE damage** when it picks
  greedily than when it picks at random. Four of the six seeds improved; one (seed 5) lost 4083 health on its
  own. The deck says why: seven copies of *Notarial Press*, five of *Waxing Authority*. **The evaluator
  cannot see magnitudes** (§5.1), so "best" means "most effect nodes", which buys multi-hit chaff — and a
  greedy picker on a blind evaluator builds a worse deck than the dice do, because the dice at least
  diversify. What B1 delivers is not strength but REACH: before it, no amount of breeding could move the
  deck at all, and the search space now contains deck-building.

  ⚠ **The gene has changed meaning, so every weight bred before today is stale** — `RewardSkip` used to say
  "decline everything declinable", it now says "how much better than my deck must this be". Breeding again
  is part of the step, not an afterthought.

  ⚠ **One fault found in B1's own first draft, and it is written down because it was measured rather than
  reasoned about:** the walk-away rule ranked EVERY offer against the deck, and a relic — about one point to
  the crude features, where a deck card is worth several — beat almost nothing in the deck and was declined.
  A run reached act V with **4 relics where the dice player had 27**, and paid ~4000 health for it. What is
  free is taken; only a card is ever refused. Pinned by `RunnerPicksTests`, which falls over without it.

  ➜ **This reorders what comes next: B3 is now the prerequisite for B1 to pay**, not an improvement on top
  of it.
- **B2 — Choose doors by their effect.** Score an `EventChoice` by what its program does (gold, hp, cards,
  relics, curses), not by its index.
- **B3 — Read the authored numbers. ✔ DONE 2026-09-18 — and it did not win either.** `CardFeatures` walks
  the authored program instead of counting words: constants are read, arithmetic over constants is folded, a
  repeat multiplies by its own count, "choose 1 of 3" scales by a third, a conditional branch counts half,
  and a spell on every enemy multiplies by **how many enemies this game's own encounters have** (1.41). Three
  numbers the document cannot supply are named in one place — `MaybeRuns`, `ScalingTerm`, `CardsInAZone`.
  Every bucket is normalized by the average card **that does that thing**, so 1.0 = "what a card of this game
  that blocks, blocks".

  ⚠ **The first normalization was wrong in an instructive way:** dividing a bucket by the mean over ALL cards
  divides it by all the zeroes in it, so the RARER an effect is the bigger every instance of it scores. Block
  (about a fifth of the cards) came out 2.5× too large, and the runner blocked for a hundred turns in the
  first fight of the game without ever killing anything. Measured, not reasoned about.

  **Measured by BREEDING, which is the only fair way to judge an evaluator** (weights bred for the old one
  mean nothing under the new one). Same budget, same search seed, same identical starting population —
  4 generations × 8 runners × 2 seeds — then both winners played over **ten held-out seeds** under their own
  build:

  | bred against | reached the act-V boss | mean damage on the way |
  |---|---|---|
  | the old word-counting evaluator | 7/10 | **4408** |
  | the new authored-amount evaluator | 8/10 | 8250 |

  ⚠⚠ **AND THE BRED WEIGHTS SAY WHY, IN ONE NUMBER: `WDamage = −1.84`.** The new evaluator is sharp enough to
  express "avoid attacking", and against the fitness as written — *damage taken at 9999 HP on the way to a
  boss* — avoiding attacks is CORRECT: a runner that never kills anything never gets hit back, it just takes
  longer. Two of its ten runs stalled a fight for a hundred turns. The old evaluator was too blunt to find
  that exploit; the new one found it immediately. **A sharper answer to the wrong question is a worse
  runner** — so the binding constraint is no longer the evaluator.

  ➜ **B6 moves to the front. The question has to be fixed before the answer is sharpened any further.**

- **THE TRAINER NOW BREEDS THROUGH THE CONSOLE RUNNER (2026-09-18).** `tools/train.py` started a whole Godot
  per run, driven through the replay model — it had never been given R4 or R5. One runner's seeds are now
  played by ONE `roguedeck-bot` process answering the engine inline; `--godot` is the way back. Measured:
  4 generations × 8 runners × 2 seeds in **~5 minutes**, where the README still warned of 10–20 minutes per
  RUN. This is not decoration: an evaluator can only be judged by breeding against it, and before this the
  judgement cost a day.
- **B4 — Engine seam: an intent carries its number. ✔ DONE 2026-09-19 — and the number is PLAYED OUT, not
  annotated.** Annotating it would author the same number twice — once in the program that applies it, once
  in the label that promises it — and the two would part company on the first relic that changes damage. So
  `InteractiveCombat.Fork()` makes a throwaway copy of the fight and `Foresee()` simply lets the enemies take
  their turn on it. What comes back is not an estimate: it is what will happen, with every strength stack,
  vulnerability, guard and passive modifier already in it, because it is the same code that will run.
  Per blow: who throws it, what it is telegraphed as, what it costs in health and in guard, and whether it
  kills. Every test foresees, then ends the turn for real, and holds the two against each other.
  ⚠ A fork has nobody sitting at it, so an enemy action that ASKS something is answered by the headless
  default on the copy; that is the one case where a projection can differ from the event.

- **B5 — Champion mode. ✔ DONE 2026-09-19, and it PLANS THE WHOLE TURN (2026-09-19, second pass).**
  `--champion` (and `--sim-champion` in the game) decides by forking the fight, playing cards on the copy,
  letting the enemies answer and looking at what is left. One gene: `Aggression`. `tools/train.py --champion`
  breeds them.

  The first version picked the best single card, played it and asked again — which is GREEDY, and a greedy
  player can never find *"A alone is worse than doing nothing, but A then B wins"*. The search is now over
  SEQUENCES: at every point the turn may stop (be ended and scored) or take one more card, and what is
  compared is whole turns. What makes that affordable is that **a fight is a deterministic puzzle**: the dice
  are bound to the seed and the step count, the enemy's intent is a function of the state, and
  `CombatStateHasher` fingerprints every position — so two orders that arrive at the same table are searched
  once. Measured on real fights: **~170 forks for a three-card turn**, and an act-I run goes from ~6 s to
  ~20 s. Two named ceilings (`PlansAhead`, `ForksPerTurn`) keep a looping hand from turning a plan into a
  hang; hitting them costs the turn its optimality, not its correctness.

  ⚠ **"The fighting is now automated" is true to ONE TURN's depth and no further.** Inside the turn the
  search is exhaustive; beyond it the position is still scored by a heuristic (the race), not played to the
  end of the fight. Whole-fight search is the next rung, and it is the rung that would let the runner's
  failure be called a proof.

  ⚠⚠ **ITS FIRST EVALUATOR STOPPED PLAYING CARDS ALTOGETHER, and the fight was right to make it.** It scored
  a turn by what was LEFT of each side — health kept against enemy health removed. Measured on the fork
  against the very first enemy of the game, the Contradictory Signpost: ending the turn having done nothing
  cost **0** health; playing a six-damage jab cost **15**, because that enemy punishes acting. At one ply,
  on a scale where standing still is free, refusing to play is correct — and it lost every fight, since the
  enemy's damage ramps while you wait. A fight is a RACE, so what is scored is now turns: this turn removed
  so much of them and cost so much of me, and at that rate, who runs out first. Both rates are measured on
  the fork; the ceiling for "never" is the runner's own `TurnsAFightShouldNotNeed`.

  **What the champion is worth, measured:**

  | | dice player | champion |
  |---|---|---|
  | immortal seed 1, damage to the act-I boss | 790 | **488** |
  | immortal seed 1, damage over the whole game | 8563 | **7493** |
  | whole games won (immortal, seeds 1–3) | 3/3 | **3/3** |
  | seconds per immortal run | ~21 | ~83–107 (**≈4.5×**, the plan predicted ~5×) |

  The two runners the plan asked for now exist and are both worth having: the **coverage runner** stays fast
  and dumb for hundreds of seeds (crashes, walls, unreachable content), the **champion** is slow and careful
  and is the only one whose failure to clear an act says anything about the act.

  ⚠⚠ **AND THE CHAMPION FOUND THE BUG THE WHOLE INSTRUMENT HAD BEEN CARRYING.** Put on a real body it never
  healed — `healed=0` over eighteen rooms, with two rest sites walked through on the way. The runner answered
  every door with a way out the way it answers a SHOP: *nothing worth buying, so leave*. A rest site offers
  `rest`, `amend` and `leave`, and has nothing to buy. **This runner had never rested once, in the entire
  history of the project**, and nobody could have seen it: every measurement before B6 was taken on a 9999-hp
  body, where never resting costs exactly nothing. Fixed, with one new gene (`RestBelow`) and a test that
  falls over without it — the heal is READ off the door (`ComputedHealRunEffect` evaluated against the run),
  never guessed.

  **Where that leaves V-7's question — quantified, and no longer unbounded:**
  - the champion wins every immortal game and takes 38 % less damage through act I than the dice player,
    so it is the best player we have by a measurable margin;
  - on a real 70-hp body it now rests, and still dies at **room 13–18 of act I's 22**;
  - early act I costs it about **5 health per fight**, and a rest site gives back about **18**.

  So the gap is small and countable, not a wall. The honest verdict is still *"we do not know yet"* — but the
  next thing to try is named: **the runner cannot drink a potion.** It never uses a consumable, which is a
  real player's emergency button, and in a fight that is lost by five health that is exactly the margin.
- **B6 — Breed on the real question. ✔ DONE 2026-09-18 — and the answer is about the RUNNER.** Real health,
  real death, fitness = *did it clear act IV*, scored per seed. Three pieces:
  - the runner reports it: a third line, `sim-clearance: … cleared=3 reached=4 hp=0/70 stopped=elite
    at=act 4 r12c0 (…)`. ⚠ It is its own line **because `golden.sh` diffs the other two field for field** —
    adding a field to either would have moved fifteen recorded runs for a change that alters no behaviour.
  - `tools/train.py --question clearance` is now the DEFAULT, on the body the game authors rather than on
    9999 hp, targeting **act IV** (the design's promise; act V is the cherry). `--question damage` keeps the
    old proxy. Ties break on distance walked and health left — never on damage taken, which is a question for
    the balance report and not for the selection.
  - `tools/unbeaten.py` is the report itself: N runners × M seeds on a real body, and out comes *which seeds
    nobody got through, and the room each one died in*.

  **What came out, and it is the most important measurement in this document:**

  | asked | answer |
  |---|---|
  | 10 generations × 8 runners × 3 seeds, bred on clearance | best runner: **0 of 3 seeds**, 13.7 of act I's ~22 rooms |
  | 20 seeds × the 2 best runners we have, act **I** only | **20 of 20 unbeaten** — one seed reached the act-I boss and died there |

  ⚠⚠ **NOT ONE RUNNER WE HAVE CLEARS ACT I ON A REAL BODY.** So this list is, today, a statement about the
  RUNNER and not about the content, and it must not be read as a balance verdict. The runner plays cards by
  what they are worth in isolation: it cannot see what is coming at it, so it cannot block the right amount —
  *the single most important skill in the genre* (§5.1) — and it cannot look one move ahead.

  ➜ **B4 and B5 stop being refinements and become the prerequisites for any answer at all.** V-7's question
  cannot be answered by an instrument that dies in act I; the honest state is *"we do not know yet"*, and
  that is worth more than a number bred against immortality.

### 5.3 The sweep that closes V-7
With a run at ~15 s and a champion at ~5×: **500 seeds × 3 candidate champions ≈ 30 min on 12 cores.** That is
the instrument the goal actually needs, and it is out of reach today by a factor of about 60.

**✔ The sweep itself exists since B6 — `tools/unbeaten.py` — and the speed is there** (a mortal run dies in
act I in seconds; a whole 20-seed sweep with two runners takes under two minutes). What is missing is not the
instrument but the PLAYER: run it today and it reports every seed as unbeaten, because the runner cannot
survive act I. The sweep becomes evidence about the content the day B4+B5 make the runner a player.

---

## 6. Found in passing — both now fixed ✔

⚠ **Read R0b before believing the first bullet's framing.** It names the card the simulator named, and the
card turned out to be innocent: the fault is the carry-over decree meeting the ordinary cost payment, and it
would have fired on whatever card came up first. The bullet is left as it was written so the shape of the
mistake stays visible — *the reproduction named a symptom, and the symptom named the wrong suspect.*


- **A real engine fault, reproducible now:** `godot --headless -- --sim --sim-seed 1 --sim-immortal` reports
  `!! PROBLEM playing contempt_finding at act 5 … (act_5_enlil_voice_of_the_unalterable_decree): Step threw
  resolving 'contempt_finding': ArgumentOutOfRangeException: Pool value cannot exceed max. (Parameter 'value')`.
  Deterministic — it survived every variation measured today. → **R0b.** A throw inside a god fight would
  poison exactly the runs the balance question is about.
- **The simulator's map comes from outside the repo.** Not, as first written here, a hardcoded difference
  between `--sim` and `--playtest`: `--playtest` names v0.0.1 outright, `--sim` reads
  `RunPreferences.MapGenerator` → `user://settings.cfg`. This machine's file says `v0.0.1`, so they agree
  today by accident. → **R0a.**
