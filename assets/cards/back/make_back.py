#!/usr/bin/env python3
"""The card back: an ink whirl drawn on the back of a paper sheet, with B&B written in its calm eye.

    python3 assets/cards/back/make_back.py          # writes card_back.png next to itself
    python3 assets/cards/back/make_back.py --ss 4   # smaller, for a quick look

What is drawn, in order:
    1  the sheet: paper_12 from ../paper, MIRRORED, because this is the other side of a card's paper
    2  the whirl: pen strokes on log spirals, r = r0 * exp(-b * theta), each swelling where the nib was
       pressed (width follows sin(pi * u)), heavier at the rim than near the eye, and stopping short of a
       calm round middle. They run off every edge; the paper's own ragged cut ends them.
    3  B&B in the Clerk of Boston hand, written by a scratchy split quill:
         - the thick strokes part down the middle into two rails (the stroke's eroded core gets less ink)
         - the ink skips in streaks along the pen's angle (noise stretched long and turned to 38 degrees)
         - the edge is torn by the fibre (fine noise on the edge, then cut hard again)
         - hairline scratches where the nib's points caught, spatter where it flicked off
    4  blots above and below the name, never on it (it must stay readable): a fat drop, a crown of satellites (the near ones still joined to it), and
       sometimes a run where it slid. Hard-edged, because ink dries where it lands.
    5  ink laid like ink on rag paper: the density wanders a little across the sheet

Nothing is animated: the back is one still. REPRODUCIBLE from SEED; needs numpy and Pillow.
The size is the paper's, 1072 x 1520 = 8 x the 134 x 190 card; Godot's mipmaps do the reduction.
"""
import argparse
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

SEED = 31
CARD = (134, 190)
SS = 8                        # pixels per design-canvas unit: 8 matches the 1072 x 1520 paper
PAPER_SHEET = 12
INK = (40, 26, 18)            # the clerk's brown-black, the same as the card faces
PEN_ANGLE = 38                # degrees: the slant the quill was held at
HERE = Path(__file__).resolve().parent
PAPER = HERE.parent / 'paper' / f'paper_{PAPER_SHEET:02d}.png'
FONT = HERE.parents[2] / 'theme' / 'font' / 'clerk-of-boston.ttf'


def arr(im):
    return np.asarray(im, float)


