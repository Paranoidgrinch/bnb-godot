#!/usr/bin/env python3
"""The game's own icon: the one a player double-clicks, and the one on the window and the taskbar.

A rubber stamp in the game's gold on the game's ground, reading B&B. It is drawn rather than cut out of an
illustration on purpose: an icon has to read at 16 px, where a picture turns to mud and two letters in a ring
still read. Draw a better one and save it over these files; nothing else has to change.

    assets/brand/icon.png    1024 x 1024 — project.godot's application/config/icon, the Linux .desktop icon
    assets/brand/icon.ico    16 … 256 — the Windows export writes it into the .exe; the installer uses it too

Then `tools/import-art.sh` once, because res:// only holds what the importer has seen.
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "assets" / "brand"

GROUND = (18, 7, 9, 255)       # MoonvineTheme.BgPanel
RAISED = (42, 16, 20, 255)     # MoonvineTheme.BgControl
GOLD = (201, 162, 39, 255)     # MoonvineTheme.Accent
GOLD_LIGHT = (233, 209, 138, 255)
BLOOD = (92, 18, 25, 255)      # MoonvineTheme.Blood
SERIF_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf"


def icon(size=1024):
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    s = size / 1024

    # the tile: a rounded square with a blood rim, so it sits on a light desktop as well as a dark one
    draw.rounded_rectangle([8 * s, 8 * s, size - 8 * s, size - 8 * s], radius=190 * s, fill=BLOOD)
    draw.rounded_rectangle([40 * s, 40 * s, size - 40 * s, size - 40 * s], radius=160 * s, fill=GROUND)

    # the stamp: a double ring, slightly heavier outside, the way a rubber stamp prints
    c = size / 2
    for radius, width in ((392, 34), (336, 12)):
        r = radius * s
        draw.ellipse([c - r, c - r, c + r, c + r], outline=GOLD, width=max(1, round(width * s)))

    # B&B across the middle, with a bar above and below like the band of a stamp
    font = ImageFont.truetype(SERIF_BOLD, round(240 * s))
    text = "B&B"
    left, top, right, bottom = draw.textbbox((0, 0), text, font=font)
    w, h = right - left, bottom - top
    draw.rectangle([c - 300 * s, c - h / 2 - 48 * s, c + 300 * s, c + h / 2 + 48 * s], fill=RAISED)
    for y in (c - h / 2 - 48 * s, c + h / 2 + 48 * s):
        draw.line([c - 300 * s, y, c + 300 * s, y], fill=GOLD, width=max(1, round(12 * s)))
    draw.text((c - w / 2 - left, c - h / 2 - top), text, font=font, fill=GOLD_LIGHT)
    return image


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    big = icon(1024)
    big.save(OUT / "icon.png")
    # Each size drawn at its size, not shrunk from 1024: the rings stay a whole pixel wide at 16 and 32.
    sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = [icon(n) if n >= 64 else icon(256).resize((n, n), Image.LANCZOS) for n in sizes]
    frames[-1].save(OUT / "icon.ico", sizes=[(n, n) for n in sizes], append_images=frames[:-1])
    print(f"wrote {OUT / 'icon.png'} and {OUT / 'icon.ico'}")


if __name__ == "__main__":
    main()
