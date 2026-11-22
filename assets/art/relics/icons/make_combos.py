#!/usr/bin/env python3
"""Three relic pictures put together from game-icons.net sources in src/, as the user asked (2026-11-22).

    uvx --with cairosvg --with pillow python assets/art/relics/icons/make_combos.py

Writes src/combo-*.png (white on black, like the other sources); make_relic_icons.py inks them.
  combo-hook-weight.png    Gate Weight: sbed's weight-crush (without the crushed ball) hung by its eye on
                           delapouite's pulley-hook.
  combo-tombstone-iii.png  Third Obituary: lorc's tombstone with a Roman III under the inscription.
  combo-knot-scissors.png  Cut Rope: delapouite's knot with lorc's scissors at it.
"""
import io
import pathlib

import cairosvg
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

SRC = pathlib.Path(__file__).resolve().parent / "src"
SERIF = "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"


def svg(name: str, side: int = 1000) -> Image.Image:
    png = cairosvg.svg2png(url=str(SRC / name), output_width=side, output_height=side)
    return Image.open(io.BytesIO(png)).convert("L")


def gap(shape: Image.Image, width: int) -> Image.Image:
    return shape.filter(ImageFilter.MaxFilter(width))


def square(shape: Image.Image) -> Image.Image:
    shape = shape.crop(shape.getbbox())
    side = max(shape.size)
    out = Image.new("L", (side, side))
    out.paste(shape, ((side - shape.size[0]) // 2, (side - shape.size[1]) // 2))
    return out


def hook_weight() -> Image.Image:
    hook = svg("gi-delapouite--pulley-hook.svg")
    side = 850
    weight = svg("gi-sbed--weight-crush.svg", side).crop((0, 0, side, int(0.71 * side)))
    x = 300 + 483 - side // 2
    y = 860 + 45 - int(0.069 * side)
    size = (1600, y + weight.size[1] + 20)
    hook_layer = Image.new("L", size)
    hook_layer.paste(hook, (300, 0))
    weight_layer = Image.new("L", size)
    weight_layer.paste(weight, (x, y))
    # the hook in front of the weight's eye on the tip side...
    out = ImageChops.lighter(ImageChops.subtract(weight_layer, gap(hook_layer, 15)), hook_layer)
    # ...and the eye in front of the hook on the shank side, so the hook goes through it
    eye = Image.new("L", size)
    box = (x + side // 2, y, x + side, y + int(0.26 * side))
    eye.paste(weight_layer.crop(box), box[:2])
    out = ImageChops.subtract(out, ImageChops.subtract(gap(eye, 15), weight_layer))
    return square(ImageChops.lighter(out, eye))


def tombstone_iii() -> Image.Image:
    stone = svg("gi-lorc--tombstone.svg")
    draw = ImageDraw.Draw(stone)
    font = ImageFont.truetype(SERIF, 150)
    left, top, right, bottom = draw.textbbox((0, 0), "III", font=font)
    draw.text((500 - (right - left) / 2 - left, 555 - (bottom - top) / 2 - top), "III", font=font, fill=0)
    return stone


def knot_scissors() -> Image.Image:
    knot = svg("gi-delapouite--knot.svg")
    scissors = Image.new("L", knot.size)
    scissors.paste(svg("gi-lorc--scissors.svg", 480).rotate(180), (520, 520))
    return ImageChops.lighter(ImageChops.subtract(knot, gap(scissors, 25)), scissors)


def main() -> None:
    for name, make in (("combo-hook-weight.png", hook_weight), ("combo-tombstone-iii.png", tombstone_iii),
                       ("combo-knot-scissors.png", knot_scissors)):
        make().save(SRC / name, optimize=True)
        print(name)


if __name__ == "__main__":
    main()
