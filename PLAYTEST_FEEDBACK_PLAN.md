# Playtest Feedback Plan — the first alpha, answered

**Status:** proposed 2026-09-26, after the first public alpha (0.2.0). **Built the same day:** P0-1, P0-2, P0-3,
P1-1, P1-2, P1-3, P2-1…P2-5, P3-1…P3-4, P4-1, P4-2. **Open:** P0-4 (narrowed, tooling built), P0-3b, P5 (waits
for the user's word on the proposal below). Source: the user's collected player
feedback (19 points, quoted per phase) plus what the 11 recorded runs in `Paranoidgrinch/bnb-runs` show when
they are replayed.

Three repos are touched: **bnb-godot** (almost everything), **bnb-content** (Rites, prices, upgrades, the
campfire, event summaries), **RogueDeck-Core** (one seam for the campfire, the replay divergence).

---

## What the runs say (measured 2026-09-26, not assumed)

- **11 runs, 7 players, 0 wins.** Furthest: Act IV r5 (Sandale). Deaths cluster in **Act I r11/r12** (4 of 9
  defeats: `devouring_waiting_room`, `old_statute_ghost`, `unsigned_form_ghost`, …) and **Act II r5–r8**.
  Several players played the SAME seeds (165882923 ×4, 249771018 ×3) — the seed-sharing works.
- **Replays: 9 of 11 reproduce exactly. 2 diverge — both by GOLD only** (recorded 104 vs replayed 100 at
  Act II r12; 191 vs 196 at Act IV r6). One of them was never resumed, so it is not the resume path.
  → **P0-4**: the recording is the whole value of bnb-runs, and a run that cannot be replayed cannot be
  studied.
- **Card pools do NOT leak.** Every one of the 1 281 recorded card-reward offers is a card whose act gate is
  ≤ the act it was offered in. What the player sees is the design as written: the pool is **cumulative**
  (`bureaucrat_final_cards.md` §1: "Act II → 60 cards available", Act-I cards included), plus the General
  pool beside the Bureaucrat's. → **P5-3** is therefore a design decision, not a bug fix.
- **The campfire:** 18 "improve a card" visits recorded, 5 of them with NO card pick after — all five inside
  the two diverged runs, late (Act II/IV), where the deck had nothing upgradable left. The choice is offered
  unconditionally (`EventTemplates.Rest`: "a choice with nothing to improve picks nothing and does nothing").

---

## Phase P0 — bugs and quick content fixes (small, independent)

**P0-1 Rites exhaust.** *"power karten … sollten exhausten, das man das ganze sonst stacken kann"*
A Rite is BnB's power (`CardAuthoring.RiteTag`, "a persistent combat effect"). Every Rite goes to the exhaust
pile when played; its rules text says so ("… Exhaust."). One place: `BnbCard.Compile` (a Rite's destination is
the ExhaustPile, as the `exhaust` tag already does). Test: every card tagged `rite` in the document is
exhaust-on-play, and its text ends in Exhaust.

**P0-2 Shop relics 20 % cheaper.** *"shop relics sind zu teuer … im zweifel einfach 20% billiger"*
`ShopTemplate.RelicPrices` 130/190/260/190 → **104/152/208/152**. Every relic on a shelf, not only the Shop
pool (the Normal relics on the same shelf are the comparison the player makes). Cards unchanged.

**P0-3 The campfire with nothing to improve.** *"improve button ausgegraut … 'theres nothing to improve'"*
- Engine (Core): an event choice can be **shown but unavailable** — `EventChoice.Requirement` today HIDES a
  choice. Add `EventChoice.DisabledText` (optional): when set and the requirement fails, the choice is offered
  as visible-but-disabled with that reason. Default null keeps every existing event byte-identical.
- Content: `amend` gets `Requirement = Count(DeckCards.Upgradable()) > 0` and
  `DisabledText = "There's nothing to improve."` (if `Count` over a selector does not serialize, a small
  `UpgradableCardCount` expression — same pattern as `DeckSize`).
- Godot: a disabled choice renders greyed; clicking it toasts the reason. Probe `--smoke-rest` with an
  all-upgraded deck.

**P0-3b "Upgradable" includes cards with no "+" form.** `RunSelectors.Upgradable()` is `UpgradeLevel < 1`, so a
Junk card or a curse is offered at the campfire and "improving" it does nothing. The engine has no card
definitions in a run expression; a fix wants either a content-side id list or a run-level "has an improved form"
fact. Not built.

**P0-4 The two diverging replays.** ⏸ NARROWED, NOT SOLVED (2026-09-26). Both recordings agree with their replay on
every card offer up to ONE fight's victory; in that room the run RNG differs (spoils gold and the card offer),
while the fight itself is identical (same HP). Ruled out by experiment: previews (`Foresee`, card-preview forks),
autosave, save→resume at every interlude, and checkpoints (the replay without any is the same). The live host
drew or skipped something the recording does not carry. Tooling for the next look: `--replay-run … --log-from A`
and `--resume-every N` (Core `2a4dba4`). Original text: Find why gold differs by +4/−5 on replay (`roguedeck-bot --replay-run`
over the two files; bisect the room where `gold=` first differs). Likely a host-side gold source the recording
does not carry, or an unordered iteration. Fix in Core; both recordings must then REPRODUCE.

---

## Phase P1 — the screen gets its width back (layout foundation)

**P1-1 The right sidebar goes.** *"die rechte leiste des spiels ist mittlerweile komplett obsolet"*
The deck is on **D**, piles have their own views. The sidebar's remaining jobs move:
- HP, gold, seed → a slim **top bar** across the full width (one line, always visible, all screens).
- Relics → **P3-1** (grid above the hero in combat; the same grid in the top bar outside combat).
- The run log → an overlay on **L** (and in the Esc menu). Nothing is lost, nothing takes permanent room.
- `--smoke-shelf` is rewritten for the new relic grid.

**P1-2 No vertical scrolling.** *"viele probleme mit der vertikalen … scrollen … vermieden werden"*
Rule for every non-map screen: it fits 1280 × 720 without a scrollbar. The shop becomes columns (cards left,
relics + services right) instead of a stack; the reward, event and campfire screens are short by construction
once centred. A probe measures every screen's content height against the pane (the map is exempt: it is a
graph taller than the window by nature, and it already scrolls to the current room).