def blurnoise(rng, w, h, cell, amp):
    """Soft noise: a small random grid blown up bicubically; `cell` is the blob size, ±amp/2 its range."""
    n = rng.normal(0, 1, (max(2, h // cell), max(2, w // cell)))
    n = (n - n.min()) / (n.max() - n.min() + 1e-9)
    im = Image.fromarray((n * 255).astype('uint8')).resize((w, h), Image.BICUBIC)
    return (arr(im) / 255 - 0.5) * amp


def ink_layer(card, alpha, color, rng, density=(0.88, 1.0)):
    """Lay `alpha` (0..1) on the card in `color`, with the uneven density of ink on rag paper."""
    w, h = card.size
    var = np.clip(blurnoise(rng, w, h, max(2, w // 25), 1.0) + 0.5, 0, 1)
    var = density[0] + (density[1] - density[0]) * var
    layer = Image.new('RGBA', (w, h), color + (0,))
    layer.putalpha(Image.fromarray((np.clip(alpha, 0, 1) * var * 255).astype('uint8')))
    card.alpha_composite(layer)


def spiral(w, h, rng, ss, eye, strokes):
    """The whirl's pen strokes, as a 0..1 mask; `eye` is the calm middle's radius as a share of the width."""
    cx, cy = w / 2, h / 2
    m = Image.new('L', (w, h), 0)
    d = ImageDraw.Draw(m)
    rmax = 0.78 * h
    for _ in range(strokes):
        th0 = rng.uniform(0, 2 * math.pi)
        r0 = rmax * rng.uniform(0.3, 1.0)
        b = rng.uniform(0.15, 0.24)
        th = np.linspace(0, rng.uniform(1.2, 4.5), 220)
        r = r0 * np.exp(-b * th) * (1 + 0.025 * np.sin(th * rng.uniform(3, 7) + rng.uniform(0, 6)))
        keep = r > eye * w * rng.uniform(0.95, 1.25)
        if keep.sum() < 8:
            continue
        r, ang = r[keep], th0 + th[keep]
        pts = list(zip(cx + r * np.cos(ang), cy + r * np.sin(ang) * 1.08))
        for j in range(0, len(pts) - 6, 5):
            u = j / len(pts)
            wd = max(1, int(ss * (0.2 + 0.7 * math.sin(math.pi * u) ** 1.5) * (1.25 - r[j] / rmax)))
            d.line(pts[j:j + 6], fill=255, width=wd)
    return arr(m) / 255


def streaks(rng, w, h, length, amp):
    """Noise drawn out long and turned to the pen's angle: the dry streaks of a scratchy quill."""
    n = rng.normal(0, 1, (max(2, h // 2), max(2, w // length)))
    im = Image.fromarray(((n - n.min()) / (n.max() - n.min()) * 255).astype('uint8')).resize((w * 2, h * 2), Image.BICUBIC)
    im = im.rotate(PEN_ANGLE, resample=Image.BICUBIC).crop((w // 2, h // 2, w // 2 + w, h // 2 + h))
    return (arr(im) / 255 - 0.5) * amp


def scratchy_logo(w, h, rng, ss, size=0.30, text='B&B'):
    glyph = Image.new('L', (w, h), 0)
    ImageDraw.Draw(glyph).text((w / 2, h / 2), text, font=ImageFont.truetype(str(FONT), int(size * w)), fill=255, anchor='mm')
    glyph = glyph.filter(ImageFilter.MaxFilter(2 * int(0.4 * ss) + 1))            # a broad nib, pressed hard
    g = arr(glyph.filter(ImageFilter.GaussianBlur(0.3 * ss))) / 255
    # the split nib: where the stroke is thick, its middle carries less ink
    core = arr(glyph.filter(ImageFilter.MinFilter(2 * int(0.9 * ss) + 1)).filter(ImageFilter.GaussianBlur(0.5 * ss))) / 255
    a = g * (1 - 0.65 * core * (0.5 + streaks(rng, w, h, 140, 1.6)).clip(0, 1))
    # dry streaks along the stroke, and the fibre tearing the edge
    a = a * (0.8 + streaks(rng, w, h, 110, 1.6)).clip(0, 1) ** 0.6
    a = a + blurnoise(rng, w, h, max(2, int(0.5 * ss)), 0.5) * (g > 0.05)
    a = np.clip((a - 0.25) / 0.4, 0, 1)
    out = Image.fromarray((a * 255).astype('uint8'))
    d = ImageDraw.Draw(out)
    ys, xs = np.nonzero(arr(glyph) > 128)
    pa = math.radians(-PEN_ANGLE)
    for _ in range(26):                     # hairline scratches, short and straight, at the pen's angle
        i = int(rng.integers(len(xs)))
        L = rng.uniform(0.02, 0.09) * w
        a2 = pa + rng.normal(0, 0.15) + (math.pi if rng.random() < 0.4 else 0)
        d.line((xs[i], ys[i], xs[i] + math.cos(a2) * L, ys[i] + math.sin(a2) * L),
               fill=int(rng.uniform(120, 230)), width=max(1, ss // 6))
    for _ in range(70):                     # spatter, thrown along the pen's way
        i = int(rng.integers(len(xs)))
        dist = abs(rng.normal(0, 0.05)) * w
        a2 = pa + rng.normal(0, 0.5) + (math.pi if rng.random() < 0.5 else 0)
        px, py = xs[i] + math.cos(a2) * dist, ys[i] + math.sin(a2) * dist
        q = rng.uniform(0.15, 1.0) * ss * (1.3 if rng.random() < 0.1 else 0.6)
        d.ellipse((px - q, py - q, px + q, py + q), fill=255)
    return arr(out) / 255


def blot(w, h, rng, ss, bx, by, br):
    m = Image.new('L', (w, h), 0)
    d = ImageDraw.Draw(m)
    d.ellipse((bx - br, by - br * rng.uniform(0.75, 1.0), bx + br, by + br), fill=255)
    for _ in range(int(rng.integers(5, 12))):
        a = rng.uniform(0, 2 * math.pi); L = br * rng.uniform(0.8, 2.4); q = br * rng.uniform(0.08, 0.35)
        px, py = bx + math.cos(a) * L, by + math.sin(a) * L
        d.ellipse((px - q, py - q, px + q, py + q), fill=255)
        if L < 1.5 * br:                    # the near ones are still joined to the body
            d.line((bx, by, px, py), fill=255, width=int(q * 1.4))
    if rng.random() < 0.5:                  # it ran a little
        a = rng.uniform(0, 2 * math.pi); L = br * rng.uniform(1.5, 3)
        d.line((bx, by, bx + math.cos(a) * L, by + math.sin(a) * L), fill=255, width=int(br * 0.35))
    m = arr(m.filter(ImageFilter.GaussianBlur(br * 0.18))) + blurnoise(rng, w, h, max(2, int(0.6 * ss)), 60)
    return np.clip((m - 110) / 40, 0, 1)


def back(ss=SS, seed=SEED):
    w, h = CARD[0] * ss, CARD[1] * ss
    im = Image.open(PAPER).convert('RGBA').resize((w, h), Image.LANCZOS).transpose(Image.FLIP_LEFT_RIGHT)
    rng = np.random.default_rng(seed)
    whirl = spiral(w, h, rng, ss, eye=0.25, strokes=560)
    logo = scratchy_logo(w, h, rng, ss)
    # the blots fall above and below the name, never on it: a blot that would touch the letters (or come
    # within a nib's width of them) is not kept, and another is dropped in its place
    keep_clear = Image.fromarray((logo > 0.1).astype('uint8') * 255).filter(ImageFilter.MaxFilter(2 * int(1.5 * ss) + 1))
    keep_clear = arr(keep_clear) > 0
    mess = np.zeros_like(logo)
    for k in range(7):
        for _ in range(60):
            # turn about: one above the name, the next below
            a = rng.uniform(math.pi, 2 * math.pi) if k % 2 == 0 else rng.uniform(0, math.pi)
            r = rng.uniform(0.04, 0.22) * w
            br = rng.uniform(0.012, 0.03) * w * (1.6 if k < 2 else 1.0)
            b = blot(w, h, rng, ss, w / 2 + math.cos(a) * r * 1.2, h / 2 + math.sin(a) * r, br)
            if not (b[keep_clear] > 0.05).any():
                mess = np.maximum(mess, b)
                break
    ink_layer(im, whirl, INK, rng, (0.88, 1.0))
    ink_layer(im, np.maximum(logo, mess * 0.95), INK, rng, (0.8, 1.0))
    # the paper's own cut is the card's edge: nothing drawn may stand outside it
    im.putalpha(Image.open(PAPER).getchannel('A').resize((w, h), Image.LANCZOS).transpose(Image.FLIP_LEFT_RIGHT))
    return im


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--ss', type=int, default=SS)
    ap.add_argument('--out', default=str(HERE / 'card_back.png'))
    args = ap.parse_args()
    back(args.ss).save(args.out, optimize=True)
    print(args.out)
