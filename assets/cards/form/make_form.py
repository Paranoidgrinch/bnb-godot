#!/usr/bin/env python3
"""The printed card form and the stamps struck on it, as layers the game lays over a sheet of card paper.

    python3 assets/cards/form/make_form.py                    # every layer, and layout.json, next to itself
    python3 assets/cards/form/make_form.py --preview out.png  # and the layers put together, without the writing

Two LOOKS, decided 2026-11-09:
    office   the clerk's own cards (character and general pool): the form printed in brown-black from a hand
             press, a border of brass rules and printer's flowers with a rosette in each corner
    notice   every card forced on the player (junk, curses, fight cards): the office's red notice — the
             paper tinted rose (the game multiplies the sheet by NOTICE_TINT), the form printed in red with
             plain rules, the cost stamp in red, and SERVED struck across the picture box

What is written in, the game writes with the Clerk of Boston font: the cost into the stamp, the title on
its rule, the kind of card, the text, the clerk's underlines and corrections. Everything here is what a
printer or a stamp put there before the clerk ever saw the card.

How a layer is printed (`press`): the shape is drawn sharp, then
    - its edge is softened and roughened with fine noise and cut again — ink running into the fibre
    - the rim of every stroke is denser than its middle — ink squashed out under the type
    - the density wanders across the sheet — a forme never inked evenly
    - small holes where the ink did not reach
    - the type's bite: a soft shadow on one side of every stroke and a lit edge on the other
The forme sat a little crooked in the press: the game turns each card's form by up to half a degree and
shifts it by a fraction of a unit, fixed per card id (layout.json says how much).

The stamp is a brass hand stamp: a twisted cable between two rings and a fine inner ring, inked from a pad
heavier on the side the clerk leaned on, with a faint second strike where it bounced. Its ink follows the
rarity like the title's: brown-black for starter and common, verdigris for uncommon, red for rare, and red
on a notice.

Files (all RGBA, transparent where there is no ink; the form at the paper's 1072 x 1520):
    form_<look>_<n>.png         the whole printed form except the title rule (n = 1..FORMS, picked per card)
    title_<look>_<long|short>.png   the swelled rule the title sits on: short when a wax seal takes the corner
    stamp_<ink>_<n>.png         the cost stamp, square, centred on the stamp
    served_<n>.png              SERVED, square, centred on its stamp
    layout.json                 where every piece goes, as fractions of the card, and the notice tint

REPRODUCIBLE from SEED. Needs numpy, Pillow, and DejaVu Serif Bold for the SERVED letters.
"""
import argparse
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

SEED = 1741
FORMS = 3                     # forms per look; stamps and SERVED come in as many
CARD = (134, 190)
SS = 8                        # pixels per design-canvas unit: 1072 x 1520, the paper's size
HERE = Path(__file__).resolve().parent
SERIF_BOLD = '/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf'

LOOKS = {
    'office': dict(print=(92, 72, 58), border='flowers', art=(0.105, 0.145, 0.895, 0.585), title_end=0.86),
    'notice': dict(print=(158, 52, 44), border='rules', art=(0.085, 0.145, 0.915, 0.585), title_end=0.93),
}
PRINT_DARK = (40, 30, 26)     # what the press ink deepens towards where it lies thick
TITLE_END_SEALED = 0.80       # a sealed (upgraded) card's title rule stops short of the seal
INKS = {                      # the stamp pad: (ink, where it lies thick)
    'brown': ((58, 40, 30), (22, 14, 10)),
    'green': ((34, 96, 64), (14, 50, 32)),
    'red': ((150, 36, 28), (95, 20, 22)),
}
NOTICE_TINT = (0.99, 0.80, 0.83)
SERVED_INK = (150, 30, 26)

LAYOUT = {
    'card': CARD,
    'notice_paper_tint': NOTICE_TINT,
    'form_jitter': {'degrees': 0.5, 'units': 0.6},
    'title_rule_y': 0.118,                        # centre of the title rule strip, fraction of the height
    'title_strip_height': 0.03,
    'title_box': {'x0': 0.25, 'end': {k: v['title_end'] for k, v in LOOKS.items()}, 'end_sealed': TITLE_END_SEALED,
                  'baseline': 0.108},
    'art_box': {k: v['art'] for k, v in LOOKS.items()},
    'type_line_y': 0.625,
    'text_box': (0.10, 0.655, 0.90, 0.935),
    'stamp': {'cx': 0.145, 'cy': 0.075, 'r': 0.105, 'image_r': 1.3,
              'ink_by_rarity': {'starter': 'brown', 'common': 'brown', 'uncommon': 'green', 'rare': 'red',
                                'notice': 'red'}},
    'served': {'cx': 0.5, 'cy': 0.37, 'size': 0.86},
    'seal': {'cx': 0.855, 'cy': 0.083, 'r': 0.085},
}