**P1-3 Centred screens.** *"bei events etc faengt der text immer irgendwo oben … sollte mittig sein"*
Event, campfire, reward, interlude, fork and shop content sit in a centred column (max width ~760), centred
vertically, with the act picture behind it.

---

## Phase P2 — reading the game

**P2-1 Tooltips that wrap.** *"1 lange zeile ohne umbruch … ueber den rechten rand hinaus"*
One tooltip for the whole game: a panel with a max width (~360), word-wrapped, a bold title line and the body
under it, clamped inside the window. Implemented once (custom tooltip on a shared helper) and used by every
hover; the glossary audit (`--smoke-tooltips`) keeps its contract. Probe: the longest status text renders
inside the window.

**P2-2 Intents, ordered.** *"es sollte klar angeordnet sein, was der attack name ist und was die attack wirkung"*
The intent plate becomes two lines with fixed roles: **the move's name** (small caps, the enemy's own words) and
**what it does** as icons + numbers (⚔ 11 · ⬆ Strength 1 · 🛡 4). Multi-hit reads `9 × 2`. Hover explains each.

**P2-3 Event choices say what they do.** *"event texte sollten klar sagen was sie machen zusaetzlich zu ihrer
bunten beschreibung"*
Every event choice gets a second line in plain words, **generated from its effects** (gain/lose gold, HP, max
HP, add/remove/upgrade/transform a card, a relic, a curse, a fight) — the same describer the shop's
`WhatItDoes` starts, grown to cover the event effect palette. Where an effect cannot be described (an escape
program), the line says "Something unpredictable." rather than nothing, and a probe lists which events still
fall back so they can be authored by hand.

**P2-4 The Compendium (wiki).** *"im pausen und hauptmenue eine art wiki … jeder effekt (und status) … in
einfacher sprache mit beispiel"*
A new panel, reachable from the title screen and the Esc menu (same pattern as the Archive): every status and
keyword, alphabetical, searchable, with **its rule in plain words** and **one worked example**. The rule comes
from the document; the plain-language line and the example are authored in bnb-content as presentation data
(`Presentation.Statuses[id].Plain` / `.Example`) so the frontend stays generic. A test fails for any status a
card or enemy can apply that has no entry.

**P2-5 Right-click shows the upgrade.** *"rechtsklick auf eine karte … upgraded version … bei einer
upgegradeten karte nichts"*
In the shop, the reward pick and every deck/pile view: right-click toggles the card face to its `+` form (the
Archive already knows the pair). On a card that is already improved, right-click does nothing.

---

## Phase P3 — the fight

**P3-1 Relic grid over the hero.** *"mit ihren kleinen bildern in einem grid ueber dem hero … max 15
nebeneinander … wenn ein relic triggert, sollte der rahmen kurz aufleuchten … permanent … permanent leuchten"*
Small tiles (~28 px), 15 per row, above the hero's column; hover = the wrapped tooltip. A relic **flashes** its
frame when it fires (the engine already reports relic reactions; the host maps the event to the tile) and wears
a **steady glow** while it is a standing rule (a relic with a combat rule and no trigger).

**P3-2 Statuses as chips, not a wall of text.** *"effekte … visuell besser … als eine wall of text … durch die
man durchscrollen muss … aktivierung durch leuchten"*
Under each body: a wrapping row of icon chips (glyph + stack count), hover for the rule. A chip **pulses** when
its status resolves (tick, trigger, stack change). No scroll box.

**P3-3 Three to four enemies side by side.** *"3-4 gegner nebeneinander"* With the sidebar gone the arena is
~1240 wide: columns shrink to fit four; `--smoke-crowd` asserts four fit with nothing off-screen.

