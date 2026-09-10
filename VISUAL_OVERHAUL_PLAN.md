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

- **A transparent frame for the NEW front.** `condemed by decree.png` (1053 × 1494, ratio 0.7048) is a
  *composed* card — frame, art and text baked together. The only transparent overlay we have,
  `bureaucrats-and-broomsticks-frame-overlay.png` (alpha 0 in the art window, confirmed), is the **older v4**
  layout, and so is the editable `…card-front-template.svg` (named windows: `titleWindowClip`,
  `costWindowClip`, `artWindowClip`, `rulesWindowClip`). **D1 is written so this does not block:** the card is
  laid out from normalized rectangles and the frame is a *swappable texture*, starting as a drawn frame in the
  new palette. The moment a transparent overlay of the new front exists, it is one file drop.
- **A decoder for the new back.** `BaB-cardback-master.zip` contains `BaB-cardback-master.mp4`
  (H.264, 720 × 1008, 30 fps). Godot 4 plays **Ogg Theora only**. This machine has `theoraenc` but **no
  H.264 decoder** (no ffmpeg, no `gstreamer1.0-libav`), so the clip cannot be converted here yet — see D2.
  ⚠ Also unresolved: the zip's mp4 is **byte-identical** to `Bureaucrats-and-Broomsticks-clockwise-smooth-master.mp4`,
  and the back already shipping in `assets/cards/card-back.ogv` is the same artwork at the same 720 × 1008.
  It may already BE this clip. D2's first act is to answer that, cheaply.

---

## Phase D0 — the ground: palette and type

**Deliverable:** `MoonvineTheme` carries a dark-red-almost-black palette and one accent; every one of the 16
literals is either a token or justified in a comment.

- Base family (near-black with a red bias): page ground, panel, raised panel, control, hairline.
- One accent, chosen by the user from the swatch page produced alongside this plan.
- ⚠ **A signal palette, decided as its own thing.** On a red ground, red stops being a warning — today's
  `Danger = e08a8a` will sink into the new panel colour. Attack intents, damage numbers and the "you cannot
  afford this" state need a signal that survives the ground (a hot ember, or bone-white carrying a red glow).
  This is the one place where "dark red everywhere" must be argued with, and the argument goes in the file.
- **The relic pool colours are NOT ours to choose.** The canon fixes them (§10.4): Normal = slate gray,
  Shop = copper, Event = pale violet, Boss = dark purple + antique gold. The relic strip (D4) uses those, and
  the accent has to sit next to them without fighting.
- Type: the frontend ships **no font today** (`theme/` is empty). The master front is set in a heavy rounded
  face for the title and a lighter one for rules. One licensed-for-shipping family gets vendored into
  `theme/`, or we stay on the Godot default and say so.

**Done when:** every screen renders in the new ground with no leftover green/yellow, and the screenshot
probes (`--smoke-map`, `--smoke-shop`, `--smoke-event`, `--smoke-reward`, `--smoke-crowd`, `--smoke-boss`)
are captured for review.

---

## Phase D1 — the card face

**Deliverable:** `CardVisuals.Face(...)` — one card widget, built from the master layout, used by hand, deck,
pickers, shop and the deck list. `CardBlockButton` in `SessionScreen` becomes a thin caller.

- **Geometry from the master.** Ratio **0.7048** (1053 × 1494). Fields as fractions of the card, so the same
  layout holds at every size: cost badge top-left ≈ (0.068, 0.048) r ≈ 0.043 · title plaque
  x 0.12–0.88, y 0.035–0.105 · **art window** x 0.055–0.945, y 0.135–0.72 · rules plaque x 0.055–0.945,
  y 0.735–0.965 · three corner badges. These get calibrated once against the real frame and pinned as
  constants with the measurement in a comment.
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

---

## Phase D2 — the card back

1. **Answer first, convert second.** Decode one frame of `BaB-cardback-master.mp4` and compare it against
   `assets/cards/card-back.png`. If it is the same animation, this phase is *already done* and costs nothing.
2. If it differs: `sudo apt install ffmpeg` (one line, the user runs it), then mp4 → `.ogv`
   (Theora, keep 720 × 1008 @ 30) plus a still poster PNG at 2× for the fanned backs, replacing both files.
3. The back's own frame colour comes from D0 so the deck pile sits in the new palette.

⚠ `CardVisuals` already limits how many backs animate at once (Godot decodes Theora on the CPU) — that stays.

---

## Phase D3 — the art slots, and the table the user fills

**Deliverable:** every card and every relic has a **stable, unique, human-readable code**, the frontend looks
for a file named by it, and finding nothing is normal.

- **Relics: the canon number is the code.** `R-001` … `R-168`, generated from the title match proved above,
  so `R-001` on screen is entry `1. Levy Stamp` in the canon — the user reads the code off the frame and knows
  which brief made it. Pinned by a test: all 168 map, no duplicates, pool ranges agree.
- **Cards: a minted code**, because no card canon exists yet. Proposal `C-B-###` (Bureaucrat), `C-G-###`
  (general), `C-S-##` (starter), `C-J-##` (Junk), an upgraded card sharing its base code with a `+`. **413**
  card presentations exist (upgrades included); the code table is generated and pinned, never hand-kept.
- **Where a file goes:** `assets/cards/art/<code>.png`, `assets/relics/art/<code>.png`. Godot resolves
  `Presentation.Art` → code → path; a missing file draws the empty window with the code. Dropping a PNG in is
  the entire act of filling a slot — no rebuild, no registry edit.
- **`ART_SLOTS.md`**, generated: every code, title, pool/act, and for relics the canon line, so the user can
  work down it while generating images.

⚠ **An audit belongs here.** The document carries **215** relic presentations against **168** canonical
relics — roughly 47 entries are ported v2 demo relics (`self_inking_stamp`, `clerk_badge`, `deputy_seal`, …).
Before the strip can promise "every relic you can hold has a frame and a code", we settle which of those a run
can actually hand you, and either give them a code or remove them. (Act III's lesson, still true: a pool
nothing draws from looks like a working pool.)

---

## Phase D4 — the relic strip

**Deliverable:** the right edge stops being a bullet list. Relics are a wrapping grid of small framed
squares; hovering one explains it.

- One square per relic: the pool's canon frame colour (D0), the art if a file exists, otherwise the code.
- **Hover = the relic's own words** — `Presentation.Relics[id].FlavorText` through `Glossary.Explain`,
  exactly what the list shows today (`SessionScreen.cs:2512`), so nothing is lost in the move.
- Disabled relics (`relic.Enabled == false`) stay visibly present but dimmed — "(off)" is information.
- Consumables get the same treatment for free; the deck list stays a list (it is a list of many, not a shelf).
- The strip must survive **69 relics on one screen** (an Act-V run can hold a lot): it wraps and scrolls, and
  a probe proves it at a hostile count rather than at four.

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

- **The accent**, from the swatch page.
- **The signal colour** for attack/damage/refusal on a red ground.
- **A font**, vendored or default.
- **The card code scheme** (`C-B-###` / `C-G-###` / `R-###`) — is that what the user wants to type when
  naming generated files?
- **The new front as a transparent overlay** — can it be exported the way v4 was?
- **`sudo apt install ffmpeg`** — allowed, or should the back be supplied as `.ogv`?
