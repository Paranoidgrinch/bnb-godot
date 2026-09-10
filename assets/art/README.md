# assets/art

One picture per card, per relic and per body, named by the code in `../../../bnb-content/ART_SLOTS.md`:

    assets/art/cards/<id>.png     the card's window   — 11:10 landscape, drawn at 122 × 110
    assets/art/relics/<id>.png    the relic's square  — square, drawn small
    assets/art/enemies/<id>.png   the body's column   — portrait, drawn 150 tall, aspect kept

A body is every enemy, elite and boss (269 of them). It is never mirrored by the game, so draw it facing
LEFT — towards the player, who stands on the left of the arena. Until the file is there the arena draws the
stick figure it draws today.

## The placeholders

Every one of the 733 slots holds a placeholder today — a plate carrying the file's own name and the word
placeholder, so a picture that is in the wrong slot is visible at a glance instead of being a picture nobody
recognises. Three scripts write them, each reading the slots out of `content/game.roguedeck.json`, which is
where the paths are declared:

    tools/make-card-art.py      254 cards   · 488 x 440, the file name, the card's name, PLACEHOLDER
    tools/make-relic-art.py     210 relics  · 512 x 512 square, the file name, the relic's name, PLACEHOLDER
    tools/make-enemy-art.py     269 bodies  · 600 x 900 on transparency, a stick figure facing left with its
                                              file name over its head — small for a standard body, bigger for
                                              an elite, big and horned for a boss

    tools/make-enemy-art.py --only queue_imp      # one slot
    tools/make-card-art.py --force                # overwrite a painted picture as well
    tools/make-relic-art.py --clean               # take the placeholders back out

**A painted picture replaces a placeholder by being saved over it** — same name, same folder, then
`tools/import-art.sh`. Nothing has to be registered, and nothing distinguishes the two to the game. Only the
scripts tell them apart: every plate they write carries a `bnb-placeholder` text chunk, which is what `--clean`
removes and what keeps `--force` from being needed to overwrite a placeholder made yesterday.

The path is the one the game document itself declares (`Presentation.Art`), which is why there is nothing to
register: drop the file in, run `../../tools/import-art.sh` once, and the game shows it. A slot with no file at
all draws an empty socket with the code in it, which is a normal state and not an error.

Paint larger than the drawn size and let the mipmaps reduce it — new textures are imported with a mip chain
(`project.godot`, `[importer_defaults]`), which is what keeps fine linework from glittering at card size.

An upgraded card has no picture of its own — `levy_stamp+` is drawn from `levy_stamp.png`. No file name in
this game contains a `+`.