**P3-4 Boss and elite mechanics made visible.** *"die spezialmechaniken der bosse und elites brauchen ein
grosses visual overhaul"*
A **mechanic banner** under the enemy's name for every elite and boss that carries a signature rule: an icon,
the mechanic's name, a one-line rule, and a counter/dial where it counts (phases, charges, "in 2 turns").
When the mechanic fires (`RuleAnnounced`, phase change) the banner flares and the rule's line is shown centre
screen for a moment. First an inventory: every elite/boss signature and what state it exposes; then one visual
language for all of them.

---

## Phase P4 — run flow

**P4-1 The reward screen, reduced.** *"man bekommt dort direkt das gold und kann eine von 3 karten waehlen
oder skip"*
The fight's gold is granted without a click (shown as "+37 gold"); the three cards are clicked directly —
one click takes the card, no Confirm; **Skip** under them. Elite/boss relic rewards stay a click (they are a
choice). Done in content where the gold is an offer (`spoils` → a plain grant) and in the frontend for the pick.

**P4-2 The map legend on top.** *"die symbole fuer die kartennodes sollten auf der map oberhalb … nicht ganz
unten versteckt"* `MapLegend()` moves above the map.

---

## Phase P5 — cards (content design, bnb-content)

**P5-1 — THE PROPOSAL (waiting for the user).** The audit (134 cards with a "+"): 29 multi-effect cards move at most
one number and not their cost; these are the ones where the number that moves is not what the card is about:

| card | now → + | proposed + |
|---|---|---|
| Cursed Addendum | 6 dmg, 2 Paperwork → 8 dmg, 2 PW | 7 dmg, **3 Paperwork** |
| Waxing Authority | 5 dmg, 1 Seal → 7 dmg, 1 Seal | 6 dmg, **2 Seal** |
| Hex Circular | 7 AoE, 1 Doubt → 9 AoE, 1 Doubt | 8 AoE, **2 Doubt** |
| Petty Objection | 5 Block, 1 Doubt → 7 Block, 1 Doubt | 6 Block, **2 Doubt** |
| Rebuttal | 9 dmg, 4 Block/Doubt (max 12) → 12 dmg | 10 dmg, **5 Block/Doubt (max 15)** |
| Certified Kindling | 4 Block (+4 if Junk) → 6 (+4) | 5 Block **(+6 if Junk)** |
| Archive Pyre | 9 AoE + 5/Junk → 12 + 5/Junk | 10 AoE **+ 7/Junk** |
| Backlog Charge | 6 + 3/Queued → 8 + 3 | 7 **+ 4/Queued** |
| Monumental Writ | 24 + 12/Queued → 30 + 12 | 26 **+ 15/Queued** |
| Smudged Index | Archive 1 from draw pile, 4 Block → 6 Block | **Archive up to 2**, 5 Block |
| Deskward | 8 Block + Red Tape → 11 Block + Red Tape | 10 Block, **Red Tape into the exhaust pile** |
| Skeleton Staff | ⚠ the "+" is IDENTICAL to the base (a bug) | **costs 1** (the design sheet's Rite version queues a card of cost ≤ 3) |

Deliberately left: single-payload cards (Deferred Hex, Protective Adjournment, Stone Levy, Errata Furnace), the
ones whose "+" already changes a second thing (Formal Dissent, Privy Seal, Tallow Budget, Candle Allowance), and the
drawback cards whose junk is the price (Cauldron Copy, Cinder Warrant).

**P5-1 Upgrades that are not one-dimensional.** *"wenn es zb damage und paperwork verteilt, wird beim upgrade
nur damage erhoeht"*
An audit first, generated: every card with two or more effects whose `+` changes only one number. Then a rule,
applied card by card: a multi-effect card's upgrade improves **the effect that defines the card** (its
keyword — Paperwork, Seal, Doubt, Queue …), or both by one step, or lowers its cost; never only the damage of
a card that is really about something else. The proposal table is written out for review before it is
compiled in; the suite's golden numbers are re-taken afterwards.

**P5-2 Rite upgrades** follow P0-1: an exhausting Rite is worth more per play, so the audit re-checks Rite costs.

**P5-3 Card pools — DECISION NEEDED.** The measurement above says the pools behave as designed (cumulative).
If the feel is "the same cards everywhere", the options are: (a) keep cumulative; (b) **weight the current
act's own cards** (e.g. 60 % of an offer from the act's new cards); (c) make act pools exclusive. The
recommendation is (b).

---

## Order of work

P0 (all four) → P1 → P2-1, P2-2 → P3-1, P3-2, P3-3 → P4 → P2-3 → P2-5 → P2-4 → P3-4 → P5.
Small bugs first, then the layout that everything else is drawn into, then the combat readability, then the
larger authoring pieces (Compendium texts, the mechanic banners, the upgrade redesign).

**Every step:** its probe or test, the full suites (Core + bnb-content) where content changes, a commit per
step in the repo it touches. Screenshots of each changed screen go to `~/Desktop/bnb-feedback/`.
