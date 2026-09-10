# Visual Overhaul Plan — the game stops looking like a test harness

**Status:** proposed 2026-09-10. Runs BEFORE `ACT_IV_V_BUILD_PLAN.md` step **V-7**, at the user's decision:
the full suites and the whole-game gates are worth running against the game that ships, not against the one
that is about to be re-skinned.

**Scope, in the user's words:** the card face (the new master front), the card back (the new master clip),
cards 20 % larger in hand and on the draw pile, an art slot per card and per relic that stays EMPTY for now
but can be filled later by dropping a file in, card pickers that show cards instead of a list, a relic strip
on the right edge drawn as framed squares with hover text, a dark-red-almost-black palette with one accent,
and the hand-card format bug.

**What this plan is not:** it does not change a single rule, number, pool or fight. Nothing here may touch
the engine's behaviour; the one engine seam it asks for (D5) is additive and carries no rule.

---

## What already exists (checked in the code, not assumed)

Four findings decide most of the cost below.

1. **The art slot is already in the contract.** `EntityPresentation.Art` (`Presentation.cs`) is exactly
   "the asset the frontend shows", it is already filled for every card and relic
   (`BlueprintAssembler.cs:168` → `cards/<id>.png`, `:212` → `relics/<id>.png`), it already survives export
   (`docs/godot-export-contract.md`, variant B), and Godot **ignores it today**. The structure the user asked
   for is therefore not built — it is *connected*. **No engine purchase.**
2. **The relic canon maps 1:1 onto the code.** `BnB_Final_Relics_Master_PostAudit_VISUAL_DESIGN_CANON.md`
   numbers all **168** relics 1–168, and the numbering agrees with our pools **exactly**: #1–50 Normal,
   #51–74 Shop, #75–99 Event, #100–168 Boss. Matched by title against `NormalRelics`/`ShopRelics`/
   `EventRelics`/`BossRelics`: **168 of 168, zero mismatches, zero pool disagreements.** The catalogue number
   can be generated mechanically and pinned by a test.
3. **The palette is centralised.** `MoonvineTheme.cs` holds the tokens; only **16** hardcoded colour literals
   exist in the whole frontend (`MapView` 8, `MoonvineTheme` 4, `SessionScreen` 3, `CardVisuals` 1). A palette
   swap is one file plus sixteen call sites.
4. **The reward picker has no identity to draw.** `EntitySelectionRequest`
   (`InteractiveRunSession.cs:342`) carries `Displays` and `Descriptions` — **strings only**. A card reward
   cannot be drawn as a card because the screen never learns *which* card it is. This is the plan's single
   engine seam (D5). The **shop** does not need it: `slot.Entry.Payload` already carries
   `AddCardToDeckRunEffect.Card` / `AddRelicByIdRunEffect.Relic` (`SessionScreen.cs:1541`).

### Two things that are missing and cannot be invented here

- ~~A transparent frame for the NEW front.~~ **Delivered 2026-09-10** and now in the repo as
  `assets/cards/card-frame.png`: the blank master frame, 1053 × 1494, ratio **0.7048** — the same as
  `condemed by decree.png` — transparent in the art window, the rules plaque, the title bar **and** the cost
  badge, opaque on the border and the four corner badges (empty cost circle, broom, crystal ball, scroll).
  The frame is no longer something D1 has to draw.
- ~~A decoder for the new back.~~ **Answered 2026-09-10:** ffmpeg is installed, and the master
  (`BaB-cardback-master.mp4`, H.264, 720 × 1008, **36 s at 30 fps**) is a *different* animation from the one
  shipping (**12 s at 12 fps**) — no frame of the master matches the shipped frame 0 at any phase. See D2.

---

## Phase D0 — the ground: palette and type  ✔ BUILT 2026-09-10

**Deliverable:** `MoonvineTheme` carries a dark-red-almost-black palette and one accent; every one of the 16
literals is either a token or justified in a comment.

**Decided 2026-09-10:**
- Base family (near-black with a red bias): `#080406` page · `#120709` panel · `#1B0A0E` raised ·
  `#2A1014` control · `#3B1419` hairline · `#5C1219` blood.
- **Accent: Antique Gold `#C9A227`** (light `#E9D18A`, dim `#7D6414`).
- **Signal: Amber `#FFD166`** — attack intents, damage, refused plays, the unaffordable state. On a red
  ground red stops being a warning: today's `Danger = e08a8a` sinks into the new panel colour, so the signal
  is its own decision and not a shade of the accent.
- **The relic pool colours are NOT ours to choose** — the canon fixes them (§10.4): Normal = slate gray,
  Shop = copper, Event = pale violet, Boss = dark purple + antique gold. ⚠ **The boss purple runs much darker
  than the canon's word suggests** (`#1C0D2C`, nearly black) at the user's call, and its gold sits a step
  under the UI accent (`#A8861D`) so that a boss frame does not compete with every button on the screen.
- ⚠ **Gold everywhere costs the boss frame its tell.** Once the accent IS antique gold, gold no longer marks
  a Boss Relic; the dark-purple ground and the ivory inner window have to carry that distinction alone.
- **Type: the Godot default stays, with one place to change it.** Decided 2026-09-10. `MoonvineTheme` gains
  a single font hook — a `Font?` that is null today and, when a file is dropped into `theme/`, is set in one
  line and applies to every Label, Button and card face at once. No screen may reach for a font of its own,
  or "change the font later" becomes twenty edits instead of one.

**Done when:** every screen renders in the new ground with no leftover green/yellow, and the screenshot
probes (`--smoke-map`, `--smoke-shop`, `--smoke-event`, `--smoke-reward`, `--smoke-crowd`, `--smoke-boss`)
are captured for review.

### What was actually built (2026-09-10)

