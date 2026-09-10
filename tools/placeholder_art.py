#!/usr/bin/env python3
"""What the three placeholder generators share: where the slots are declared, and how text is put on a plate.

A slot is not a list kept here — it is what the shipped game document declares. `Presentation.<kind>[id].Art`
says `cards/levy_stamp.png`, and that path IS the slot, so these generators read the same sentence the game
reads and can never drift from it. An improved card has no slot of its own (`levy_stamp+` draws
`levy_stamp.png`), which is why the card slots are gathered by PATH and not by id.

The files are PNG and not JPG, because that is the extension the document names and Godot resolves; a body
also needs its transparency, which a JPG cannot carry.

Every plate written here carries a `bnb-placeholder` text chunk. That is what lets `--clean` take the
placeholders back out without touching a painted picture that happens to sit in the same folder.
"""
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from PIL.PngImagePlugin import PngInfo

REPO = Path(__file__).resolve().parent.parent
BLUEPRINT = REPO / "content" / "game.roguedeck.json"
ART = REPO / "assets" / "art"

MARK = "bnb-placeholder"

# The Moonvine ramp, the three tones a placeholder needs (scripts/MoonvineTheme.cs): the card ground a plate
# sits on, the amber that is allowed to shout, and the antique gold everything quieter is written in.
GROUND = (36, 16, 21)
INK = (255, 209, 102)
DIM = (201, 162, 39)

FONT_BOLD = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
FONT_MONO = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"


def document(path=None):
    return json.loads(Path(path or BLUEPRINT).read_text())


def card_slots(doc):
    """code -> the name of the card the picture belongs to.

    Two card definitions can point at one picture (a card and its improvement), and the unimproved one is the
    one whose name goes on the plate.
    """
    names = {c["Id"]: c.get("NameKey") or c["Id"] for c in doc["Cards"]}
    slots = {}
    for card_id, entry in doc["Presentation"]["Cards"].items():
        code = stem(entry.get("Art"))
        if code is None:
            continue
        slots.setdefault(code, names.get(code) or names.get(card_id) or code)
    return slots


def relic_slots(doc):
    """code -> the relic's name. The pool it is drawn from rides in `Frame`, but a relic plate does not use it:
    the relic is named and marked a placeholder, which is all that was asked of it."""
    names = {r["Id"]: r.get("DisplayName") or r["Id"] for r in doc["Relics"]}
    slots = {}
    for relic_id, entry in doc["Presentation"]["Relics"].items():
        code = stem(entry.get("Art"))
        if code is not None:
            slots[code] = names.get(relic_id, relic_id)
    return slots


def enemy_slots(doc):
    """code -> (name, role). `Frame` is the kind of body — standard, elite, boss, mimic — worked out by the
    converter from the rooms the body is actually met in, not from anything the body says about itself."""
    slots = {}
    for enemy_id, entry in doc["Presentation"]["Enemies"].items():
        code = stem(entry.get("Art"))
        if code is not None:
            slots[code] = (entry.get("FlavorText") or enemy_id, entry.get("Frame") or "standard")
    return slots


def character_slots(doc):
    """code -> (name, "hero"). The player's own body is a body like any other and is painted by the same hand —
    the shape is the same, only the direction it faces is not. Shaped like an enemy slot so one painter serves
    both; the role is always `hero`, because there is only one kind of player."""
    names = {c["Id"]: (c.get("Start") or {}).get("HeroName") or c["Id"] for c in doc.get("Characters", [])}
    slots = {}
    for character_id, entry in doc["Presentation"].get("Characters", {}).items():
        code = stem(entry.get("Art"))
        if code is not None:
            slots[code] = (names.get(character_id, character_id), "hero")
    return slots


def stem(art):
    return Path(art).stem if art else None


def font(path, size):
    return ImageFont.truetype(path, size)


def wrap(draw, text, face, width):
    lines, line = [], ""
    for word in text.split():
        candidate = f"{line} {word}".strip()
        if line and draw.textlength(candidate, font=face) > width:
            lines.append(line)
            line = word
        else:
            line = candidate
    if line:
        lines.append(line)
    return lines


