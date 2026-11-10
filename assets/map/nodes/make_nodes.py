#!/usr/bin/env python3
"""The map's rooms: one round token per kind of room, writes <role>.png next to itself.

    python3 assets/map/nodes/make_nodes.py

A token is a paper disc in a wobbly ink ring of the room's colour, with the room's symbol inked in the middle.
The symbols are silhouettes from game-icons.net (src/, CC BY 3.0, see LICENSE.txt), chosen by the user
2026-11-10 from a board of three or four per kind. Deterministic: the same script writes the same tokens.
Needs numpy and Pillow.
"""
import math, random, zlib
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
SIZE = 96            # the token's canvas; it is drawn at half that on the map (44 px disc, 2 px air)
DISC = 88
SUPER = 4            # the ring is drawn this many times larger and shrunk, for a smooth line
INK = (43, 29, 20)
PAPER = (244, 232, 205)

# the ring's colour is the room's colour on the map: kept in step with MapView.RoleColor
ROLES = {
    'combat': 'b4553f',
    'multi-combat': 'c9633a',
    'elite': 'd8443f',
    'boss': 'e0313c',
    'shop': 'c9a227',
    'rest': '7fa8ce',
    'treasure': 'e9d18a',
    'event': 'b98fd0',
}


def inked(role, size):
    """The symbol as an ink mask `size` across: the silhouette shrunk, its edge roughened as if inked."""
    a = Image.open(HERE / 'src' / f'{role}.png').convert('RGBA').getchannel('A')
    a = a.crop(a.getbbox())
    w, h = a.size
    s = size / max(w, h)
    a = a.resize((max(1, round(w * s * 2)), max(1, round(h * s * 2))), Image.LANCZOS)
    rng = np.random.default_rng(zlib.crc32(role.encode()))
    arr = np.asarray(a, dtype=np.float32) / 255
    noise = Image.fromarray((rng.random(arr.shape) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))
    noise = np.asarray(noise, dtype=np.float32) / 255
    arr = np.clip((arr - 0.5 + (noise - 0.5) * 0.5) * 3 + 0.5, 0, 1)
    return Image.fromarray((arr * 255).astype(np.uint8)).resize((round(w * s), round(h * s)), Image.LANCZOS)


def token(role, ring):
    rng = random.Random(role)
    big = SIZE * SUPER
    d = DISC * SUPER
    c = big / 2
    col = tuple(int(ring[i:i + 2], 16) for i in (0, 2, 4))

    def wobble(r, amp, n=120):
        """A circle drawn by hand: three slow waves on the radius."""
        ph = [rng.uniform(0, 2 * math.pi) for _ in range(3)]
        pts = []
        for i in range(n + 1):
            t = i / n * 2 * math.pi
            rr = r + amp * (0.6 * math.sin(2 * t + ph[0]) + 0.3 * math.sin(3 * t + ph[1]) + 0.2 * math.sin(5 * t + ph[2]))
            pts.append((c + rr * math.cos(t), c + rr * math.sin(t)))
        return pts

    im = Image.new('RGBA', (big, big), (0, 0, 0, 0))
    dr = ImageDraw.Draw(im)
    rim = round(d * 0.075)
    dr.polygon(wobble(d / 2 - rim / 2, d * 0.012), fill=PAPER + (255,))
    dr.line(wobble(d / 2 - rim / 2, d * 0.015), fill=col + (255,), width=rim, joint='curve')
    dr.line(wobble(d / 2 - rim / 2 - d * 0.1, d * 0.012), fill=col + (170,), width=round(d * 0.022), joint='curve')
    im = im.resize((SIZE, SIZE), Image.LANCZOS)
    sym = inked(role, round(DISC * 0.56))
    ink = Image.new('RGBA', sym.size, INK + (255,))
    ink.putalpha(sym)
    im.alpha_composite(ink, (round(SIZE / 2 - sym.width / 2), round(SIZE / 2 - sym.height / 2)))
    return im


if __name__ == '__main__':
    for role, ring in ROLES.items():
        token(role, ring).save(HERE / f'{role}.png')
        print('wrote', role)