`MoonvineTheme.cs` was rewritten around the decided ramp and three call sites were repointed. **Zero
hardcoded colour literals remain outside the theme** — the four that stayed (the map's ember shades) live in
`MapView.RoleColor` and carry the reason they cannot be tokens.

Two things the decision list did not settle, decided here and written into the file:

- **`Danger` and `Warning` were renamed to `Harm` and `Signal`,** because on this ground they stopped being
  the same idea. `Danger` was doing three jobs at once (an error banner, the enemy's own body, a debuff chip)
  and `Warning` two (the energy purse, a card's cost). ⚠ **Red is no longer a warning here**: on a red page a
  red alert sinks into the panel behind it, so *amber* is the colour that means "pay attention" and red keeps
  the narrower job of flesh and damage. Anything that wants to shout, shouts in amber.
- **The default panel border became the hairline, not the accent at 30 %.** Every panel in the game used to
  be outlined in the accent, which made a screen where everything looked equally pressable. A gold frame now
  *means* something and a caller that wants one passes it.

Consequences of the ramp that had to be chosen rather than derived:

| decision | why |
|---|---|
| Text warmed to `#F3E9E1 / #D6C4BA / #9B877F` | a pure grey on a red ground reads as a grey nobody picked |
| Health bar = `Harm` on `BgRaised` | a bar is a MAGNITUDE, not an alert, so it may keep the red the warnings gave up — and it is the same bar for hero and enemy, because health is health |
| Hero figure gold, enemy figure `Harm` | the one place in the game where "who is this" must read in a tenth of a second |
| Rarity ramp bone → pale violet → violet, **never gold** | gold now means "you can touch this"; a rare card wearing the button colour reads as a button. And the ramp runs quietly: most of a hand is common or uncommon, so only RARE is allowed to be a colour |
| Map legend rebuilt as a sentence | rust → hotter rust → hot → hottest for the four fight kinds, gold sells to you, violet surprises you, steel mends you, copper builds for you. The four ember shades are the only non-token colours left in the frontend, and they exist to be told apart *from one another* on a shrunken map — a job no single token can do |

**The font hook is `MoonvineTheme.FontPath`** — one `static readonly string?`, null today. Set it to a
`res://theme/…` path and `Build()` writes the face into the Theme's *default* font, which every Label,
Button, RichTextLabel and card face inherits. A missing file warns and falls back rather than crashing.
⚠ No screen may load a font of its own; the first one that does turns "change the font later" from one edit
into twenty.

**Probes captured:** `--smoke-map`, `--smoke-shop`, `--smoke-event`, `--smoke-reward`, `--smoke-crowd`,
`--smoke-boss 5 --boss inanna`. The tooltip audit is unchanged (0 named-but-unexplained controls).

---

## Phase D1 — the card face  ✔ BUILT 2026-09-10

**Deliverable:** `CardVisuals.Face(...)` — one card widget, built from the master layout, used by hand, deck,
pickers, shop and the deck list. `CardBlockButton` in `SessionScreen` becomes a thin caller.

- **Geometry, MEASURED off the frame's own alpha channel** (not estimated). Ratio **0.7048**; every field is
  a fraction of the card, so one layout holds at every size:

  | field | x | y |
  |---|---|---|
  | art window | 0.0437 – 0.9554 | 0.1419 – 0.7202 |
  | rules plaque | 0.0437 – 0.9554 | 0.7416 – 0.9639 |
  | title bar | 0.0456 – 0.9525 | 0.0268 – 0.1218 |
  | cost badge | centre (0.0679, 0.0476) | r ≈ 0.034 of width |

  ⚠ The title bar's transparent band runs the **full width**, under the cost badge on the left and the broom
  badge on the right. The title TEXT is therefore inset to roughly x 0.12–0.88; the band is the hole, not the
  text box.
- **Size: +20 %.** `CardW/CardH` 112 × 156 → **134 × 190** (which also corrects the ratio from 0.718 to
  0.705). Nine call sites (`grep CardVisuals.Card[WH]`). The hand row and the deck pile both read them, so
  both grow together, as asked.
- ⚠ **The format bug, and the prime suspect.** In `BuildHand` a card face is given
  `face.Size = (CardW, CardH)`, but Godot **clamps a Control's size up to its combined minimum size** — and
  the one child whose minimum is unbounded is the **name Label** (`AutowrapMode.WordSmart`, no fixed window).
  How many lines a title wraps to depends on the width at measure time, and clicking a card calls `Rebuild()`,
  which measures it again from scratch. The rules text already got the fix — a fixed-size `Control` window
  with `ClipContents` (`SessionScreen.cs:2318`, and the comment there tells the whole story). The title never
  did. **Every field of the new face lives in a fixed window; nothing on the card may report a minimum size.**
  Reproduced and confirmed before it is called fixed, not after.
- **The hover survives, unchanged.** `Glossary.Explain(...)` stays on the panel AND on the click overlay, and
  `--smoke-tooltips` (which counts controls that name something the glossary knows while offering no hover)
  must not get worse. It is the acceptance test for "the hint text still appears".
- **The art window is empty on purpose** — it draws the ground plus, in the corner, the card's art code
  (D3), so an unfilled card is *obviously* unfilled and its code is readable on screen.

**Done when:** `--smoke-draw`, `--smoke-target`, `--smoke-crowd` and `--smoke-boss 5` capture a hand of the
new faces; clicking a card ten times changes nothing about its size; `--smoke-tooltips` reports no new mutes.

### What was actually built (2026-09-10)

`CardVisuals.Face(CardFace, onClick, scale)` renders one card; `CardBlockButton` in `SessionScreen` now only
decides what is TRUE of a card (name, price, payable, marks) and hands it over. The frame is laid over the
fields, so frame and text can never disagree about where a field is. Card 112 × 156 → **134 × 190**.

**The format bug was real, and it was not what the plan predicted.** `--smoke-format` was written first and
run against the OLD face, which is the only reason we know: a card did not drift across clicks, it was
**never the size it was handed at all**. Every card came out **136 × 169** — 24 px too wide, because the
`PanelContainer`'s stylebox adds 12 px of content margin on each side to the combined minimum — and *Cower
Behind a Desk* came out **136 × 192**, another 23 px taller than its neighbours, because its title wrapped to
two lines. A row of cards that are neither the same shape nor the shape asked for, stepping 124 px apart at
136 px wide and hanging out of the row into the End-turn button, is what "the card changes format when I
click it" looks like from the player's side. After: **134 × 190 on every slot, ten clicks apart, PASS.**

The fix is structural, not a tweak: the root is a **plain `Control`** (its combined minimum is exactly its
`CustomMinimumSize`, whatever it holds — only Containers propagate a child's minimum upward), every field
sits in a fixed clipped window anchored by fraction, and the frame `TextureRect` is `IgnoreSize` so it does
not report the artwork's own 1053 × 1494.

**Four things the screenshots taught that no amount of reading would have:**

| what | why |
|---|---|
| ⚠ **the card ground had to become LIGHT** (`CardGround` `#0a0507` → `#241015`) | the master frame is black tracery with a few silver highlights; on D0's near-black card ground the frame was simply *not there*. A card is legible because its FIELDS are lit and the frame is the dark border around them. This is the one place the ramp runs the other way, and the reason is written into `MoonvineTheme`. |
| the ground is a **rounded** Panel, not a ColorRect | a lit square behind a frame with 7.4 %-radius corners shows as four bright nubs past the artwork. Everywhere else the frame covers its own ground. |
| **mipmaps, on both sides** | 1053 px of filigree shrunk to 134 px without mipmaps samples one pixel in eight: the frame came out as glitter. `mipmaps/generate=true` in `card-frame.png.import` **and** `LinearWithMipmaps` on the TextureRect — either alone does nothing. |
| ⚠ **measure with the spacing you will draw with** | `GetMultilineStringSize` asks the *font*; a `Label` then adds the theme's `line_spacing` (3 px) between lines, so a paragraph measured to fit exactly lost its last line to the clip. The rules Label sets that constant to 0 and `FitBlock` measures the same block. |

**Type fits the field instead of the field fitting the type.** `FitLine`/`FitBlock` pick the largest size at
which the name fits the band and the rules fit the plaque. A name gets QUIETER, never re-wrapped — wrapping
is what used to change the card's shape. What still does not fit is clipped with an ellipsis and the hover
has it whole. The longest names in the game (*Break the Great Seal of Execution*, 33 characters) do reach the
ellipsis at the floor size; a condensed typeface would buy them back, and that is exactly what
`MoonvineTheme.FontPath` is for.

**Two decisions the phase forced:**

- **The ring takes a number and nothing else.** The cost hole is 6.8 % of the card's width — a glyph and a
  digit do not both fit — so `CostBadge` prints the amount and the hover says what it is denominated in.
  Every card in the game is priced in energy today; the hover is where it stays honest if one is not.
- **The mark chips sit ABOVE the click overlay**, or the overlay swallows the hover that explains them — so
  each chip is itself a button that plays the card, and the card behaves the same wherever you click it.

**The hand is held, not shelved.** Up to five cards it is a plain row at a full gap; past that the step
closes and the cards overlap left-under-right, and the whole row leans into a shallow fan — a fixed 9°
*spread* shared out, not a fixed angle per card, so a hand of twelve leans no further than a hand of four, it
just leans in smaller increments. An overlap without the lean reads as a layout that ran out of room; with it
it reads as a hand of cards. ⚠ And the hand now draws **in front of the deck**: the draw pile is added to the
combat root after the hand column, so by tree order it lay over the leftmost card — a card dealt underneath
the deck it came out of. (In front is fine, the player said; behind is not.)

**The art slot is already wired** (the structure, not the pictures — the files are D3): `assets/cards/art/<id>.png`,
looked up and cached by id, and until one exists the window is an empty socket with the card's code printed
in it, so an unfilled card is obviously unfilled and the person painting it can read which one it is off the
screen.

**Probes:** `--smoke-format` (new, 5/5 slots exact, 0 off over 10 clicks) · `--smoke-crowd` · `--smoke-draw` ·
`--smoke-target` (block plays, attack arms — the targeting rule survived the rebuild) · `--smoke-tooltips`
(43 labelled controls, 28 with a hover, **0** naming something the glossary knows with no hover) ·
`--smoke-boss 5 --boss inanna`.

---

## Phase D2 — the card back  ✔ BUILT 2026-09-10

**Answered 2026-09-10: the clip is genuinely new, and ffmpeg is installed.** The master is
**36 s at 30 fps (1080 frames)**; what ships today is **12 s at 12 fps**, and no frame of the master matches
the shipped frame 0 at any phase (mean channel difference stays ~32/255 across the whole 36 s). The artwork
is different too — the new back is a *paperwork maelstrom*: documents, seals and ledger sheets spiralling
into a black vortex inside an ornate frame with violet corner gems.

1. mp4 → `.ogv` (Theora, 720 × 1008; keep 30 fps unless the CPU cost says otherwise — Godot decodes Theora
   on the CPU and `CardVisuals` already limits how many backs animate at once) plus a still poster PNG at 2×
   for the fanned backs. Both files replaced.
2. The back's own frame colour comes from D0 so the deck pile sits in the new palette.
3. ⚠ The back is violet-accented art next to a gold-accented UI. That is fine — it is a picture, not chrome —
   but the deck pile's own frame must be gold, or the corner reads as a second accent.

⚠ `CardVisuals` already limits how many backs animate at once (Godot decodes Theora on the CPU) — that stays.

### What was actually built (2026-09-10)

**The clip is not a picture that needs a frame. It is a whole card.** The plan's third point asked for a gold
frame around the deck pile, on the reasoning that the back is violet-accented art next to a gold-accented UI
and the corner would otherwise read as a second accent. Then somebody looked at the artwork — the same move
that saved D1 — and the premise was wrong: the master already carries its own ornate border, corner
medallions and all, with the card's rounded silhouette cut into the picture and plain black outside the
round. A gold ring drawn around that is not an accent, it is a picture in the wrong frame, and the old
wrapper would have done worse than that: `Framed()` put the back inside a bordered `PanelContainer` of card
ground, so a *square* of lit ground behind *rounded* art would have lit four nubs at the corners — the exact
bug D1 fixed on the front, arriving from the other side. So `Back()` now returns a plain `Control` with the
picture at full bleed and no chrome at all, `Framed()` is gone, and the gold this corner owes the rest of the
screen is paid by the pile's count instead. `Back(bool animated, float phase)` also lost `phase`: no caller
ever passed it.

**⚠⚠ A video texture cannot be mipmapped, and that decides the encode.** D1's lesson was that 1053 px of
filigree drawn at 134 px turns to glitter unless mipmaps are on in *both* the `.import` and the
`TextureFilter`. The back is the same filigree with the exit welded shut: a `VideoStreamPlayer` rebuilds its
texture every tick, so there is no chain to build and no `LinearWithMipmaps` to reach for. The only remaining
lever is the encode itself, so each rendition is cut to the size it is actually drawn at:

| | cut to | why |
|---|---|---|
| `card-back.ogv` | **134×190**, 30 fps, Theora `-q:v 7`, 2.63 MB | 1:1 with the card, so it is never resampled at draw time at all |
| `card-back.png` | **268×380**, mipmaps **on**, 178 KB | a still *can* carry a chain, so the poster keeps 2× of headroom |

Measured against an ideal offline Lanczos reduction of the master, the moving back lands 3.73/255 away and
the still 3.28 — and 5.00 from each other, which is the seam inside the pile and is invisible, because a
still only ever shows as a 4 px sliver under the top card. The alternative, a 2× clip resampled down by the
GPU, cost **20.6 MB** to land at 3.56: worse than the poster and eight times the file. ⚠ Both renditions are
cut from `BaB-cardback-master.mp4`, which is **not in this repo** (34 MB); if `CardW`/`CardH` ever move,
re-cut both:

```
ffmpeg -i BaB-cardback-master.mp4 -an -vf "scale=134:190:flags=lanczos" -c:v libtheora -q:v 7 -r 30 assets/cards/card-back.ogv
ffmpeg -i BaB-cardback-master.mp4 -an -vf "scale=268:380:flags=lanczos" -frames:v 1 assets/cards/card-back.png
```

Theora encodes in 16×16 macroblocks and 134 is not a multiple of 16, so the encoder pads to 144×192 and
writes a crop region. Godot honours it — the probe logs carry no decoder warning and the edges are clean —
but that was a real risk of cutting to the card's size and is the thing to check first if the back ever
shows a garbage edge.

**The whole 36 s ships, because the clip has no shorter period.** Frame 1079 is 3.50/255 from frame 0 — the
seam of a clean loop — while every interior sample (6 s, 12 s, 18 s, 24 s, 30 s) sits at 19–22. There is no
half-turn to cut to; shortening it would put a visible jump in the deck corner. Against the old back that is
3× the duration at 2.5× the frame rate for 0.4 MB more, and the poster came down from 1.32 MB to 178 KB, so
the phase is net **+0.3 MB**.

**⚠ A pile's footprint is its ink, not one card.** Making the count gold made a defect visible that had been
there since the pile was built: `SetAnchorsPreset(BottomWide)` was being applied to a `Label` that had not
been laid out yet, so its anchor rect was zero-high, its minimum height pushed it out of the bottom of the
holder, and "Draw N" printed 2 px from the edge of the window, below the pane's own hairline. The holder now
measures the stack's whole lean plus a caption band of its own, the cards are laid from the bottom of the
lean upward so nothing is drawn above the footprint, and the count sits in an explicit 26 px band —
clearance measured back off the screenshot: **31 px**.

**Probes** (all on the shipped binary): `--smoke-format` 5/5 exact PASS · `--smoke-tooltips` 43/28/**0**,
unchanged from D1, so the back's new `MouseFilter.Ignore` cost no hover · `--smoke-draw` clean (the flip's
cover is a still back) · `--smoke-crowd` `enemies=3 … offscreen=no error=none` · `--smoke-boss 5`.

---

## Phase D3 — the art slots, and the table the user fills  ✔ BUILT 2026-09-10

**Deliverable:** every card and every relic has a **stable, unique, human-readable code**, the frontend looks
for a file named by it, and finding nothing is normal.

**Decided 2026-09-10: the id IS the code.** `levy_stamp.png`, `condemned_by_decree.png` — no minted
`R-###` / `C-B-###` scheme. A filename that says what it is beats one that has to be looked up, and
`Presentation.Art` already carries exactly these paths, so nothing has to be generated at all.

- **Where a file goes:** `assets/cards/art/<id>.png`, `assets/relics/art/<id>.png` — the paths
  `BlueprintAssembler` already writes. Godot resolves `Presentation.Art` → path; a missing file draws the
  empty window with the id printed small in it, so an unfilled card is obviously unfilled and names its own
  file. Dropping a PNG in is the entire act of filling a slot — no rebuild, no registry edit.
- **`ART_SLOTS.md`**, generated: every id, title, pool/act, and for relics the matching canon number and its
  brief line, so the catalogue can still be worked down while generating images even though the number is not
  in the filename.
- An upgraded card (`levy_stamp+`) falls back to its base card's art unless its own file exists.

### What was actually built (2026-09-10)

**The contract's path IS the path on disk.** The plan proposed `assets/cards/art/<id>.png` and a Godot-side
scheme to build it. There is no need for one: `Presentation.Art` already reads `cards/levy_stamp.png` for
every card and `relics/levy_stamp.png` for every relic, written by `BlueprintAssembler`, and it survives the
export contract. So the frontend resolves that string under `res://assets/art/` and invents nothing — the
folders are `assets/art/cards/` and `assets/art/relics/`, and the document's own words name the file. What is
left over is a two-step fallback for anything that arrives without a declaration (`<kind>/<id>.png`, then the
base card), and a cache that remembers MISSES too, because finding nothing is the normal state and must not
cost a filesystem call per redraw.

**413 cards ask for 254 pictures.** An upgraded card is drawn from its base card's file: an improvement
changes what a card DOES, not what it is a picture of. The authored cards already said so (`TrimEnd('+')` in
the assembler); the 25 ported v2 upgrades did not — they asked for their own `<id>_plus.png` — so one line
changed and the document now declares exactly **464 distinct pictures**: 254 cards + 210 relics. That number
is not maintained anywhere; it is the count of distinct declared paths, and `ArtSlotTests` pins it.

⚠ **A dropped file is invisible until Godot has imported it.** "Dropping a PNG in is the entire act of
filling a slot — no rebuild" was half true: `res://` holds what the importer has scanned and nothing else,
so a file that was merely copied into the folder does not exist for the running game. The editor imports on
focus; a headless run never does. Measured: with the file in place and no import, `--smoke-art` reported it
missing; after `godot --headless --path . --import`, the same probe found it and the card drew it. That one
command is `tools/import-art.sh`, and it is named in the README, in `assets/art/README.md` and in the
generated table.

**Every relic already has its brief.** The two visual-design canons
(`BnB_Final_Relics_Master_PostAudit_VISUAL_DESIGN_CANON.md` #1–168 and
`BnB_Elite_Relics_MASTER_AND_VISUAL_CANON.md` #169–210) cover **210 of 210** relics with **zero** title
mismatches against the code, so `ART_SLOTS.md` carries the canon number and the object line for every one of
them. Cards have no visual canon and carry their own rules text instead.

⚠ **AND IT IS IMPORTED WITHOUT MIPMAPS.** D1's glitter trap has a third door: Godot's default for a new
texture is `mipmaps/generate=false`, and every picture in this game is drawn far smaller than it is painted
(488 × 440 into a 122 × 110 window). Nobody dropping a file in would think to open its `.import` to find out
why the result sparkles, so the project now carries an `[importer_defaults] texture={"mipmaps/generate": true}`
block — it applies to NEW imports and leaves the files that carry the setting themselves alone. The drawing
side was set too (`TextureFilter.LinearWithMipmaps` on the card's picture), because D1 already proved that one
half without the other does nothing.

⚠ **AN AUTOWRAPPING LABEL'S MINIMUM WIDTH IS ONE CHARACTER.** Found by looking, not by reasoning: with the
relic pictures dropped in for the test, the sidebar printed "Crossed-Out Map" down the edge one letter per
line. An `HBoxContainer` gives every child its minimum and shares out only the remainder among the children
that asked to expand — the name had not asked. This is D1's container lesson from the other side: there, a
child's minimum pushed a parent open; here, a child that reported almost no minimum got almost nothing. One
`SizeFlags.ExpandFill`.

**`ART_SLOTS.md` is generated and pinned.** `dotnet run --project Converter -- --art-slots ART_SLOTS.md`
(in bnb-content, where the data and the canons live). It is one row per PICTURE — code, title, pool, rarity,
brief — grouped by relic pool and by card sheet, and it marks the **25 ported v2 remnants** as work not worth
doing, so the real list is **439**. A test regenerates it and fails if the checked-in file has gone stale,
because a slot table that quietly drifts is worse than none.

### D3a — closed before this phase started

The audit above was written against the code of 2026-09-10 morning; the relic arc closed it the same day
(bnb-content `3ff4318`). Re-checked faucet by faucet, not taken on trust: `EventTemplates.Treasure` now draws
`pools.NormalRelicOnTheCurve`, the elite/boss/mimic rewards draw their own authored pools, a shop samples
`depth: pools.ShopRelicStock.Count` so all 24 shop relics are reachable, `BlueprintAssembler` ships
`FinalRelics.Compile()` and nothing else, and the document holds **210 relics with 210 presentations and no
strays in either direction**. `ConversionPools.RelicGrantSource` — the function that was never re-pointed —
still compiles but has no caller left.

### What the regeneration uncovered: the shipped document had no rarity curve

The document had to be rewritten for 25 art paths. Diffing the old one against the new one — the habit, not a
suspicion — turned up **9,939 changed weights** that had nothing to do with art. The act rarity curve the
relic arc wrote on 2026-09-10 (`ConversionPools.RarityCurve`, `ActCurve`) lives in the code and in its tests,
but the checked-in `game.roguedeck.json` predates it: every reward pool in the SHIPPED game was still a
uniform draw, and bnb-godot's copy is a copy of that file. **A curve that exists only in the converter is not
in the game.**

What actually moved, measured rather than assumed: pool MEMBERSHIP is identical (11,291 offer entries, **0**
differing), the weights carry the curve, and the entries are re-ordered because the curve groups them by
rarity class. Everything else in 23,888 leaf differences is the 25 art paths. No key exists on one side only.

It is also why the Act-V boss probe now reads `round=6` and `133/120` where D2 read `round=7` and
`152/138`: the same seed meets a differently-weighted deck. Gates re-run against the refreshed document:
`--maps 2` clean (no `PROBLEM` line), bnb-content 1469/1469, and every screen probe below. `--playtest 2` walks **1 victory and 1 defeat** — seed 20260718 dies of
attrition in Act IV. That is not this phase's doing and was proved so rather than assumed: with the one
converter line reverted the same walk fails identically, 82 rooms and 398 steps. The sparring ring puts the
fatal encounter at ~185 HP against a starter deck, so it is the act that kills, not the room. **V-7's first
gate has found its first customer**, and it belongs to V-7.

⚠ **A probe that runs while another experiment writes into `assets/` is not a probe.** The first Act-V boss
run was launched into the background and the relic-picture experiment then dropped 210 files into
`assets/art/relics` while it was still walking — a running Godot resolves `res://` off the disk, so that run
saw pictures appear underneath it. Its numbers were thrown away and it was walked again on the clean tree,
where it returned the same three numbers — which is the answer the throwaway was for. The standing rule
(*run the long probes one at a time*) is about more than CPU.

### The probes

`--smoke-art` is the new one and it cannot fail on a count — an unfilled slot is the normal state, so it
REPORTS: `464 slots asked for (254 cards, 210 relics), 0 filled, 0 upgrade(s) asking for a picture of their
own`. The last number is the rule that keeps the first honest, and the same three numbers are how the user
will watch the work go down. Acceptance, all on the final binary: `--smoke-format` 5 slots, 0 off over 10
clicks — PASS · `--smoke-tooltips` 43 labelled controls, 28 with a hover, **0** mutes (digit for digit D1's
and D2's) · `--smoke-draw` clean · `--smoke-crowd` `offscreen=no error=none` · `--smoke-boss 5` (Inanna, 7
rounds, 4 plays, seed 4) `act=5 boss=yes round=6 ended=it found what it came for error=none` with
`133 labelled controls, 120 with a hover, 0` and no ERROR line. bnb-content **1469/1469** with four new
`ArtSlotTests`.

### D3a — the diagnosis, as it was written that morning

The audit this phase asked for was run against the spec, and it did not come back clean. **The old v2 relics
did not "slip into the data" — one function was never re-pointed, and every faucet that calls it still hands
out the pool that existed before the final one did.**

**What `BnB_Final_Relics_Master_PostAudit.md` §1 says.** There is no per-act relic layer at all: the 50
Normal relics are one global pool for Acts I–IV, reachable from four faucets — standard random relic rewards,
**Treasure** rewards, the normal-relic slots in shops, and events that award a random Normal relic. Boss
relics are 1-of-3 on the kill and appear nowhere else. Shop relics are the shop inventory plus three named
market events. Event relics hang on named branches.

**What the code does, faucet by faucet:**

| faucet | draws from | verdict |
|---|---|---|
| Elite / Boss / Mimic victory (`MapSpecBuilder.VictoryRewards`) | `RelicGrantSource(null, …)` | ❌ ported v2 list |
| Treasure chest (`EventTemplates.Treasure`) | `RelicGrantSource(null, …)` | ❌ ported v2 list |
| Shop, normal-relic shelf (`EventTemplates.Build`) | `pools.NormalRelicStock` | ✔ canonical 50 |
| Act-IV doors that award "a random Normal Relic" | `NormalRelicOfRarity` | ✔ canonical 50 |
| Boss kill, forced 1-of-3 (`BossRewards`) | `BossRelics` | ✔ canonical 69 |
| Event branches | `EventRelics` | ✔ canonical 25 |

**The single cause.** `ConversionPools.Relics` is `data.Relics` — the **ported v2 JSON relics**, filtered only
to "not boss rarity" and "class-eligible". `RelicGrantSource` reads it (its own error message still says
*"no event-eligible relics"* — it was written for event awards), and the map layer and the treasure chest both
call it. The canonical pools arrived later as `NormalRelicStock` / `ShopRelicStock` and were wired only into
the shop shelves and, at IV-22, into `NormalRelicOfRarity`.

**What that costs, measured in the shipped document:** every Elite, Boss and Mimic reward in Acts I–IV offers
a pool of **49 relics — 47 ported and 2 canonical**. The 2 are `archive_key` and `emergency_inkwell`, the only
final relics whose ids collide with a ported one, so the final version replaced them in place
(`BlueprintAssembler.cs:110` keeps every ported relic whose id does *not* meet a final one). The other 48
authored Normal relics never enter that pool. Act V correctly grants none.

**A second, smaller gap.** A shop's shelves are sampled at CONVERSION time — `Relics(pools.ShopRelicStock,
rng, depth: 5)` stores 5 of the 24 shop relics per shop, and a reroll only turns over inside that stored
depth. Across the four shops in the shipped document, **12 of the 24 shop relics are never named at all**:
`backroom_kettle`, `bent_auction_gavel`, `bounty_hook`, `copper_receipt_roll`, `guest_favor_token`,
`indemnity_stamp`, `notarys_waiver`, `priority_window_pass`, `scriveners_shears`, `turnover_bell`,
`warranty_tag`, `witchmarket_purse`. The spec says all 24 are eligible.

**The fix:** point the Elite/Boss/Mimic and Treasure sources at the canonical Normal pool with its authored
rarity weights, delete the 47 ported relics and their presentations, and deepen or rotate the shop sample so
all 24 shop relics are reachable. One test per faucet that proves *a run can actually reach this pool* —
the Act-III lesson written down as an assertion instead of a memory.

⚠ **This is content, not paint: it changes what a run hands out.** It belongs here rather than in V-7 because
a relic shelf drawn over a pool of 47 things that have no canon entry, no art brief and no file name is a
shelf that documents a bug — and because V-7's first real balance numbers should be measured against the
relics the game is supposed to have.

## Phase D4 — the relic strip  ✔ BUILT 2026-09-10

**Deliverable:** the right edge stops being a bullet list. Relics are a wrapping grid of small framed
squares; hovering one explains it.

- One square per relic: the pool's canon frame colour (D0), the art if a file exists, otherwise the code.
- **Hover = the relic's own words** — `Presentation.Relics[id].FlavorText` through `Glossary.Explain`,
  exactly what the list shows today (`SessionScreen.cs:2512`), so nothing is lost in the move.
- Disabled relics (`relic.Enabled == false`) stay visibly present but dimmed — "(off)" is information.
- Consumables get the same treatment for free; the deck list stays a list (it is a list of many, not a shelf).
- The strip must survive **69 relics on one screen** (an Act-V run can hold a lot): it wraps and scrolls, and
  a probe proves it at a hostile count rather than at four.

### What was actually built (2026-09-10)

**The one thing the plan did not have: the pool never left the converter.** The frame is not a decision the
frontend can make — the canon fixes it per pool (§10.4) and the elite canon adds the sixth (§2) — but a
relic arrives in Godot as an id, a display name and a rules line. `Presentation.Relics[id]` carried `Art`,
`FlavorText` and `Rarity`, and rarity is *not* the pool: a Common relic can be a shop relic, and the shelf
would have been drawn in the wrong six colours with nothing anywhere to notice. **`Frame` is the export
contract's own slot for exactly this** ("card-frame / border style"), the engine ignores it, and the pool
name is the value because in this game the pool IS the frame. One line in `BlueprintAssembler`, and the
regenerated document differs from the old one in **210 leaves and nothing else** — every one of them a
`Presentation/Relics/*/Frame` going from `null` to a pool name, 0 keys added, 0 keys removed, 0 other values
touched. (The diff was taken because D3's rarity-curve finding made it a habit, not because anything was
suspected.) `RelicFrameTests` counts every pool across the seam: 69 boss · 50 normal · 38 elite · 25 event ·
24 shop · 4 mimic.

**The tile.** `CardVisuals.Tile` is the card face's construction reused for a much smaller object: a plain
`Control` root that reports nothing but the size it was handed, a `StyleBoxFlat` ground carrying the pool's
frame, and one clipped window holding either the picture or the code. 50 px — five to a row in the 320 px
sidebar with room for the gaps and the scrollbar, and still large enough that a painted object will read.
The six frames, measured off the screenshot pixel by pixel rather than eyeballed:

| pool | ground | frame | canon |
|---|---|---|---|
| normal | `#1b0a0e` | `#6f7480`, 1 px | thin slate-gray, "frame quiet" |
| shop | `#1b0a0e` | `#b87333`, 1 px | copper, slightly heavier than Normal |
| event | `#1b0a0e` | `#b98fd0`, 1 px | pale violet, "the pool identifier" |
| boss | `#1c0d2c` | `#c9a227`, 2 px **doubled** | dark purple + antique gold, epic |
| elite | `#1c0d2c` | `#a8861d`, 1 px | *a plainer boss frame* — one gold line, gold a step down |
| mimic | `#1c0d2c` | `#a8861d`, 1 px | drawn in the elite canon; four grades, one object |

⚠ **The Boss frame had to stay double at 50 px.** Gold is now the UI accent, so gold alone no longer says
"boss" — the canon's own warning — and the whole boss/elite relationship is *double line vs one*. Drawn hard
against the outer line a second rail is not a second rail, it is a thicker one, so it is set one tile-width
in at half weight. That reads as two rails at a glance and still leaves the window open.

⚠⚠ **A SCROLLCONTAINER HANDS ITS CHILD A MINIMUM, NOT A WIDTH.** The third door into the same trap as D1's
card minimum and D3's one-letter-per-line relic name. An `HFlowContainer`'s minimum width is one tile, and a
`ScrollContainer` that is allowed to scroll sideways gives its child exactly that — so the shelf would have
come out as a single column 69 squares tall, inside a sidebar that scrolled sideways to show it. Switching
the sidebar's horizontal scrolling off is what makes the scroll stretch the shelf to the panel and lets it
wrap. The probe prints `~N/row` and says **ONE COLUMN** in as many words if it ever regresses.

⚠ **A block that fits by height can still be too wide.** An empty socket shows the slot code, and the ids
are written as words (`conservators_thread`), so the tile sets them as words — an underscore is a line
break. `FitBlock` asks whether the wrapped paragraph is short enough, and wrapping happens *between* words:
a single word longer than the box never wraps, overflows sideways and is clipped to `onservato`. The size
used is the smaller of two answers — the one that fits the block's height and the one that fits the longest
word's width. Caught by looking at the first screenshot, not by reasoning.

**The hover carries the name and the rules**, which is what the list carried, through the same
`Glossary.Explain` — plus "(off)" for a relic that has been switched off, and that one is dimmed as an
OBJECT the way an unaffordable card is, because it did not leave the shelf, it stopped working.

### The probe

`--smoke-shelf` puts the relics on rather than walking for them: what is being measured is the layout, and a
layout does not care how a relic was earned. One from every pool before a second from any, so all six frames
are on the shelf at any count — a strip filled with 69 boss relics would look right and prove that one frame
works. Every seventh is switched off. At the hostile count:

```
smoke-shelf: worn=69 (asked 69, 9 switched off) tiles=69 rows=14 ~4/row
             right=1238/1260 outside=no content=995 viewport=494 scrolls=yes error=none
```

and at a count a real run actually reaches (`--shelf 6`): `rows=2 … scrolls=no`, with the deck list under it.

---

## Phase D4a — the window, the regions, the bodies  ✔ BUILT 2026-09-10

**Commits:** bnb-godot `32f7fa6` (the window) · `2f27db4` (the boss's name) · `f0cbd40` (the pile and the
regions) · `ea31720` (a body draws) · bnb-content `93204bd` (the body slots). Core untouched.
**Green at the end:** bnb-content 1477/1477 · `--smoke-deck` PASS · `--smoke-format` PASS · `--smoke-art`
733 slots · `--smoke-shelf` 69 tiles / 14 rows / outside=no · `--smoke-crowd` offscreen=no ·
`--smoke-boss 5` `act=5 boss=yes round=7 error=none` with tooltips 136/122/**0**.

⚠ **Left open, and it belongs to D6:** in Act V the arena region is shorter than a full combatant column
(30 heading + 104 rule + 292 bottom leave 238; the column wants about 290), so it scrolls and the hero's
health bar is cut across. It did exactly this before D4a — the regions did not cause it, they make it a
number one can change (`DivineBand`, `BodyHeight`, `HintBand`/`ControlBand`).

Not in the plan when it was written; asked for after D4 was reviewed, and all of it standing between the
screen as it is and any further work on it. Five things, four of them small and one of them structural.

### The draw pile's clip was restarting, and it was measured before it was fixed

The report was "there is a bug with the animation on the card stack". What it was could not be guessed from
the code, so the draw probe was turned into a burst — fourteen frames, then a card played, then six more —
and each frame asked the tree which `VideoStreamPlayer` it was looking at and where in the clip it stood:

```
players a13: 46489667569@1.59      ← before a card was played
players b0:  56103011972@0.00      ← after: a DIFFERENT object, back at the start
```

⚠ **A CLIP THAT IS REBUILT NEVER PLAYS.** `Rebuild()` frees every child of the combat root on every state
change — a card played, an enemy acting, a card drawn — and the pile was one of them. Its animated back is a
video: freed and made again, it starts at 0.00 every time, so a 36-second turn of the card back never got
past its first tenth of a second and the artwork visibly snapped back on every click. D2 measured that clip
frame by frame and cut it to the pixel; none of that was ever on the screen.

The pile is now built ONCE per fight and updated in place (visibility, position, the count), and freed when
the fight is. `--smoke-deck` plays three cards and reports whether the clip is the same player and whether
its position moved: `players=1 same-clip=yes advanced=yes from=0.03 to=1.24 PASS`. Its footprint is fixed at
the full lean too, so a thinning pile no longer moves house — it thins from the top card down, which is where
a dealt card comes off a real one.

### "(empty hand)" is gone

A muted label in a zero-width holder, so it printed one letter per line down the left of the pane. An empty
hand is already legible as an empty hand.

### The boss room says who it is, one hover away

`MapView` already named boss rooms — but only in a gauntlet act (several boss rooms, where the order IS the
act's shape), on the principle that finding out who ends an ordinary act is part of walking it. The room
still says "Boss"; the **tooltip** now always carries the name, because a player choosing a path is choosing
who to meet.

⚠ **THE BOSS ROOM IS A DISABLED BUTTON** — it is not on the fork, so it cannot be clicked, and "the tooltip
text was set" is not the claim that matters. `--smoke-map` now scrolls it into view, puts a pointer on it and
photographs the result (`smoke-map-boss.png`: *The Queue Commissioner — The one who ends the act.*).
⚠ And the first two attempts at that measured nothing: **`Input.WarpMouse` moves the cursor but delivers no
motion event to a window that does not have focus**, so the probe asked who was hovered and heard "nobody" —
about an enabled control as much as a disabled one. A synthesised `InputEventMouseMotion` through
`Input.ParseInputEvent` is what a probe has to use; both controls then reported `hovered=yes`.

### The window is a setting, and the pane is regions

**The standard Godot answer, not a new one.** The project declares one design canvas (1280 × 720) and a
stretch mode (`canvas_items`); every coordinate in the frontend is a unit of that page and the engine scales
the page to the window. A card is 134 × 190 at 1280 × 720 and at 3840 × 2160 alike, and nothing in the game
multiplies anything by a scale factor. The aspect is **`expand`**: a window that is not 16:9 gets a WIDER
page rather than black bars, so an ultrawide screen hands the extra room to the combat pane.

`DisplaySettings` (`user://settings.cfg`) carries window mode (windowed / borderless / exclusive fullscreen),
size, V-Sync, and an **interface scale** on top of all of it (`ContentScaleFactor`) — which is a different
thing from the window size and both are wanted: a bigger window shows the same page larger, a bigger scale
makes the page itself bigger and the screen hold less of it. Reachable from the title screen and from **Esc**
inside a run. ⚠ A probe's window is not the player's — the window half is skipped under `--smoke*`/`--sim`,
or a stored 4K fullscreen would silently invalidate every screenshot number this plan has taken.

**The pane is six fixed regions** (heading · divine rule · arena · hand · controls · the pile's corner),
declared as constants in one block and anchored, replacing the VBoxContainer that used to hold them. A
container hands out its children's MINIMUM heights first and the leftovers afterwards, which is why an Act V
boss with a rule panel and a dozen statuses ate the arena's share and cut the hero's own health bar in half
(visible in `smoke-boss5.png` from D4). Regions cannot do that to each other: what overflows one scrolls
inside it. The hand no longer lies across the pile it was dealt from, either — the pile's corner is a region
and the hand's starts where it ends.

`--smoke-window` resizes the real window under a real fight and reports the pane in CANVAS units:

```
window=1280x720  canvas=1280x720  arena=880x334@66  hand=880x214  cards@195 deck→186 clear
window=1920x1080 canvas=1367x720  arena=967x334@66  hand=967x214  cards@224 deck→186 clear
```

(The screen this was measured on is 1366 × 768, so the larger windows were clamped by the desktop — what the
run proves is the aspect rule: the page's HEIGHT and every region's vertical place are identical, and the
extra width went to the arena and the hand.)

### Every body is a picture too

The third art slot, on the same terms as the first two: `Presentation.Enemies[id].Art` already said
`enemies/<id>.png` for all **269** bodies and Godot ignored it. It does not any more — a column draws the
picture if the file is there and the stick figure if it is not. Two fields were added at assembly, because
neither can be worked out by whoever is drawing:

- the **name**, which lives inside the encounters that use a body, not on the enemy record; and
- the **role** (`Frame`, the same look slot the relic shelf reads its pool from): **35 boss · 66 elite ·
  4 mimic · 164 standard**. ⚠ **WHAT KIND OF BODY THIS IS, IS NOT A PROPERTY OF THE ENEMY** — it is a
  property of the fights it is met in, and the same id can be met in several. The strongest room a body ever
  stands in is what it is. The failure this guards is silent: a boss exported as "standard" still ships,
  still fights, still gets a picture — it is simply drawn as filler and nothing would ever say so.

Regenerating the document and comparing leaf by leaf against the old one: **538 differences, 0 keys only on
one side** — 269 `Frame` and 269 `FlavorText`, and nothing else in the game moved.

⚠ **A PICTURE IS NOT MIRRORED.** The stick figure is flipped to face its enemy because it is a symmetric
scribble; a drawn body that is flipped wears its sash on the wrong side. The bodies are drawn facing LEFT,
towards the player, and `ART_SLOTS.md` says so — **733 pictures** now (254 cards · 210 relics · 269 bodies).
Proved with two throwaway placeholders (dropped in, imported, photographed in the arena, removed again;
`--smoke-art` says `733 slots … 0 filled` once more).

---

## Phase D4b — every slot holds a plate, and the demo game leaves ✔ BUILT 2026-09-10

Two things, in this order, because the second changes the count the first works from.

**The placeholders.** `tools/make-card-art.py`, `make-relic-art.py` and `make-enemy-art.py` write one plate
per slot: the file's own name, the entity's name, the word placeholder — and for a body a stick figure facing
LEFT, small for a standard body, bigger for an elite, big and horned for a boss. ★ **THE SLOT LIST IS NOT KEPT
IN THE SCRIPTS**: they read `Presentation.<kind>[id].Art` out of the shipped document, the same sentence the
game reads, so they cannot drift from it. Card slots are gathered by PATH and not by id, or a card and its
improvement would each get a plate. Each plate carries a `bnb-placeholder` text chunk: `--clean` removes only
those, and a painted picture is never overwritten without `--force`. They are PNG, not JPG — the document
names `.png`, and a body needs the transparency a JPG cannot carry.

**The leftovers left.** 25 ported v2 cards (plus their 25 `_plus` twins) had ridden along in the document
since the port, because C-5 in `bnb-content/ACT_I_II_COMPLETION_PLAN.md` said the ported events still named
them. ⚠ **THAT REASON HAD EXPIRED AND NOBODY HAD LOOKED**: the events were replaced act by act, and a search
of the shipped document found not one reference to any of the 50 — not in a pool, a shop, an event, an
encounter, a reward, the starting deck, or any canon card's own program. They could not be dealt. What still
used them were TEN TEST FILES, which had borrowed them as convenient tools (`form_12_b` as a 0-cost filing,
`approved_for_disposal` as a 12-damage hammer), and that was the whole cost of the cleanup. Every fixture now
names a canon card, and the numbers around it moved with it.

Gone with them: `CardMapper`, `RelicMapper`, `BabCard`, `BabRelic`, `BabData.Cards`, `BabData.Relics`,
`ConversionPools.Relics` and the never-called `RelicGrantSource` — the last of the ported relic faucet that
D3a had already cut off. Regenerated and compared leaf by leaf: **2491 leaves removed, all of them under the
50 leftover cards, 0 added, 0 changed.** The document ships **363 cards** and **708 pictures** (229 cards ·
210 relics · 269 bodies), and 25 stale plates were deleted from `assets/art/cards/`.

---

## Phase D5 — pick a card by looking at it

**Deliverable:** card rewards, shop shelves, event offers and in-combat card choices show **card faces**.

- **The engine seam (the only one).** `EntitySelectionRequest` gains a parallel list of identities — what
  kind of thing each option is and its id — filled where `Display(...)` already switches over
  `RewardOffer` / `RunCardInstance` / `RelicInstance` (`InteractiveRunSession.cs:320`). Additive, the
  back-compat constructor stays, no rule moves. Everything downstream (`RenderEntityPick`) then draws a face
  instead of `EntityOption`'s name-over-description panel.
- **The shop needs no seam** — it reads the payload (see finding 4) and swaps `AddShopRow`'s button for a
  face with its price beneath; what the purse cannot reach stays visible and dimmed, as it is today.
- **In-combat card choices** (`PendingCardChoice`, e.g. "archive a card from your draw pile") already hold
  real `CardInstance`s and can draw faces immediately.
- A selected face is marked on the card, not by a checkbox next to it.

**Done when:** `--smoke-reward` and `--smoke-shop` capture rows of cards; picking still works through
`OnCardChoiceClicked` / `PickEntities` unchanged.

---

## Phase D6 — the rest of the screen

The parts that will look wrong once the cards look right: the map (`MapView`, 8 of the 16 colour literals),
enemy and hero panels, the intent telegraph, buttons, banners, the act heading, toasts. No new structure —
the palette, the frame idiom and the spacing from D0/D1 carried through, plus one honest pass over every
screenshot probe. **`--smoke-tooltips` runs on combat, crowd and each boss** and may not report more mutes
than it does today.

---

## Phase D7 — the gate, and only then V-7

1. Core + bnb-content suites green (they are today: 1469 / 755 / 581 / 369 and 1406/1406).
2. bnb-godot builds; every screenshot probe captured and reviewed by the user.
3. `--smoke-marathon` still finishes with `Victory acts=5 rooms=111` and its per-act latency **not worse**
   than 592 s — a card face with an art window and a texture per card is more nodes per screen than a
   label stack, and the marathon is where that shows.
4. Then, unchanged, `ACT_IV_V_BUILD_PLAN.md` **V-7**.

---

## Risks, named now

1. **The frame we do not have yet** (see above). D1's swappable-texture shape is the whole mitigation; if the
   user can export the new front as a transparent overlay the way v4 was exported, the risk disappears.
2. **Cost per frame.** 134 × 190 faces with a frame texture, an art texture and clipped text, times a hand of
   ten, times a rebuild on every click — the current screen is Labels in a Box and is already the expensive
   half of an answer in Act V (33 s per room in the marathon). Measured at D1, not at D7.
3. **The 47 unaccounted relics** (D3) — a strip that shows a frame with no code is exactly the "looks like it
   works" failure this project keeps finding.
4. **A palette is not a look.** Dark red plus an accent will not by itself stop the screen reading as a
   harness; what fixes that is the card face, the relic shelf and the pickers — which is why D0 is the
   shortest phase here and D1/D4/D5 are the long ones.

---

## Open decisions (needed before D0 starts)

- ~~The accent~~ — **Antique Gold `#C9A227`**, decided 2026-09-10.
- ~~The signal colour~~ — **Amber `#FFD166`**, decided 2026-09-10.
- ~~`sudo apt install ffmpeg`~~ — installed; the back converts in D2.
- ~~A font~~ — **Godot default, with one hook in `MoonvineTheme` to change it later.** Decided 2026-09-10.
- ~~The card code scheme~~ — **the id is the code** (`levy_stamp.png`). Decided 2026-09-10.
- ~~The new front as a transparent overlay~~ — **delivered**, `assets/cards/card-frame.png`.
- **D3a** — is the relic-faucet fix in scope for this arc? (It is content, and it is the reason the shelf
  can mean anything.)
