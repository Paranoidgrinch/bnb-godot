#!/usr/bin/env python3
"""The relic pictures chosen by the user (2026-11-22) from two icon sets, inked for the relic tile.

    uvx --with cairosvg --with pillow python assets/art/relics/icons/make_relic_icons.py

picks.tsv names, per relic id, its source in src/: a game-icons.net SVG (gi-<author>--<name>.svg), one of Lorc's
"700+ RPG Icons" from OpenGameArt (oga-lorc-<n>.png), or
a picture put together from game-icons.net sources by make_combos.py (combo-*.png). Every
source is white on black; its brightness becomes the picture's opacity, inked in pale parchment so it reads on the
tile's dark ground, with a margin so the frame never touches it. Writes ../<relic id>.png at 512 x 512. Licences:
LICENSE.txt (CC BY 3.0, credited in the game's credits as "Relic icons").
"""
import io
import pathlib

import cairosvg
from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
OUT = HERE.parent
SIZE = 512
MARGIN = 0.10          # of the side, on every side
INK = (236, 224, 198)  # parchment
COLOURS = {"red": (200, 64, 52)}  # an optional third column in picks.tsv inks that relic in another colour


def mask(source: pathlib.Path) -> Image.Image:
    if source.suffix == ".svg":
        png = cairosvg.svg2png(url=str(source), output_width=1024, output_height=1024)
        image = Image.open(io.BytesIO(png)).convert("L")
    else:
        image = Image.open(source).convert("L")
        if source.name.startswith("oga-"):
            side = image.size[0]
            cut = round(side * 0.04)  # the set's grey frame (14 of 400 pixels)
            image = image.crop((cut, cut, side - cut, side - cut))
    return image


def ink(source: pathlib.Path, colour: tuple[int, int, int] = INK) -> Image.Image:
    inner = round(SIZE * (1 - 2 * MARGIN))
    alpha = mask(source).resize((inner, inner), Image.LANCZOS)
    alpha = alpha.point(lambda v: 0 if v < 24 else v)
    shape = Image.new("RGBA", (inner, inner), colour + (0,))
    shape.putalpha(alpha)
    picture = Image.new("RGBA", (SIZE, SIZE), colour + (0,))
    offset = (SIZE - inner) // 2
    picture.alpha_composite(shape, (offset, offset))
    return picture


def main() -> None:
    for line in (HERE / "picks.tsv").read_text().splitlines():
        if not line.strip():
            continue
        relic, source, *colour = line.split("\t")
        ink(HERE / "src" / source, COLOURS[colour[0]] if colour else INK).save(OUT / f"{relic}.png", optimize=True)
        print(f"{relic}.png  <-  {source}")


if __name__ == "__main__":
    main()
