#!/usr/bin/env python3
"""The card paper: yellowed sheets the card face is written on, one picked per card.

    python3 assets/cards/paper/make_paper.py                  # writes the KEPT sheets next to itself, at SCALE
    python3 assets/cards/paper/make_paper.py --all --scale 1  # all twenty candidates, as they were first shown
    python3 assets/cards/paper/make_paper.py --sheet out.png  # and a contact sheet of what was written

Nothing here is a scan or a photograph: every sheet is arithmetic on the pixels, in layers.
    1  a base tone, the same yellowed rag paper for every card (rarity is the TITLE'S ink, not the paper)
    2  large soft clouds — the paper yellowed unevenly
    3  small soft clouds — the fibre
    4  per-pixel grain
    5  the edges browned — blue is taken out faster than red, so the edge goes brown and not grey
    6  foxing: a few small rust spots (gaussian bells, again stronger in blue)
    7  sometimes a fold (a bright ridge beside its own shadow) and sometimes a damp ring where a cup stood
    8  a cut edge, slightly ragged: a soft rectangle with noise on it, cut hard again — that is the alpha

The soft clouds are all one trick (`soft_noise`): a tiny grid of random numbers, blown up bicubically to the
sheet's size. The grid's cell size is how big the clouds are; the amplitude is how strong.

REPRODUCIBLE: sheet i is drawn from the seed MASTER_SEED * 100 + i, so running this again draws the same
twenty sheets pixel for pixel. Change MASTER_SEED for a new set. Needs numpy and Pillow.

The size is four times the 134 x 190 card of the design canvas, i.e. a hand card (x1.25) on a 4K screen (x3)
with room to spare; Godot's mipmaps do the reduction.
"""
import argparse
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

MASTER_SEED = 1739
# The sheets the user kept on 2026-11-09 (01, 03, 11, 14, 15 and 20 went out). A sheet keeps its number for
# good, because the number is its seed.
KEEP = [2, 4, 5, 6, 7, 8, 9, 10, 12, 13, 16, 17, 18, 19]
SCALE = 2                  # what is written: 1072 x 1520, twice the 4K hand card again
SIZE = (536, 760)          # 4 x (134 x 190)
TONE = (224, 210, 178)     # the yellowed rag paper
HERE = Path(__file__).resolve().parent