def block(draw, text, *, top, width, path, size, fill, margin=0, spacing=4):
    """Centre `text` across `width`, wrapped, shrinking the face until the longest WORD fits as well — a word
    longer than the plate is not broken by a wrap, it simply runs off the edge (the same trap the relic shelf
    fell into: a block that fits in height can still be too wide). Returns the y below the last line."""
    room = width - 2 * margin
    face = font(path, size)
    while size > 8 and (
        max((draw.textlength(w, font=face) for w in text.split()), default=0) > room
        or len(wrap(draw, text, face, room)) * (size + spacing) > width
    ):
        size -= 2
        face = font(path, size)
    y = top
    for line in wrap(draw, text, face, room):
        draw.text(((width - draw.textlength(line, font=face)) / 2, y), line, font=face, fill=fill)
        y += size + spacing
    return y


def plate(size, ground=GROUND):
    """A plate with a hairline so the picture's edge is visible against whatever it is drawn on."""
    image = Image.new("RGBA", size, (*ground, 255))
    draw = ImageDraw.Draw(image)
    draw.rectangle([0, 0, size[0] - 1, size[1] - 1], outline=(*DIM, 255), width=max(2, size[0] // 160))
    return image, draw


def save(image, path):
    """A plate is two inks on one ground, so it is written with a 32-colour palette — a quarter of the bytes
    of the same picture in full colour, and 708 of them are going to sit in the repository until the real
    ones arrive. The mark rides in a text chunk, which a palette PNG carries just as well."""
    info = PngInfo()
    info.add_text(MARK, "1")
    path.parent.mkdir(parents=True, exist_ok=True)
    image.quantize(colors=32, method=Image.FASTOCTREE).save(path, "PNG", pnginfo=info, optimize=True)


def is_placeholder(path):
    try:
        with Image.open(path) as image:
            return MARK in (image.text or {})
    except Exception:
        return False


def run(kind, slots, paint, args, siblings=()):
    """The loop all three generators share: one file per slot, an existing picture left alone unless told
    otherwise, and `--clean` which removes only what carries the mark.

    `siblings` are slot codes a generator paints under a DIFFERENT kind (the bodies script paints the player's
    own body into `characters/`). A `--only` code that belongs to a sibling is not a typo, it is simply not
    this call's business — without that distinction one generator with two folders could never be told to
    redraw a single slot.
    """
    out = Path(args.out) if args.out else ART / kind
    codes = sorted(slots) if not args.only else [c for c in sorted(slots) if c in set(args.only)]
    if args.only:
        missing = sorted(set(args.only) - set(codes) - set(siblings))
        if missing:
            raise SystemExit(f"no {kind} slot named: {', '.join(missing)}")
        if not codes:
            return

    if args.clean:
        gone = [p for p in sorted(out.glob("*.png")) if is_placeholder(p)]
        for path in gone:
            path.unlink()
        kept = len(list(out.glob("*.png"))) if out.exists() else 0
        print(f"{kind}: removed {len(gone)} placeholder(s), {kept} painted picture(s) left in {out}")
        return

    written = skipped = 0
    for code in codes:
        path = out / f"{code}.png"
        if path.exists() and not args.force and not is_placeholder(path):
            skipped += 1
            continue
        save(paint(code, slots[code]), path)
        written += 1
    print(f"{kind}: wrote {written} placeholder(s) of {len(codes)} slot(s) into {out}"
          + (f", left {skipped} painted picture(s) alone" if skipped else ""))
    print("run tools/import-art.sh once — Godot only shows a file the importer has seen")


def arguments(parser):
    parser.add_argument("--only", nargs="+", metavar="CODE", help="just these slot codes")
    parser.add_argument("--out", metavar="DIR", help="write somewhere else than assets/art/<kind>")
    parser.add_argument("--force", action="store_true", help="overwrite a painted picture as well")
    parser.add_argument("--clean", action="store_true", help="delete the placeholders again (marked files only)")
    parser.add_argument("--blueprint", metavar="FILE", help="read a different game document")
    return parser
