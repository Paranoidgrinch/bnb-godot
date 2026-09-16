#!/usr/bin/env python3
"""The materials the game is built out of: wood, black marble, jasper, and one gold moulding.

D8 gives a surface a MATERIAL instead of a fill, and a material has to exist before a panel can wear it. This
writes the eight files in `assets/materials/` — three seamless tiles and five built pieces — and it writes them
the way `placeholder_art.py` writes a picture: marked `bnb-placeholder`, replaced by saving a painted file over
it, taken back out by `--clean`. Nothing here is precious.

Where the stone comes from and where it does not. The grain and the veining are cut out of the museum
photographs (`~/Downloads/IMG_25xx.jpg`); the COLOUR is not. The same stone measured 1.7x apart in luminance
between two of those photographs — a warm spot on one lintel, a window reflected in the vitrine at the other —
so every crop is reduced to its DETAIL (high-pass, flat-fielded: the lighting is what the blur holds, and the
blur is subtracted away), normalised to a fixed contrast, and then mapped through a ramp built out of
`MoonvineTheme`. The photograph supplies the veining; the palette supplies the colour.

⚠ FOUR OF THE PHOTOGRAPHS ARE LYING ON THEIR SIDE. Every file is 4032x3024; the portraits carry an EXIF
orientation flag that ffmpeg honours and PIL ignores. Everything here goes through `exif_transpose` first, and
`--contact` writes a sheet to look at, because a crop fraction is a guess until it has been seen.

⚠ A MATERIAL IS FELT, NOT SEEN. A surface carrying text may not compete with it, so the panel materials are
normalised to sigma 7 of 255 and the small fields — a plaque the size of a word — to 14. The number is printed
on every run; if a material starts shouting, that is where it shows.
"""
import argparse
import math
import random
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageOps, ImageStat
from PIL.PngImagePlugin import PngInfo

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / "assets" / "materials"
PHOTOS = Path.home() / "Downloads"
MARK = "bnb-placeholder"

# ── the ramps, out of MoonvineTheme ───────────────────────────────────────────────
# Three stops: the shadow a grain sits in, the body of the material, and the bright accident in it — a vein of
# calcite, a highlight on a fibre. The body is what the eye reads as "the colour of this thing".
WOOD = [(0.0, (0x0b, 0x06, 0x03)), (0.5, (0x23, 0x15, 0x0a)), (1.0, (0x3c, 0x28, 0x14))]
MARBLE = [(0.0, (0x07, 0x05, 0x07)), (0.5, (0x1a, 0x15, 0x18)), (1.0, (0x93, 0x88, 0x8c))]
JASPER = [(0.0, (0x0a, 0x03, 0x05)), (0.5, (0x2e, 0x0a, 0x0e)), (1.0, (0x7d, 0x66, 0x5e))]
# ⚠ THE FIELD IS OXBLOOD AND NOT RASPBERRY. The first ramp put the body at #451118 with a pink vein,
# which reads as a bright berry red the moment it is laid out flat — the reference stone is browner,
# darker, and its veins are a warm grey. A red that cheerful would also have walked straight into D0's
# rule that red is not a warning here.

GOLD = (0xc9, 0xa2, 0x27)
GOLD_LIGHT = (0xe9, 0xd1, 0x8a)
GOLD_DARK = (0x7d, 0x64, 0x14)

PANEL_SIGMA = 7.0   # a big surface under text
FIELD_SIGMA = 14.0  # a small plaque that is allowed to look like stone

# Where each material is cut from, as fractions of the UPRIGHT photograph (after exif_transpose).
SOURCES = {
    "wood": ("IMG_2629.jpg", (0.40, 0.58, 0.55, 0.78)),      # the door leaf, matte and evenly lit
    "marble": ("IMG_2629.jpg", (0.283, 0.50, 0.327, 0.88)),  # the doorpost: black with white veining
    "jasper": ("IMG_2597.jpg", (0.18, 0.12, 0.78, 0.48)),    # the Eisenkappel slab, veins and all
}


def photo(name):
    return ImageOps.exif_transpose(Image.open(PHOTOS / name))


def cut(name, box):
    image = photo(name)
    w, h = image.size
    return image.crop((int(box[0] * w), int(box[1] * h), int(box[2] * w), int(box[3] * h))).convert("RGB")


