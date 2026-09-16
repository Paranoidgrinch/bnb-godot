#!/usr/bin/env python3
"""One picture per act, as a placeholder until the acts are painted.

The slots are not a list kept here — they are what the shipped document declares. `Presentation.Encounters
[<id>].Art` says `backgrounds/act_2_archives.png` for every fight of Act II, so this reads the same sentence
the game reads and can never drift from it: 294 encounters asking for 5 pictures.

Each plate is cut from the museum stone the way the materials are (tools/make-materials.py): the photograph's
DETAIL, flat-fielded away from the room it was standing in, mapped through a ramp built out of MoonvineTheme,
and then darkened toward the edges. The act's name and the word PLACEHOLDER are written across it, because a
picture in the wrong slot has to be visible at a glance rather than be a picture nobody recognises.

⚠ THESE ARE DRAWN AT 1280 x 720 AND THEY ARE NOT THE CEILING. A painted background may be larger; it is drawn
scaled to the page and reduced through a mip chain. What it may NOT be is bright: the scrim that keeps a fight
readable lives in the SLOT, not in the picture (scripts/SessionScreen.cs), so a loud picture cannot break a
fight — it can only look loud under a veil.
"""
import argparse
import json
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont
from PIL.PngImagePlugin import PngInfo

REPO = Path(__file__).resolve().parent.parent
BLUEPRINT = REPO / "content" / "game.roguedeck.json"
OUT = REPO / "assets" / "art" / "backgrounds"
PHOTOS = Path.home() / "Downloads"
MARK = "bnb-placeholder"

FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FONT_MONO = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"

INK = (233, 209, 138)
DIM = (125, 100, 20)

# Which stone stands in for which act, and how dark its ground is run. Act V is the darkest because its rooms
# are the gods' and the canon runs them on near-black purple; Act I is the warmest because it is an office.
ACTS = {
    "act_1_city": ("IMG_2597.jpg", (0.18, 0.12, 0.78, 0.48), (0x14, 0x09, 0x0b), (0x4a, 0x18, 0x1c)),
    "act_2_archives": ("IMG_2596.jpg", (0.12, 0.30, 0.92, 0.66), (0x0f, 0x0a, 0x0c), (0x38, 0x22, 0x20)),
    "act_3_green_docket": ("IMG_2609.jpg", (0.12, 0.06, 0.58, 0.36), (0x0c, 0x0e, 0x0a), (0x2c, 0x3a, 0x24)),
    "act_4_licensing_labyrinth": ("IMG_2606.jpg", (0.22, 0.20, 0.72, 0.72), (0x0d, 0x07, 0x07), (0x46, 0x1c, 0x14)),
    "act_5_divine_ledger": ("IMG_2600.jpg", (0.02, 0.58, 0.55, 0.96), (0x0a, 0x06, 0x0f), (0x1c, 0x0d, 0x2c)),
}


def slots(doc):
    """act id -> how many encounters are drawn on it, read out of the document itself."""
    found = {}
    for entry in doc["Presentation"]["Encounters"].values():
        art = entry.get("Art")
        if art:
            found[Path(art).stem] = found.get(Path(art).stem, 0) + 1
    return found


def plate(act, size=(1280, 720)):
    name, box, dark, lit = ACTS.get(act, ACTS["act_1_city"])
    from PIL import ImageChops, ImageOps, ImageStat

    photo = ImageOps.exif_transpose(Image.open(PHOTOS / name))
    w, h = photo.size
    crop = photo.crop((int(box[0] * w), int(box[1] * h), int(box[2] * w), int(box[3] * h))).convert("RGB")
    crop = crop.resize(size, Image.LANCZOS)

    grey = crop.convert("L")
    high = ImageChops.subtract(grey, grey.filter(ImageFilter.GaussianBlur(40)), scale=1.0, offset=128)
    spread = ImageStat.Stat(high).stddev[0] or 1.0
    gain = 26.0 / spread
    high = high.point(lambda v: max(0, min(255, int(128 + (v - 128) * gain))))

    lut = [[int(dark[c] + (lit[c] - dark[c]) * (v / 255.0)) for v in range(256)] for c in range(3)]
    image = Image.merge("RGB", [high.point(channel) for channel in lut])

    # A vignette of its own, so even a placeholder sits down rather than standing up. The slot adds its own on
    # top; this one is the picture being polite, not the picture being safe.
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).ellipse([-size[0] * 0.15, -size[1] * 0.25,
                                 size[0] * 1.15, size[1] * 1.25], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(size[0] // 8))
    image = Image.composite(image, Image.new("RGB", size, dark), mask)

    draw = ImageDraw.Draw(image)
    title = ImageFont.truetype(FONT_BOLD, 62)
    code = ImageFont.truetype(FONT_MONO, 26)
    words = act.replace("_", " ").upper()
    draw.text(((size[0] - draw.textlength(words, font=title)) / 2, size[1] * 0.42), words, font=title, fill=INK)
    mark = f"backgrounds/{act}.png · PLACEHOLDER"
    draw.text(((size[0] - draw.textlength(mark, font=code)) / 2, size[1] * 0.56), mark, font=code, fill=DIM)
    _ = math
    return image


def is_placeholder(path):
    try:
        with Image.open(path) as image:
            return MARK in (image.text or {})
    except Exception:
        return False


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--force", action="store_true", help="overwrite a painted background as well")
    parser.add_argument("--clean", action="store_true", help="remove only what carries the placeholder mark")
    parser.add_argument("--only", nargs="+", metavar="ACT", help="just these act ids")
    args = parser.parse_args()

    if args.clean:
        gone = [p for p in sorted(OUT.glob("*.png")) if is_placeholder(p)]
        for path in gone:
            path.unlink()
        print(f"backgrounds: removed {len(gone)} placeholder(s)")
        return

    declared = slots(json.loads(BLUEPRINT.read_text()))
    if not declared:
        raise SystemExit("no encounter declares a background — is content/game.roguedeck.json current?")
    OUT.mkdir(parents=True, exist_ok=True)
    wanted = args.only or sorted(declared)
    written = skipped = 0
    for act in wanted:
        if act not in declared:
            raise SystemExit(f"no act declares {act}; the document names: {', '.join(sorted(declared))}")
        path = OUT / f"{act}.png"
        if path.exists() and not args.force and not is_placeholder(path):
            skipped += 1
            continue
        info = PngInfo()
        info.add_text(MARK, "1")
        plate(act).save(path, "PNG", pnginfo=info, optimize=True)
        print(f"  {act:28s} {declared[act]:4d} encounters draw on it")
        written += 1
    print(f"backgrounds: wrote {written} of {len(wanted)}"
          + (f", left {skipped} painted alone" if skipped else ""))
    print("run tools/import-art.sh once — Godot only shows a file the importer has seen")


if __name__ == "__main__":
    main()
