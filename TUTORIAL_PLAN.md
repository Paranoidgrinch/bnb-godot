# Tutorial Plan — the game, shown once and explained

**Status:** started 2026-09-26 at the user's request: "ein tutorial …, wo der spieler alles einmal gezeigt bekommt
und erklaert bekommt".

## Shape

A **short fixed run** on the real content, with a **coach** beside it. Nothing is simulated: the player plays real
fights, a real event, the real shop, and every screen explains itself the first time it appears.

**T1 — Engine (Core): a blueprint can ship a tutorial.** `RunBlueprint.Tutorial` (optional): a fixed `RunMap` and
an optional `RunStart`. `RunBlueprint.ForTutorial()` is the same blueprint walking that map instead of the acts —
same cards, enemies, relics, statuses; no second 11 MB document. Generic: any game can ship one.

**T2 — Content (bnb-content): the walk and the words.** Six rooms of Act I, in order, each with the payout its role
pays in a real run: an easy single-enemy fight → a simple event → a two-enemy fight (targeting, statuses) → the
shop (with gold to spend) → the waiting room → an elite whose rule stands on a plate, paying a relic. The coach's
words live in `Presentation.Game.Extra` under `tutorial:<moment>` (title and text), so the frontend names moments
and the game supplies what is said.

**T3 — Frontend (bnb-godot).**
- A **Tutorial** button on the title screen; a tutorial run is never uploaded, recorded in history or ranked.
- **The coach**: a panel that shows one step at a time — a title, a few sentences, *Next* — and frames the thing it
  talks about in gold. Moments are recognised from what is on screen (the map, a fight's first turn, a card played,
  the reward, an event, the shop, the rest, an elite's rules, the end) and each is explained once.
- The steps can be skipped (*Skip tutorial*) and the run finishes on its own screen with "back to the title".

## Moments (the coach's script, in the order a player meets them)
map · combat.hand · combat.energy · combat.intent · combat.target · combat.block · combat.endturn · combat.statuses ·
combat.relics · reward · event · combat.multiple · shop · rest · elite.rules · compendium · complete