def arr(im):
    return np.asarray(im, float)


def blurnoise(rng, w, h, cell, amp):
    n = rng.normal(0, 1, (max(2, h // cell), max(2, w // cell)))
    n = (n - n.min()) / (n.max() - n.min() + 1e-9)
    im = Image.fromarray((n * 255).astype('uint8')).resize((w, h), Image.BICUBIC)
    return (arr(im) / 255 - 0.5) * amp


def smooth(x, a, b):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def press(layer, mask, color, rng, ss, density=(0.55, 1.0), dropout=0.03, bite=0.10, dark=(0, 0, 0)):
    """Print `mask` onto the transparent `layer` like ink from metal type (see the module notes)."""
    w, h = layer.size
    m = arr(mask.filter(ImageFilter.GaussianBlur(0.35 * ss))) + blurnoise(rng, w, h, max(2, int(0.8 * ss)), 120)
    alpha = smooth(m, 60, 140)
    er = arr(Image.fromarray((alpha * 255).astype('uint8')).filter(
        ImageFilter.MinFilter(2 * max(1, int(0.25 * ss)) + 1))) / 255
    rim = np.clip(alpha - er, 0, 1)
    dens = np.clip(blurnoise(rng, w, h, max(4, w // 6), 1.2) + 0.5, 0, 1)
    dens = (density[0] + (density[1] - density[0]) * dens) * (0.78 + 0.22 * rim)
    k = max(1, int(0.45 * ss))
    holes = rng.random((h // k + 1, w // k + 1)) > dropout
    holes = Image.fromarray((holes * 255).astype('uint8')).resize((w, h), Image.NEAREST).filter(
        ImageFilter.GaussianBlur(0.3 * ss))
    a = alpha * dens * (arr(holes) / 255)
    if bite:
        A = Image.fromarray((alpha * 255).astype('uint8'))
        sh = arr(A.transform(A.size, Image.AFFINE, (1, 0, -0.6 * ss, 0, 1, -0.6 * ss)).filter(
            ImageFilter.GaussianBlur(0.7 * ss))) / 255
        hi = arr(A.transform(A.size, Image.AFFINE, (1, 0, 0.6 * ss, 0, 1, 0.6 * ss)).filter(
            ImageFilter.GaussianBlur(0.7 * ss))) / 255
        # on a transparent layer the bite is a faint dark and a faint light, laid over the paper
        for tone, amount in (((30, 20, 10), bite * sh), ((255, 250, 235), 0.5 * bite * hi * (1 - sh))):
            t = Image.new('RGBA', (w, h), tone + (0,))
            t.putalpha(Image.fromarray((np.clip(amount, 0, 1) * 255).astype('uint8')))
            layer.alpha_composite(t)
    col = np.array(color, float) * (1 - 0.35 * dens[..., None]) + np.array(dark, float) * 0.35 * dens[..., None]
    layer.alpha_composite(Image.fromarray(np.dstack([col, np.clip(a, 0, 1) * 255]).astype('uint8'), 'RGBA'))


def rule(d, x0, y0, x1, y1, lw, rng, ss):
    """A brass rule: the pieces never quite meet at the corners."""
    L = math.hypot(x1 - x0, y1 - y0); ux, uy = (x1 - x0) / L, (y1 - y0) / L
    e0, e1 = rng.uniform(-0.7, 0.5) * ss, rng.uniform(-0.7, 0.5) * ss
    d.line((x0 - ux * e0, y0 - uy * e0, x1 + ux * e1, y1 + uy * e1), fill=255, width=max(1, int(round(lw))))


def swelled(d, x0, x1, y, thick, ss, n=60):
    """The swelled rule of 18th-century printing: hairline at the ends, fat in the middle."""
    top, bot = [], []
    for i in range(n + 1):
        u = i / n
        t = 0.12 * ss + thick * math.sin(math.pi * u) ** 1.6
        top.append((x0 + (x1 - x0) * u, y - t / 2)); bot.append((x0 + (x1 - x0) * u, y + t / 2))
    d.polygon(top + bot[::-1], fill=255)


def leaf(d, cx, cy, ang, L, W):
    """One printer's flower: a small leaf on its stalk."""
    pts = []
    for i in range(17):
        u = i / 16
        pts.append((u * L - L / 2, W * math.sin(math.pi * u) ** 0.8 * (1 - 0.35 * u)))
    pts += [(x, -y) for x, y in pts[::-1]]
    ca, sa = math.cos(ang), math.sin(ang)
    d.polygon([(cx + x * ca - y * sa, cy + x * sa + y * ca) for x, y in pts], fill=255)
    d.line((cx - L / 2 * ca, cy - L / 2 * sa, cx - L * 0.75 * ca, cy - L * 0.75 * sa), fill=255, width=max(1, int(W * 0.35)))


def rosette(d, cx, cy, r):
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        leaf(d, cx + math.cos(a) * r * 0.55, cy + math.sin(a) * r * 0.55, a, r, r * 0.32)
    d.ellipse((cx - r * 0.18, cy - r * 0.18, cx + r * 0.18, cy + r * 0.18), fill=255)


def hatch(mask_draw, box, ss):
    """Faint diagonal hatching in the picture box, the engraver's ground under a missing picture."""
    x0, y0, x1, y1 = box
    step = 2.2 * ss
    k = -int((y1 - y0) / step)
    while x0 + k * step <= x1:
        xa = x0 + k * step
        mask_draw.line((xa, y1, xa + (y1 - y0), y0), fill=40, width=max(1, int(0.3 * ss)))
        k += 1


def form(look, seed, ss=SS):
    spec = LOOKS[look]
    rng = np.random.default_rng(seed)
    w, h = CARD[0] * ss, CARD[1] * ss
    m = Image.new('L', (w, h), 0); d = ImageDraw.Draw(m)
    lw = 0.5 * ss
    if spec['border'] == 'flowers':
        o, n = 0.03 * w, 0.072 * w
        for a, b in [((o, o), (w - o, o)), ((w - o, o), (w - o, h - o)), ((w - o, h - o), (o, h - o)), ((o, h - o), (o, o))]:
            rule(d, *a, *b, lw, rng, ss)
        mid, step = (o + n) / 2, 0.052 * w
        def chain(xa, ya, xb, yb):
            k = int(math.hypot(xb - xa, yb - ya) / step)
            ang = math.atan2(yb - ya, xb - xa)
            for j in range(k + 1):
                px = xa + (xb - xa) * j / k + rng.uniform(-0.15, 0.15) * ss
                py = ya + (yb - ya) * j / k + rng.uniform(-0.15, 0.15) * ss
                leaf(d, px, py, ang + (math.pi if j % 2 else 0) + rng.uniform(-0.06, 0.06), step * 0.62, step * 0.2)
        c = 0.07 * w
        chain(mid + c, mid, w - mid - c, mid); chain(mid + c, h - mid, w - mid - c, h - mid)
        chain(mid, mid + c, mid, h - mid - c); chain(w - mid, mid + c, w - mid, h - mid - c)
        for cx, cy in ((mid, mid), (w - mid, mid), (mid, h - mid), (w - mid, h - mid)):
            rosette(d, cx, cy, 0.035 * w)
    else:
        i = 0.04 * w
        for a, b in [((i, i), (w - i, i)), ((w - i, i), (w - i, h - i)), ((w - i, h - i), (i, h - i)), ((i, h - i), (i, i))]:
            rule(d, *a, *b, lw, rng, ss)
    ax0, ay0, ax1, ay1 = (f * s for f, s in zip(spec['art'], (w, h, w, h)))
    g = 0.013 * w
    for a, b in [((ax0, ay0), (ax1, ay0)), ((ax1, ay0), (ax1, ay1)), ((ax1, ay1), (ax0, ay1)), ((ax0, ay1), (ax0, ay0))]:
        rule(d, *a, *b, 1.1 * ss, rng, ss)
    for a, b in [((ax0 + g, ay0 + g), (ax1 - g, ay0 + g)), ((ax1 - g, ay0 + g), (ax1 - g, ay1 - g)),
                 ((ax1 - g, ay1 - g), (ax0 + g, ay1 - g)), ((ax0 + g, ay1 - g), (ax0 + g, ay0 + g))]:
        rule(d, *a, *b, 0.4 * ss, rng, ss)
    q = 0.022 * w
    for cx, cy in ((ax0, ay0), (ax1, ay0), (ax0, ay1), (ax1, ay1)):         # corner blocks
        d.rectangle((cx - q, cy - q, cx + q, cy + q), fill=255)
        d.ellipse((cx - q * 0.45, cy - q * 0.45, cx + q * 0.45, cy + q * 0.45), fill=0)
    ty = LAYOUT['type_line_y'] * h
    for x0, x1, inner in ((0.12 * w, 0.355 * w, 0.355 * w), (0.645 * w, 0.88 * w, 0.645 * w)):
        swelled(d, x0, x1, ty, 1.0 * ss, ss)
        r = 0.016 * w
        d.polygon([(inner, ty - r), (inner + r, ty), (inner, ty + r), (inner - r, ty)], fill=255)
    layer = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    press(layer, m, spec['print'], rng, ss, dark=PRINT_DARK)
    # the hatching is engraved, not set: laid thinner, and only inside the inner rule
    hm = Image.new('L', (w, h), 0)
    hatch(ImageDraw.Draw(hm), (ax0 + 2 * g, ay0 + 2 * g, ax1 - 2 * g, ay1 - 2 * g), ss)
    clip = Image.new('L', (w, h), 0); ImageDraw.Draw(clip).rectangle((ax0 + 2 * g, ay0 + 2 * g, ax1 - 2 * g, ay1 - 2 * g), fill=255)
    ha = np.minimum(arr(hm), arr(clip)) / 255 * np.clip(blurnoise(rng, w, h, max(2, w // 25), 1.0) + 0.5, 0.4, 1.0)
    t = Image.new('RGBA', (w, h), spec['print'] + (0,)); t.putalpha(Image.fromarray((ha * 255).astype('uint8')))
    layer.alpha_composite(t)
    return layer


def title_rule(look, sealed, seed, ss=SS):
    """The title's swelled rule, as a strip the full width of the card."""
    rng = np.random.default_rng(seed)
    w, sh = CARD[0] * ss, int(LAYOUT['title_strip_height'] * CARD[1] * ss)
    end = TITLE_END_SEALED if sealed else LOOKS[look]['title_end']
    m = Image.new('L', (w, sh), 0)
    swelled(ImageDraw.Draw(m), 0.25 * w, end * w, sh / 2, 1.3 * ss, ss)
    layer = Image.new('RGBA', (w, sh), (0, 0, 0, 0))
    press(layer, m, LOOKS[look]['print'], rng, ss, dark=PRINT_DARK)
    return layer


def stamp(ink, seed, ss=SS):
    """The cost stamp, square, centred; its radius is LAYOUT['stamp']['r'] of the card's width."""
    rng = np.random.default_rng(seed)
    r = LAYOUT['stamp']['r'] * CARD[0] * ss
    n = int(2 * LAYOUT['stamp']['image_r'] * r)
    cx = cy = n / 2
    rot = rng.uniform(0, 2 * math.pi)
    def face(ox=0.0, oy=0.0, extra=0.0):
        m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
        x0, y0 = cx + ox, cy + oy
        lw = max(1, int(0.045 * r))
        for rr in (r, r * 0.80):                                # the band the cord lies in
            d.ellipse((x0 - rr, y0 - rr, x0 + rr, y0 + rr), outline=255, width=lw)
        N = 34
        for k in range(N):                                      # the cord: twisted strands across the band
            a = rot + extra + k / N * 2 * math.pi
            a2 = a + 0.75 * 2 * math.pi / N
            pts = []
            for j in range(9):
                u = j / 8
                ang, rad = a + (a2 - a) * u, r * (0.83 + 0.14 * u)
                pts.append((x0 + math.cos(ang) * rad, y0 + math.sin(ang) * rad))
            d.line(pts, fill=255, width=max(1, int(0.075 * r)), joint='curve')
        ri = r * 0.70
        d.ellipse((x0 - ri, y0 - ri, x0 + ri, y0 + ri), outline=255, width=max(1, int(0.03 * r)))
        return m
    layer = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    color, dark = INKS[ink]
    ghost = face(r * rng.uniform(0.04, 0.07), -r * rng.uniform(0.02, 0.05), rng.uniform(-0.04, 0.04))
    g = Image.new('RGBA', (n, n), color + (0,))
    g.putalpha(Image.fromarray((arr(ghost.filter(ImageFilter.GaussianBlur(0.4 * ss))) * 0.16).astype('uint8')))
    layer.alpha_composite(g)                                     # it bounced: a faint second strike
    yy, xx = np.mgrid[0:n, 0:n]
    tilt = rng.uniform(0, 2 * math.pi)
    lean = np.clip(0.85 + 0.3 * ((xx - cx) * math.cos(tilt) + (yy - cy) * math.sin(tilt)) / r, 0.55, 1.0)
    press(layer, Image.fromarray((arr(face()) * lean).astype('uint8')), color, rng, ss,
          density=(0.5, 1.0), dropout=0.08, bite=0.0, dark=dark)
    return layer


def served(seed, ss=SS, word='SERVED'):
    """SERVED in a double box, struck at an angle; square, centred, LAYOUT['served']['size'] of the card's width."""
    rng = np.random.default_rng(seed)
    n = int(LAYOUT['served']['size'] * CARD[0] * ss)
    size = 0.13 * CARD[0] * ss
    m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
    f = ImageFont.truetype(SERIF_BOLD, int(size))
    c = n / 2
    tw = f.getlength(word)
    bx0, by0, bx1, by1 = c - tw / 2 - size * 0.45, c - size * 0.75, c + tw / 2 + size * 0.45, c + size * 0.75
    d.rectangle((bx0, by0, bx1, by1), outline=255, width=int(size * 0.09))
    d.rectangle((bx0 + size * 0.18, by0 + size * 0.18, bx1 - size * 0.18, by1 - size * 0.18), outline=255, width=int(size * 0.04))
    d.text((c, c), word, font=f, fill=255, anchor='mm')
    m = m.rotate(14 + rng.uniform(-3, 3), center=(c, c), resample=Image.BICUBIC)
    layer = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    press(layer, m, SERVED_INK, rng, ss, density=(0.35, 0.85), dropout=0.12, bite=0.0)
    return layer


def preview(out, ss=SS):
    """Every look put together on its paper, as the game will lay it, without the clerk's writing."""
    paper = Image.open(HERE.parent / 'paper' / 'paper_07.png').convert('RGBA')
    seal = Image.open(HERE.parent / 'seal' / 'seal_01.png').convert('RGBA')
    w, h = CARD[0] * ss, CARD[1] * ss
    def card(look, ink, sealed):
        p = paper.resize((w, h), Image.LANCZOS)
        if look == 'notice':
            a = arr(p); a[..., :3] *= np.array(NOTICE_TINT); p = Image.fromarray(np.clip(a, 0, 255).astype('uint8'), 'RGBA')
        p.alpha_composite(Image.open(HERE / f'form_{look}_1.png'))
        t = Image.open(HERE / f"title_{look}_{'short' if sealed else 'long'}.png")
        p.alpha_composite(t, (0, int(LAYOUT['title_rule_y'] * h - t.height / 2)))
        s = Image.open(HERE / f'stamp_{ink}_1.png')
        p.alpha_composite(s, (int(LAYOUT['stamp']['cx'] * w - s.width / 2), int(LAYOUT['stamp']['cy'] * h - s.height / 2)))
        if look == 'notice':
            v = Image.open(HERE / 'served_1.png')
            p.alpha_composite(v, (int(LAYOUT['served']['cx'] * w - v.width / 2), int(LAYOUT['served']['cy'] * h - v.height / 2)))
        if sealed:
            k = LAYOUT['seal']['r'] * w / 128            # make_seal draws R = 128
            v = seal.resize((int(seal.width * k), int(seal.height * k)), Image.LANCZOS)
            p.alpha_composite(v, (int(LAYOUT['seal']['cx'] * w - v.width / 2), int(LAYOUT['seal']['cy'] * h - v.height / 2)))
        return p.resize((w // 2, h // 2), Image.LANCZOS)
    cards = [card('office', 'brown', False), card('office', 'green', False), card('office', 'red', True),
             card('notice', 'red', False)]
    sheet = Image.new('RGBA', (len(cards) * (w // 2 + 30) + 30, h // 2 + 60), (24, 18, 18, 255))
    for i, c in enumerate(cards):
        sheet.alpha_composite(c, (30 + i * (w // 2 + 30), 30))
    sheet.convert('RGB').save(out)


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--preview', help='also write the layers put together here')
    args = ap.parse_args()
    s = SEED * 1000
    for look in LOOKS:
        for i in range(1, FORMS + 1):
            form(look, s + i).save(HERE / f'form_{look}_{i}.png', optimize=True)
        s += 100
        for sealed in (False, True):
            title_rule(look, sealed, s + sealed).save(HERE / f"title_{look}_{'short' if sealed else 'long'}.png", optimize=True)
        s += 100
    for ink in INKS:
        for i in range(1, FORMS + 1):
            stamp(ink, s + i).save(HERE / f'stamp_{ink}_{i}.png', optimize=True)
        s += 100
    for i in range(1, FORMS + 1):
        served(s + i).save(HERE / f'served_{i}.png', optimize=True)
    (HERE / 'layout.json').write_text(json.dumps(LAYOUT, indent=2) + '\n')
    print('written to', HERE)
    if args.preview:
        preview(args.preview)
        print(args.preview)
