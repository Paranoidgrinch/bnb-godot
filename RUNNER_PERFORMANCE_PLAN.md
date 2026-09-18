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
   - the balance runner never draws; a named share of the batch (`--ui N`, default 5) still does, and now
     actually fails when the screen does.
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

### R0c — The golden run set (half a day)
Freeze the instrument before moving it.
- `tools/golden.sh`: 12 named seeds × immortal, records `sim-fitness:` + `sim-result:` into
  `tools/golden-runs.txt`.
- A `--check` mode that re-runs them and diffs. **Every phase below gates on this.**
- Also freezes the measurement: the file carries the wall clock per seed, so a regression in speed is as
  visible as a regression in outcome.

### R1 — Compile once per fight, not once per answer *(RogueDeck-Core)* — **÷2.2, measured**
The library half of every fight's registry is constant for the whole process; only the encounter's own
triggers, the run's relic contributions and the hero's projected deck vary.
- Give `CombatDefinitionRegistryBuilder` a **prebuilt base registry** to start from, and let `Encounters` hold
  one compiled library per catalog. The registry is already read-only after `Build()` (checked: no public
  mutator on `CombatDefinitionRegistry`), and the triggered programs / interceptors are *already* shared
  instances across fights today — this shares nothing that is not shared now.
- Alternative if the seam proves awkward: memoise `CompiledScenario` in the run layer, keyed on encounter id +
  a fingerprint of (deck definition ids and tags, enabled relic ids, unit ids, hero resources). The prototype
  keyed on the fight's identity alone and already hit 98 %.
- ⚠ The key must include everything `ApplyRunProjection` reads (`CombatNode.cs:361`) — deck, tags, relics,
  units — or a fight would be built from a stale deck. **This is the one place in the plan that can change the
  game if it is got wrong**, which is why the golden set exists.
- **Gate:** Core 1475/755/387/827 + bnb-content 1526/1526 + golden set identical.
- **Bonus, worth saying out loud:** this is the fix for player-facing click latency in Act V.

### R2a — The runner stops drawing, and a screen fault becomes a finding *(bnb-godot)* — **÷3.3, measured**
- `SessionScreen._Ready` subscribes `Rebuild` only when a human (or `--sim-ui`, or a smoke probe) is watching.
- ⚠ **First, the part that is a bug fix and not a speed-up:** wrap `Rebuild()` so a throw becomes
  `problems++` + `!! PROBLEM screen at <where>: <exception>` + a non-zero exit. Without this, R2a would be
  removing something that was never working; with it, the drawing runs start earning their cost.
- `simulate.sh` / `train.py` gain `--ui N` (default 5) and run that many of the batch with the screen on.
- **Gate:** golden set identical; the deliberate-fault experiment of §1.1b now **fails** the run; `--smoke-*`
  battery unchanged; the 5 UI runs of a 100-run batch clean.

### R2b — `--smoke-screens`: every kind of screen, built once, on purpose *(bnb-godot)*
The systematic half of the frontend check, separated from the balance question entirely — it does not walk a
run for its content, it walks it for its *screen states*.
- Drive a seeded run (immortal, v0.0.1) and, at each distinct state, build the screen and assert it builds:
  combat · node fork · event/door · shop · rest · reward pick · entity pick · in-combat card choice ·
  in-combat option choice · interlude · defeat · victory — **in each of the five acts**, plus the boss and
  elite variants and an Act-V god.
- It reports a **table of state × act with what built and what did not**, so a gap in coverage is visible as a
  gap rather than as silence. A state the walk never reached is reported as *not reached*, never as *ok*.
  (That is the D7 lesson: three screenshots that were the same defeat screen all reported success.)
- Stays a Godot probe by nature — it is the one thing that cannot move to the console runner.
- **Gate:** every state × act either builds or is honestly reported as unreached; runs in the smoke battery
  beside `--smoke-marathon`.

### R3 — Release *(all three repos)* — **÷1.38, measured**
- The console runner (R4) builds `-c Release`; `simulate.sh`'s Godot runs come from the exported release
  binary, or from a Release-configured headless build.
- **Gate:** golden set identical. (A Release-only difference would itself be a finding worth having.)

### R4 — One runner, out of Godot, many runs per process — **removes 5.15 s × N**
- Lift `RunSimulator`'s brain (the answer loop, the four guards, the policy, the log format) into a
  Godot-free `RogueDeck.Bot` library beside `RunPlayback`. `RunWalker` (bnb-content) and Godot's `--sim-ui`
  both become thin callers of it, so **S8 dies**: one behaviour, one log format, one default generator.
