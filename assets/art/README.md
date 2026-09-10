# assets/art

One picture per card, per relic and per body, named by the code in `../../../bnb-content/ART_SLOTS.md`:

    assets/art/cards/<id>.png     the card's window   — 11:10 landscape, drawn at 122 × 110
    assets/art/relics/<id>.png    the relic's square  — square, drawn small
    assets/art/enemies/<id>.png   the body's column   — portrait, drawn 150 tall, aspect kept

A body is every enemy, elite and boss (269 of them). It is never mirrored by the game, so draw it facing
LEFT — towards the player, who stands on the left of the arena. Until the file is there the arena draws the
stick figure it draws today.

The path is the one the game document itself declares (`Presentation.Art`), so there is nothing to register:
drop the file in, run `../../tools/import-art.sh` once, and the game shows it. A slot with no file draws an
empty socket with the code in it, which is a normal state and not an error.

Paint larger than the drawn size and let the mipmaps reduce it — new textures are imported with a mip chain
(`project.godot`, `[importer_defaults]`), which is what keeps fine linework from glittering at card size.

An upgraded card has no picture of its own — `levy_stamp+` is drawn from `levy_stamp.png`. No file name in
this game contains a `+`.
