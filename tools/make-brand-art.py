#!/usr/bin/env python3
"""The studio's mark, as a placeholder until the real one is drawn.

A logo is the one picture in this repository that is not ours to invent — it belongs to Moonvine Forge, not to
the game — so what this writes is deliberately a PLACEHOLDER and says so on its face: a moonvine over a forge
mark, the file's own name under it, and the `bnb-placeholder` chunk that lets `--clean` take it back out.
Saving the real logo over it is the whole handover; nothing has to be registered.

    assets/brand/moonvine-forge.png    drawn at 220 x 220 in the opening, painted larger

Then `tools/import-art.sh` once, because res:// only holds what the importer has seen.
"""
import argparse
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from PIL.PngImagePlugin import PngInfo

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "assets" / "brand"
MARK = "bnb-placeholder"

GROUND = (8, 4, 6, 0)
GOLD = (201, 162, 39, 255)
GOLD_LIGHT = (233, 209, 138, 255)
GOLD_DARK = (125, 100, 20, 255)
FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


def moonvine(size=512):
    """A crescent with a vine through it, over an anvil line. Legible at 220 px, obviously provisional."""
    image = Image.new("RGBA", (size, size), GROUND)
    draw = ImageDraw.Draw(image)
    c = size / 2

    # the moon: a ring with a bite taken out of it
    draw.ellipse([c - size * 0.34, c - size * 0.36, c + size * 0.34, c + size * 0.32], outline=GOLD, width=size // 45)
    draw.ellipse([c - size * 0.16, c - size * 0.40, c + size * 0.46, c + size * 0.28], fill=GROUND)

    # the vine: a coil climbing through the crescent, with two leaves
    points = []
    for step in range(120):
        t = step / 119
        angle = t * math.pi * 2.2
        radius = size * (0.07 + 0.20 * t)
        points.append((c + math.sin(angle) * radius * 0.7, c + size * 0.30 - t * size * 0.62))
    draw.line(points, fill=GOLD_LIGHT, width=max(2, size // 90), joint="curve")
    for at, lean in ((0.35, -1), (0.68, 1)):
        x, y = points[int(at * (len(points) - 1))]
        draw.ellipse([x - size * 0.09 * (1 if lean > 0 else 0), y - size * 0.05,
                      x + size * 0.09 * (1 if lean > 0 else 0) + size * 0.09 * (1 if lean < 0 else 0),
                      y + size * 0.05], fill=GOLD_DARK, outline=GOLD)

    # the forge: an anvil line under it all
    draw.rectangle([c - size * 0.30, c + size * 0.34, c + size * 0.30, c + size * 0.38], fill=GOLD)

    face = ImageFont.truetype(FONT_BOLD, size // 16)
    word = "PLACEHOLDER"
    draw.text(((size - draw.textlength(word, font=face)) / 2, c + size * 0.41), word, font=face, fill=GOLD_DARK)
    return image


PIECES = {"moonvine-forge.png": moonvine}


def is_placeholder(path):
    try:
        with Image.open(path) as image:
            return MARK in (image.text or {})
    except Exception:
        return False


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--force", action="store_true", help="overwrite a real logo as well")
    parser.add_argument("--clean", action="store_true", help="remove only what carries the placeholder mark")
    args = parser.parse_args()

    if args.clean:
        gone = [p for p in sorted(OUT.glob("*.png")) if is_placeholder(p)]
        for path in gone:
            path.unlink()
        print(f"brand: removed {len(gone)} placeholder(s)")
        return

    OUT.mkdir(parents=True, exist_ok=True)
    for name, paint in PIECES.items():
        path = OUT / name
        if path.exists() and not args.force and not is_placeholder(path):
            print(f"brand: {name} is a real picture — left alone (--force to overwrite)")
            continue
        info = PngInfo()
        info.add_text(MARK, "1")
        paint().save(path, "PNG", pnginfo=info, optimize=True)
        print(f"brand: wrote {path}")
    print("run tools/import-art.sh once — Godot only shows a file the importer has seen")


if __name__ == "__main__":
    main()