- `tools/simulate.sh` drives a **console runner** that plays N runs in one process on M threads: the 11.31 MB
  document is parsed once, the JIT warms once, the act plan cache is shared.
  - ⚠ Per run: its own `RunPlayback`, its own RNG, its own try/catch, and a watchdog thread — a crash must
    still cost one run, not the batch. That is the single property the process-per-run model was bought for,
    and it has to be paid for again in-process.
  - ⚠ `RunPlayback._actPlan` is per-instance for exactly this reason (see its comment) — do **not** make the
    new caches static.
- Default job count = cores.
- `Converter --playtest` stops being a `for` loop (**S6**).
- **Gate:** golden set identical run through the console runner AND through Godot's `--sim-ui`.

### R5 — The bot drives directly, without the replay — **÷~2.4, projected**
The replay model exists so a single-threaded UI can park at a prompt. A bot never parks: it answers. 
- An `IRunChoiceProvider` + `ICombatDriver` pair that answers inline and drives `InteractiveCombat` forward
  once. No park, no restore, no re-execution — `RunRunner` walks the run exactly once.
- The replay path keeps its coverage through the `--sim-resume` probe of §3.
- **Gate:** the golden set identical **through both drivers** — that equality is also the strongest statement
  the project has ever made that replay and direct play are the same game.

**Expected after R1–R5** (measured where marked, otherwise projected from the measured factors):

| | today | after |
|---|---|---|
| one immortal whole-game run | 324.5 s | **~15 s** |
| 100 immortal runs | ~68 min (8 jobs) | **~2 min** (12 jobs) |
| `train.py` defaults (80 runs) | ~1 h 50 m | **~2 min** |
| `train.py` 10×12×3 (360 runs) | ~4 h | **~8 min** |

---

## 5. Then: make them play well

Speed is the enabler; the balance question is the goal. **A seed is "fine" when a perfect player always
reaches the end of Act IV.** Today's runner cannot answer that, and not because it is slow.

### 5.1 What the trained runner actually does not do

- **It does not build a deck.** `SimPick` is **random even under a policy** (`RunSimulator.cs`, the
  `IsAwaitingEntities` arm): the policy decides *whether* to skip an offer, never *which* card or relic to
  take. A run's strength in this genre is mostly its deck. **This is the biggest single gap in the instrument.**
- **It picks doors by their ordinal position.** `EventLate` is one scalar, clamped into the choice list's
  index (`PickChoice`). A door is chosen by *where it is printed*, not by what it does.
- **It cannot see magnitudes.** A card's value is the number of times `node.dealDamage` appears as a
  **substring of its JSON** (`Features`). A 3-damage card and a 30-damage card score identically.
- **It cannot see what is coming.** `InteractiveCombat.UpcomingIntentFor` exists, but `ActionIntent` carries
  only `Label` + `Kind` — **no number**. Blocking the right amount, the single most important skill in the
  genre, is not computable from what the engine exposes.
- **Shop buying is a coin flip** (`ShopBuy` picks a random `buy-` choice).
- **The fitness is a proxy for the wrong question.** It breeds on *damage taken at 9999 HP to an act's boss*.
  An immortal runner never has to survive, never has to block, never faces a death spiral, and its deck never
  has to carry it — so its damage total is a weak predictor of whether a real-health player lives.

### 5.2 What to do about it, in order

- **B1 — Let the policy choose what it takes.** Score reward cards, relics and shop goods with the same
  evaluator that scores a card in hand. *(Biggest win; smallest change.)*
- **B2 — Choose doors by their effect.** Score an `EventChoice` by what its program does (gold, hp, cards,
  relics, curses), not by its index.
- **B3 — Read the authored numbers.** Replace substring counting with the real amounts off the card's
  program. The evaluator becomes a function of the game, not of the JSON's spelling.
- **B4 — Engine seam: an intent carries its number.** Let `ActionIntent` state the damage/block it is about
  to apply. Additive, no rule changes — **and the player's screen wants it as much as the bot does**
  (today the UI shows a word where every game in the genre shows a number).
- **B5 — Champion mode: one-ply lookahead.** A full combat clone was measured at **~0.2 ms** (snapshot 0.04 ms
  + restore 0.17 ms), so trying every playable card and scoring the resulting state is affordable: ~5 clones ×
  ~4 ms per decision. That is roughly **5× the cost of a dumb run** — which is exactly why R1–R5 come first.
  Two runners for two questions:
  - **coverage runner** — fast, dumb, hundreds of seeds: finds crashes, walls, unreachable content;
  - **champion runner** — slow, careful, few seeds: answers whether a seed is beatable.
- **B6 — Breed on the real question.** Real health, real death, fitness = *did it clear Act IV*, scored per
  seed. The report the player wants is not a damage number: it is **"these 7 of 500 seeds no runner we have
  can beat, and here is the room each one died in."**

### 5.3 The sweep that closes V-7
With a run at ~15 s and a champion at ~5×: **500 seeds × 3 candidate champions ≈ 30 min on 12 cores.** That is
the instrument the goal actually needs, and it is out of reach today by a factor of about 60.

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