def soft_noise(rng, w, h, cell, amp):
    """Soft clouds of `cell` pixels OF THE BASE SIZE, spanning ±amp/2. The random grid is always the base
    size's grid, so a sheet drawn larger is the same sheet, only sharper."""
    bw, bh = SIZE
    n = rng.normal(0, 1, (max(2, bh // cell), max(2, bw // cell)))
    n = (n - n.min()) / (n.max() - n.min() + 1e-9)
    im = Image.fromarray((n * 255).astype('uint8')).resize((w, h), Image.BICUBIC)
    return (np.asarray(im, float) / 255 - 0.5) * amp


def sheet(seed, scale=1):
    """One sheet of paper and what happened to it, from one seed, at `scale` times SIZE."""
    rng = np.random.default_rng(seed)
    bw, bh = SIZE
    w, h = bw * scale, bh * scale
    yy, xx = np.mgrid[0:h, 0:w]
    traits = []

    # 1  base tone, nudged a little per sheet so no two come out of the same ream
    tone = np.array(TONE, float) + rng.uniform(-5, 5) + rng.uniform(-3, 3, 3)
    base = np.ones((h, w, 3)) * tone
    # 2, 3  clouds
    base += soft_noise(rng, w, h, max(4, bw // 5), rng.uniform(26, 40))[..., None]
    base += soft_noise(rng, w, h, max(2, bw // 30), rng.uniform(6, 12))[..., None]
    # 4  grain: the base size's grain, drawn from the same stream so everything after it lands where it did,
    #    and below it a finer grain of its own for the extra pixels
    grain = rng.normal(0, 3.2, (bh, bw))
    if scale > 1:
        g = Image.fromarray(((grain + 16) * 8).clip(0, 255).astype('uint8')).resize((w, h), Image.BICUBIC)
        grain = np.asarray(g, float) / 8 - 16
        grain = grain * 0.6 + np.random.default_rng(seed + 7_000_000).normal(0, 2.4, (h, w))
    base += grain[..., None]
    # 5  browned edges
    d = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy)) / (0.10 * min(w, h))
    edge = np.clip(1 - d, 0, 1) ** 1.6
    base -= edge[..., None] * np.array([34, 48, 66]) * rng.uniform(0.8, 1.3)
    # 6  foxing
    spots = int(rng.integers(2, 11))
    for _ in range(spots):
        cx, cy = rng.uniform(0, w), rng.uniform(0, h)
        r = rng.uniform(0.004, 0.018) * w
        spot = np.exp(-((xx - cx) ** 2 + (yy - cy) ** 2) / (r * r)) * rng.uniform(14, 34)
        base -= spot[..., None] * np.array([0.55, 0.9, 1.35])
    traits.append(f'{spots} Flecken')
    # 7  a fold, a ring
    if rng.random() < 0.3:
        vertical = rng.random() < 0.6
        if vertical:
            dist = xx - rng.uniform(0.35, 0.65) * w - (yy - h / 2) * rng.uniform(-0.08, 0.08)
        else:
            dist = yy - rng.uniform(0.3, 0.7) * h - (xx - w / 2) * rng.uniform(-0.08, 0.08)
        base += (np.exp(-(dist / (0.004 * w)) ** 2) * 14
                 - np.exp(-((dist - 0.008 * w) / (0.006 * w)) ** 2) * 12)[..., None]
        traits.append('Knick ' + ('längs' if vertical else 'quer'))
    if rng.random() < 0.2:
        cx, cy, rr = rng.uniform(0.2, 0.8) * w, rng.uniform(0.15, 0.85) * h, rng.uniform(0.18, 0.28) * w
        rad = np.hypot(xx - cx, yy - cy)
        ring = np.exp(-((rad - rr) / (0.012 * w)) ** 2) \
            * (0.6 + 0.4 * np.cos(np.arctan2(yy - cy, xx - cx) * 2 + rng.uniform(0, 6)))
        base -= ring[..., None] * np.array([10, 16, 24])
        traits.append('Wasserring')
    rgb = Image.fromarray(np.clip(base, 0, 255).astype('uint8'), 'RGB')

    # 8  the cut edge
    m = Image.new('L', (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle((w * 0.012, h * 0.009, w * 0.988, h * 0.991), radius=w * 0.03, fill=255)
    m = m.filter(ImageFilter.GaussianBlur(w * 0.006))
    a = np.asarray(m, float) + soft_noise(rng, w, h, max(2, bw // 40), rng.uniform(110, 170))
    a = np.clip((a - 128) * 4 + 128, 0, 255).astype('uint8')
    rgb.putalpha(Image.fromarray(a).filter(ImageFilter.GaussianBlur(0.6 * scale)))
    return rgb, traits


def contact_sheet(images, out, cols=5):
    w, h = images[0][1].size
    tw, th = w // 2, h // 2
    gap, head = 24, 34
    rows = math.ceil(len(images) / cols)
    page = Image.new('RGBA', (cols * (tw + gap) + gap, rows * (th + head + gap) + gap), (8, 4, 6, 255))
    draw = ImageDraw.Draw(page)
    try:
        label = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf', 17)
    except OSError:
        label = ImageFont.load_default()
    for i, (name, img, traits) in enumerate(images):
        x = gap + (i % cols) * (tw + gap)
        y = gap + (i // cols) * (th + head + gap)
        draw.text((x, y), f'{name[6:8]}  ' + ', '.join(traits), font=label, fill=(215, 200, 185))
        page.alpha_composite(img.resize((tw, th), Image.LANCZOS), (x, y + head))
    page.convert('RGB').save(out)


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--all', action='store_true', help='all twenty candidates, not only the kept ones')
    ap.add_argument('--scale', type=int, default=SCALE)
    ap.add_argument('--sheet', help='also write a contact sheet of every paper here')
    args = ap.parse_args()
    made = []
    for i in (range(1, 21) if args.all else KEEP):
        img, traits = sheet(MASTER_SEED * 100 + i, args.scale)
        name = f'paper_{i:02d}.png'
        img.save(HERE / name, optimize=True)
        made.append((name, img, traits))
        print(name, ', '.join(traits))
    if args.sheet:
        contact_sheet(made, args.sheet)
        print(args.sheet)