def detail(image, sigma, radius=None):
    """The material without its lighting: a high-pass at `radius`, then scaled to a fixed contrast.

    Subtracting a heavy blur is a flat-field correction — the blur IS the museum's lighting, the glare and the
    gradient across the slab — and what survives is the stone's own grain, which is the only part of a
    photograph that is about the stone and not about the room it was standing in.
    """
    grey = image.convert("L")
    radius = radius or max(6, min(grey.size) // 12)
    high = ImageChops.subtract(grey, grey.filter(ImageFilter.GaussianBlur(radius)), scale=1.0, offset=128)
    measured = ImageStat.Stat(high).stddev[0] or 1.0
    gain = sigma / measured
    return high.point(lambda v: max(0, min(255, int(128 + (v - 128) * gain))))


def ramp(grey, stops):
    """Map a grey detail image through a three-stop colour ramp."""
    luts = []
    for channel in range(3):
        lut = []
        for v in range(256):
            t = v / 255.0
            for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
                if t <= t1 or (t1, c1) is stops[-1]:
                    k = 0.0 if t1 == t0 else (t - t0) / (t1 - t0)
                    k = max(0.0, min(1.0, k))
                    lut.append(int(round(c0[channel] + (c1[channel] - c0[channel]) * k)))
                    break
        luts.append(lut)
    return Image.merge("RGB", [grey.point(lut) for lut in luts])


def to_sigma(grey, stops, sigma, tries=8):
    """Hit the contrast on the OUTPUT, not on the grey going in.

    ⚠ A RAMP IS A CONTRAST CHANGE. Normalising the detail to sigma 7 and then mapping it through three stops
    that span a third of the range delivers sigma 1.3 — the first run of this script wrote a wood grain nobody
    could see and printed the number that proved it. The gain is therefore solved against the ramped result.
    """
    out = ramp(grey, stops)
    for _ in range(tries):
        got = ImageStat.Stat(out.convert("L")).stddev[0] or 0.01
        if abs(got - sigma) < 0.3:
            break
        gain = sigma / got
        grey = grey.point(lambda v: max(0, min(255, int(128 + (v - 128) * gain))))
        out = ramp(grey, stops)
    return out


def seamless(image, n, feather=None):
    """Seamless by healing the seam, not by mirroring the tile.

    ⚠⚠ A MIRRORED TILE IS A RORSCHACH AND IT SHOWS AT SIGMA 7. The first run built each tile out of one
    quadrant flipped four ways, on the argument that a quiet texture hides its own symmetry. It does not: a
    butterfly axis runs straight down the middle of the marble and the jasper, and it is the first thing the
    eye finds on a contact sheet. Measuring contrast said nothing about it — only looking did.

    So: offset the tile by half, which moves both seams into the middle as a cross, and heal that cross by
    blending a band of it with a mirror of the same band. The mirroring is then local to a 2 x feather strip
    instead of being the structure of the whole tile.
    """
    tile = image.resize((n, n), Image.LANCZOS)
    feather = feather or n // 8
    tile = ImageChops.offset(tile, n // 2, n // 2)
    for axis in (0, 1):
        if axis:
            tile = tile.transpose(Image.TRANSPOSE)
        band = tile.crop((n // 2 - feather, 0, n // 2 + feather, n))
        mask = Image.linear_gradient("L").resize((feather, n)).transpose(Image.ROTATE_270)
        ramp2 = Image.new("L", (2 * feather, n))
        ramp2.paste(ImageOps.invert(mask), (0, 0))
        ramp2.paste(mask, (feather, 0))
        tile.paste(Image.composite(ImageOps.mirror(band), band, ramp2), (n // 2 - feather, 0))
        if axis:
            tile = tile.transpose(Image.TRANSPOSE)
    return tile


def wood_grain(n, sigma, seed=7):
    """Wood is drawn, not photographed: the two photographs show it in perspective, and a plank's grain is a
    periodic thing that a tile can simply BE, instead of being cut to look like one."""
    # ⚠⚠ GRAIN IS A WAVELENGTH IN SCREEN PIXELS, NOT A FRACTION OF A TILE. The first panel tiled a 80 px
    # centre whose lowest components ran three cycles across it — about 8 px per stripe, five repeats across a
    # dialog — and the settings box came out in corduroy. Wood at panel size is FINE and QUIET: the low
    # frequencies that make a plank look like a plank at arm's length are the ones that make it look like a
    # barcode at 240. So the spectrum starts at 13, the falloff is gentle enough to keep the fine lines, and
    # the panel tiles at 240 px instead of 80 — D2's rule (encode at the size it is drawn) one layer up.
    rnd = random.Random(seed)
    parts = [(f, rnd.uniform(0, 2 * math.pi), 1.0 / f ** 0.25)
             for f in (13, 17, 23, 29, 37, 41, 53, 61, 73, 89, 101)]
    grey = Image.new("L", (n, n))
    px = grey.load()
    for y in range(n):
        wobble = 3 * math.sin(2 * math.pi * y / n) + 1.5 * math.sin(4 * math.pi * y / n + 1.1)
        for x in range(n):
            u = 2 * math.pi * ((x + wobble) % n) / n
            v = sum(a * math.sin(f * u + p) for f, p, a in parts)
            px[x, y] = max(0, min(255, int(128 + 34 * v + rnd.uniform(-4, 4))))
    measured = ImageStat.Stat(grey).stddev[0] or 1.0
    gain = sigma / measured
    return grey.point(lambda v: max(0, min(255, int(128 + (v - 128) * gain))))


def tile(kind, n=512, sigma=PANEL_SIGMA):
    if kind == "wood":
        return to_sigma(wood_grain(n, sigma), WOOD, sigma)
    name, box = SOURCES[kind]
    stops = {"marble": MARBLE, "jasper": JASPER}[kind]
    return to_sigma(seamless(detail(cut(name, box), sigma), n), stops, sigma)


# ── the built pieces ──────────────────────────────────────────────────────────────

def bevel(draw, box, light, dark, width=1):
    """A raised edge: lit from the top-left, the way every moulding in the photographs is."""
    x0, y0, x1, y1 = box
    for i in range(width):
        draw.line([(x0 + i, y0 + i), (x1 - i, y0 + i)], fill=light)
        draw.line([(x0 + i, y0 + i), (x0 + i, y1 - i)], fill=light)
        draw.line([(x0 + i, y1 - i), (x1 - i, y1 - i)], fill=dark)
        draw.line([(x1 - i, y0 + i), (x1 - i, y1 - i)], fill=dark)


def dentils(draw, box, step, depth, light, dark):
    """The tooth course under a cornice — the one ornament that reads at any size, because it is a rhythm.

    ⚠ A CORNICE IS HORIZONTAL. The first version ran the same course down the left edge as well, which no
    portal in the reference does and which made the frame look like a picture mount rather than a doorway."""
    x0, y0, x1, y1 = box
    for x in range(x0, x1 - step // 2, step):
        draw.rectangle([x, y0, x + step // 2, y1], fill=dark)
        draw.line([(x, y0), (x, y1)], fill=light)
    _ = depth


def nine_patch_ground(kind, size, border, sigma=PANEL_SIGMA):
    """A nine-patch ground whose CENTRE is seamless at centre size.

    ⚠⚠ A SUB-CROP OF A SEAMLESS TILE IS NOT A SEAMLESS TILE. A nine-patch repeats its centre region, and the
    centre of a 128 px piece with a 16 px border is 96 px — cut out of the middle of a seamless 512, whose
    edges match each OTHER and not the edges of that crop. Every panel in the game would have carried a grid
    of hairline discontinuities, visible exactly where a panel is big, which is where panels matter. So the
    centre is GENERATED at centre size, where `seamless` makes it match itself, and the border ring is drawn
    over a separate full-size pass of the same material.
    """
    image = tile(kind, size, sigma).convert("RGBA")
    inner = size - 2 * border
    image.paste(tile(kind, inner, sigma).convert("RGBA"), (border, border))
    return image


def panel_wood(size=256, border=8, rim=False):
    """The everyday panel: a wood ground in a carved edge. Nine-patch margins = `border`.

    ⚠ THE BORDER IS 8 AND NOT 16 BECAUSE OF THE ARENA. A stylebox's content margin has to clear its texture
    margin or the text sits on the carving, and today's panels pad 12 across and 8 down (`Panel`). A 16 px
    carving would have forced 20 px of padding into every PanelContainer in the game — and D6 spent a whole
    pass winning back 145 px of arena height. A material may not cost layout.

    `rim` adds the gold hairline an overlay wants: a dialog on top of the game says so with one gold line,
    which is what the accent has meant since D0.
    """
    image = nine_patch_ground("wood", size, border, sigma=5.0)
    draw = ImageDraw.Draw(image)
    edge = (0x08, 0x05, 0x03, 255)
    lift = (0x3c, 0x28, 0x14, 255)
    sink = (0x0e, 0x08, 0x04, 255)
    draw.rectangle([0, 0, size - 1, size - 1], outline=edge, width=1)
    bevel(draw, (1, 1, size - 2, size - 2), lift, sink, width=1)
    bevel(draw, (border - 3, border - 3, size - border + 2, size - border + 2), sink, lift, width=1)
    draw.rectangle([border - 1, border - 1, size - border, size - border],
                   outline=(*GOLD_DARK, 90), width=1)
    if rim:
        draw.rectangle([0, 0, size - 1, size - 1], outline=(*GOLD, 200), width=1)
    return image


def frame_stone(size=128, border=14):
    """A marble edge around nothing: the centre is transparent, so a plate keeps whatever ground it had.

    ⚠ THERE IS NO PICTURE OF A LINTEL HERE, AND THAT IS DELIBERATE. The first pass drew one — surround,
    tooth course, bead and field, all in a single 256 px nine-patch — and it was the best-looking thing on
    the contact sheet. It is still wrong: a nine-patch REPEATS its edge strips, so the tooth course only
    survives at widths that happen to be whole multiples of its step, and it smears at every other width.
    The portal in the photographs is not one carved slab either; it is a surround, a field and a course laid
    on top of each other. So the lintel is ASSEMBLED at runtime out of frame-stone, field-jasper and
    moulding-gold, and no ornament is ever stretched.
    """
    image = tile("marble", size * 2).resize((size, size), Image.LANCZOS).convert("RGBA")
    draw = ImageDraw.Draw(image)
    draw.rectangle([0, 0, size - 1, size - 1], outline=(0x05, 0x04, 0x05, 255), width=2)
    bevel(draw, (2, 2, size - 3, size - 3), (0x93, 0x88, 0x8c, 170), (0x07, 0x05, 0x07, 255), width=2)
    bevel(draw, (border - 3, border - 3, size - border + 2, size - border + 2),
          (0x07, 0x05, 0x07, 255), (0x93, 0x88, 0x8c, 120), width=1)
    image.paste(Image.new("RGBA", (size - 2 * border, size - 2 * border), (0, 0, 0, 0)), (border, border))
    return image


def field_jasper(size=128, border=10):
    """A small plaque: jasper with a dark stone lip, for anything a word is written on."""
    image = nine_patch_ground("jasper", size, border, sigma=FIELD_SIGMA)
    draw = ImageDraw.Draw(image)
    draw.rectangle([0, 0, size - 1, size - 1], outline=(0x10, 0x06, 0x08, 255), width=2)
    bevel(draw, (2, 2, size - 3, size - 3), (0x12, 0x0a, 0x0c, 255), (0x6a, 0x5a, 0x52, 120), width=2)
    _ = border
    return image


def moulding_gold(width=96, height=18):
    """Egg-and-dart, tileable across: an egg, a dart, an egg. The only ornament allowed to be gold."""
    image = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    step = width // 4
    for i in range(4):
        x = i * step
        draw.ellipse([x + 2, 2, x + step - 8, height - 3], fill=(*GOLD_DARK, 255), outline=(*GOLD, 255))
        draw.ellipse([x + 4, 4, x + step - 10, height - 5], fill=(*GOLD, 255))
        draw.ellipse([x + 5, 5, x + step - 13, height - 9], fill=(*GOLD_LIGHT, 255))
        draw.polygon([(x + step - 6, 2), (x + step - 2, height // 2), (x + step - 6, height - 3)],
                     fill=(*GOLD, 255), outline=(*GOLD_LIGHT, 255))
    return image


def rosette_gold(size=40):
    """The corner of the wooden portal: a rosette, drawn as the photograph has it — a boss in a ring."""
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    c = size / 2
    # ⚠ PETALS GO OUTSIDE THE RING. Drawn inside it they are swallowed by the boss and the thing reads as a
    # coin; the portal's rosette is a flower with a stud in the middle, so the flower has to be the silhouette.
    for i in range(8):
        a = i * math.pi / 4
        r, s_ = c * 0.62, size * 0.19
        draw.ellipse([c + math.cos(a) * r - s_, c + math.sin(a) * r - s_,
                      c + math.cos(a) * r + s_, c + math.sin(a) * r + s_],
                     fill=(*GOLD, 255), outline=(*GOLD_DARK, 255))
    draw.ellipse([c - c * 0.52, c - c * 0.52, c + c * 0.52, c + c * 0.52],
                 fill=(*GOLD_DARK, 255), outline=(*GOLD, 255))
    draw.ellipse([c - size * 0.14, c - size * 0.14, c + size * 0.14, c + size * 0.14],
                 fill=(*GOLD_LIGHT, 255), outline=(*GOLD_DARK, 255))
    return image


PIECES = {
    "wood.png": lambda: tile("wood"),
    "marble.png": lambda: tile("marble"),
    "jasper.png": lambda: tile("jasper", sigma=FIELD_SIGMA),
    "panel-wood.png": panel_wood,
    "panel-wood-rim.png": lambda: panel_wood(rim=True),
    "frame-stone.png": frame_stone,
    "field-jasper.png": field_jasper,
    "moulding-gold.png": moulding_gold,
    "rosette-gold.png": rosette_gold,
}


def save(image, path):
    info = PngInfo()
    info.add_text(MARK, "1")
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, "PNG", pnginfo=info, optimize=True)


def is_placeholder(path):
    try:
        with Image.open(path) as image:
            return MARK in (image.text or {})
    except Exception:
        return False


def contact(out):
    """A sheet to look at: every piece at 1:1, and every tile laid 2x2 so a seam has somewhere to show."""
    pieces = [(name, Image.open(OUT / name).convert("RGBA")) for name in PIECES]
    cell, pad = 300, 16
    sheet = Image.new("RGB", (cell * 3 + pad * 4, (cell + 40) * 3 + pad * 4), (8, 4, 6))
    draw = ImageDraw.Draw(sheet)
    for i, (name, image) in enumerate(pieces):
        x = pad + (i % 3) * (cell + pad)
        y = pad + (i // 3) * (cell + 40 + pad)
        if image.width >= 128 and image.height >= 128:
            twice = Image.new("RGBA", (image.width * 2, image.height * 2))
            for dx in (0, image.width):
                for dy in (0, image.height):
                    twice.paste(image, (dx, dy))
            shown = twice
        else:
            shown = image.resize((image.width * 3, image.height * 3), Image.NEAREST)
        shown.thumbnail((cell, cell), Image.LANCZOS)
        sheet.paste(Image.new("RGB", shown.size, (20, 12, 14)), (x, y))
        sheet.paste(shown, (x, y), shown)
        stat = ImageStat.Stat(image.convert("L"))
        draw.text((x, y + cell + 6), f"{name}  {image.width}x{image.height}  "
                                     f"mean {stat.mean[0]:.0f}  sigma {stat.stddev[0]:.1f}", fill=(233, 209, 138))
    sheet.save(out)
    print(f"contact sheet: {out}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--force", action="store_true", help="overwrite a painted material as well")
    parser.add_argument("--clean", action="store_true", help="remove only what carries the placeholder mark")
    parser.add_argument("--only", nargs="+", metavar="FILE", help="just these pieces")
    parser.add_argument("--contact", metavar="PATH", help="write a contact sheet here and stop")
    args = parser.parse_args()

    if args.contact and not args.clean:
        if not OUT.exists():
            raise SystemExit("nothing to show — run without --contact first")
        contact(Path(args.contact))
        return

    if args.clean:
        gone = [p for p in sorted(OUT.glob("*.png")) if is_placeholder(p)]
        for path in gone:
            path.unlink()
        print(f"materials: removed {len(gone)} placeholder(s)")
        return

    names = args.only or list(PIECES)
    unknown = [n for n in names if n not in PIECES]
    if unknown:
        raise SystemExit(f"no material named: {', '.join(unknown)}")
    written = skipped = 0
    for name in names:
        path = OUT / name
        if path.exists() and not args.force and not is_placeholder(path):
            skipped += 1
            continue
        image = PIECES[name]()
        save(image, path)
        stat = ImageStat.Stat(image.convert("L"))
        print(f"  {name:20s} {image.width}x{image.height}  mean {stat.mean[0]:5.1f}  sigma {stat.stddev[0]:5.1f}")
        written += 1
    print(f"materials: wrote {written} of {len(names)}" + (f", left {skipped} painted alone" if skipped else ""))
    print("run tools/import-art.sh once — Godot only shows a file the importer has seen")


if __name__ == "__main__":
    main()
