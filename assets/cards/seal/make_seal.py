#!/usr/bin/env python3
"""The wax seal on an upgraded card: red sealing wax, poured and pressed with a cross pattee.

    python3 assets/cards/seal/make_seal.py          # writes seal_01.png ... seal_06.png next to itself
    python3 assets/cards/seal/make_seal.py --r 64   # smaller

No two pours of wax are alike, so there are COUNT seals and the game picks one per card, fixed by card id,
the way it picks the paper. Each PNG is the wax AND the shadow it throws, on transparency, centred: lay it
on the card with its centre at (0.855 w, 0.083 h) and scale it so R is 0.085 of the card's width.

The seal is not painted, it is LIT. It is built as a height map and a lamp shines on it:
    1  the poured outline: a wobbling circle, plus two or three tongues where the wax ran
    2  the dome: thick in the middle, thin at the rim (T = 0.22 R high)
    3  the matrix pressed in, a little off centre: a flat floor inside, and the wax squeezed up into a
       ridge round it
    4  what was cut into the matrix stands up out of the wax: the cross pattee (it reads as a "+") and a
       ring of beads, softened, at 0.32 T
    5  normals from the slope (np.gradient), a lamp at the top left: diffuse light plus a glossy highlight
    6  colour by thickness: deep red where the wax is thick, lighter where it is thin and light comes back
       through it
    7  a soft shadow on the paper, down and to the right of the lamp

REPRODUCIBLE: seal i is drawn from SEED * 100 + i. Needs numpy and Pillow.
"""
import argparse
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SEED = 1740
COUNT = 6
R = 128                       # the wax's radius in pixels; the card paper at 1072 wide wants about 91
HERE = Path(__file__).resolve().parent
WAX = (132, 18, 24)           # thick
THIN = (196, 52, 44)          # thin, lit from behind
LAMP = (-0.55, -0.6, 0.58)    # the same lamp as every other lit thing on the card


def blurnoise(rng, w, h, cell, amp):
    n = rng.normal(0, 1, (max(2, h // cell), max(2, w // cell)))
    n = (n - n.min()) / (n.max() - n.min() + 1e-9)
    im = Image.fromarray((n * 255).astype('uint8')).resize((w, h), Image.BICUBIC)
    return (np.asarray(im, float) / 255 - 0.5) * amp


def motif(d, cx, cy, s):
    """The matrix: a cross pattee — four arms flaring out from a round boss — inside a ring of beads."""
    for k in range(4):
        a = k * math.pi / 2
        ca, sa = math.cos(a), math.sin(a)
        pts = [(0.12, 0.10), (0.78, 0.30), (0.78, -0.30), (0.12, -0.10)]
        d.polygon([(cx + s * (x * ca - y * sa), cy + s * (x * sa + y * ca)) for x, y in pts], fill=255)
    d.ellipse((cx - 0.16 * s, cy - 0.16 * s, cx + 0.16 * s, cy + 0.16 * s), fill=255)
    for k in range(28):
        a = k / 28 * 2 * math.pi
        px, py = cx + math.cos(a) * s * 1.18, cy + math.sin(a) * s * 1.18
        q = 0.045 * s
        d.ellipse((px - q, py - q, px + q, py + q), fill=255)


def seal(seed, R=R):
    rng = np.random.default_rng(seed)
    soft = R / 11.4                            # one design-canvas unit at this size
    pad = int(R * 1.9)
    n = 2 * pad
    yy, xx = np.mgrid[0:n, 0:n].astype(float)
    lx, ly = xx - pad, yy - pad
    th, rr = np.arctan2(ly, lx), np.hypot(lx, ly)
    # 1  the outline
    edge = np.ones_like(th)
    for k in range(2, 8):
        edge += rng.uniform(-0.05, 0.05) * np.cos(k * th + rng.uniform(0, 6.28))
    for _ in range(int(rng.integers(2, 4))):
        c, wdt = rng.uniform(-math.pi, math.pi), rng.uniform(0.12, 0.25)
        edge += rng.uniform(0.1, 0.22) * np.exp(-(np.angle(np.exp(1j * (th - c))) / wdt) ** 2)
    inside = edge * R - rr
    # 2  the dome
    T = 0.22 * R
    h = T * np.clip(inside / (0.45 * R), 0, 1) ** 0.6
    # 3  the matrix: a flat floor and a squeezed ridge
    mx, my = rng.uniform(-0.06, 0.06) * R, rng.uniform(-0.06, 0.06) * R
    rm, Rm = np.hypot(lx - mx, ly - my), 0.66 * R
    inmat = rm < Rm
    h = np.where(inmat, np.minimum(h, T * 0.55), h)
    h += T * 0.35 * np.exp(-((rm - Rm * 1.04) / (0.07 * R)) ** 2) * (inside > 0)
    # 4  the relief
    mm = Image.new('L', (n, n), 0)
    motif(ImageDraw.Draw(mm), pad + mx, pad + my, Rm * 0.7)
    h += np.asarray(mm.filter(ImageFilter.GaussianBlur(0.035 * R)), float) / 255 * T * 0.32 * inmat
    h += blurnoise(rng, n, n, max(2, int(0.08 * R)), T * 0.05)
    # 5  the light
    gy, gx = np.gradient(h)
    nrm = np.dstack([-gx, -gy, np.ones_like(h)])
    nrm /= np.linalg.norm(nrm, axis=2, keepdims=True)
    L = np.array(LAMP) / np.linalg.norm(LAMP)
    Hv = (L + [0, 0, 1]) / np.linalg.norm(L + [0, 0, 1])
    diff = np.clip(nrm @ L, 0, 1)
    spec = np.clip(nrm @ Hv, 0, 1) ** 38
    # 6  the colour
    thick = np.clip(h / T, 0, 1)[..., None]
    col = np.array(WAX) * thick + np.array(THIN) * (1 - thick)
    col = col * (0.3 + 0.95 * diff[..., None]) + spec[..., None] * np.array([255, 225, 215]) * 0.75
    alpha = np.clip(inside / (0.5 * soft), 0, 1)
    # 7  the shadow, then the wax over it
    out = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    shadow = Image.new('RGBA', (n, n), (35, 18, 8, 0))
    shadow.putalpha(Image.fromarray((alpha * 150).astype('uint8')).filter(ImageFilter.GaussianBlur(0.9 * soft)))
    out.alpha_composite(shadow, (int(0.7 * soft), int(0.9 * soft)))
    out.alpha_composite(Image.fromarray(np.dstack([np.clip(col, 0, 255), alpha * 255]).astype('uint8'), 'RGBA'))
    return out


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--r', type=int, default=R)
    args = ap.parse_args()
    for i in range(1, COUNT + 1):
        out = HERE / f'seal_{i:02d}.png'
        seal(SEED * 100 + i, args.r).save(out, optimize=True)
        print(out)
