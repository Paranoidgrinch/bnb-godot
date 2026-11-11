#!/usr/bin/env python3
"""The map's ground, one kind per act, five variants each, made from real scans in src/ (public domain or CC0,
see sources.json and LICENSE.txt) plus damage layers. The acts go back in time and the material with them:
good 18th-century paper in the City Offices, dry and brittle in the Archives, wet and mouldy in the Green Docket,
worn papyrus in the Labyrinth. Act V has no sheet: its three bosses each lie on a clay fragment, drawn from a pool
of museum photos cut free here (shard_NN.png) and laid by the game under the rooms.

    make_grounds.py                 writes ground_<act>_<n>.jpg (acts 1-4, n 1-5) and shard_NN.png next to itself
    make_grounds.py --board FILE    also an overview of every variant, Act V laid out as in a run

Deterministic: variant n of act a is drawn from random.Random(f"{roman}:{n-1}"). Needs numpy, Pillow, scipy."""
import math, random, sys, os
import numpy as np
from PIL import Image, ImageFilter, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
N = 5
GW, GH = 1280, 2200          # 2x the map's 640 x 1100
TABLE = np.array([10, 6, 6], np.float32) / 255  # the game's dark ground the sheet lies on


def load(name):
    return np.asarray(Image.open(f'{HERE}/src/{name}.jpg').convert('RGB'), np.float32) / 255


def to_img(a):
    return Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))


def resize(a, w, h):
    return np.asarray(to_img(a).resize((w, h), Image.LANCZOS), np.float32) / 255


def soft_noise(rng, w, h, cell, amp=1.0, cell_y=None):
    """Clouds: a coarse grid of random numbers blown up smoothly, in -amp..amp (cells may be stretched)."""
    gw, gh = max(2, int(w / cell) + 2), max(2, int(h / (cell_y or cell)) + 2)
    g = np.random.default_rng(rng.getrandbits(32)).random((gh, gw)).astype(np.float32)
    im = Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    return (np.asarray(im, np.float32) / 255 - 0.5) * 2 * amp


def blur(a, r):
    if a.ndim == 2:
        return np.asarray(Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(r)), np.float32) / 255
    return np.asarray(to_img(a).filter(ImageFilter.GaussianBlur(r)), np.float32) / 255


def crop_to(rng, a, w, h, zoom=(1.0, 1.0)):
    """A random portrait crop of the scan, flipped at random, brought to w x h."""
    H, W = a.shape[:2]
    asp = w / h
    ch = H * rng.uniform(*zoom)
    cw = ch * asp
    if cw > W:
        cw = W
        ch = cw / asp
    x = rng.uniform(0, W - cw)
    y = rng.uniform(0, H - ch)
    c = a[int(y):int(y + ch), int(x):int(x + cw)]
    if rng.random() < 0.5:
        c = c[:, ::-1]
    if rng.random() < 0.5:
        c = c[::-1]
    return resize(c, w, h)


def tone(a, rgb, keep=0.6):
    """Pull the scan's mean colour to `rgb`, keeping its own variation."""
    mean = a.reshape(-1, 3).mean(0)
    return np.clip((a - mean) * keep + np.array(rgb, np.float32) / 255 + (a - mean) * (1 - keep) * 0.5, 0, 1)


def edge_mask(rng, w, h, inset, rough, cell=40):
    """The sheet's cut: a soft rectangle `inset` in from the border, its edge pushed by noise."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy))
    d = d + soft_noise(rng, w, h, cell, rough) + soft_noise(rng, w, h, cell / 6, rough * 0.35)
    return np.clip((d - inset) / 2.0, 0, 1), d - inset


def brown_edges(a, dist, width, strength, burn=None):
    """Browning toward the edge: blue goes first, then green, so the edge goes brown and not grey."""
    k = np.clip(1 - dist / width, 0, 1) ** 1.6 * strength
    b = a.copy()
    b[..., 0] *= 1 - 0.35 * k
    b[..., 1] *= 1 - 0.55 * k
    b[..., 2] *= 1 - 0.8 * k
    if burn is not None:  # charred: toward near-black at the very rim
        kk = np.clip(1 - dist / burn, 0, 1) ** 1.3
        b = b * (1 - kk[..., None] * 0.9) + np.array([0.08, 0.04, 0.02]) * kk[..., None] * 0.9
    return b


def foxing(rng, a, n, rmax):
    h, w = a.shape[:2]
    m = np.zeros((h, w), np.float32)
    for _ in range(n):
        x, y = rng.uniform(0, w), rng.uniform(0, h)
        r = rng.uniform(1.5, rmax)
        x0, x1, y0, y1 = int(max(0, x - 3 * r)), int(min(w, x + 3 * r)), int(max(0, y - 3 * r)), int(min(h, y + 3 * r))
        yy, xx = np.mgrid[y0:y1, x0:x1]
        m[y0:y1, x0:x1] += np.exp(-((xx - x) ** 2 + (yy - y) ** 2) / (2 * r * r)) * rng.uniform(0.3, 1)
    m = np.clip(m, 0, 1)[..., None]
    return a * (1 - m * np.array([0.25, 0.45, 0.6]))


def water_stain(rng, a, n, cell, strength, tide=1.0):
    """Damp marks: a blob a little darker inside, with a darker tide line where the water stopped."""
    h, w = a.shape[:2]
    for _ in range(n):
        f = soft_noise(rng, w, h, cell, 1) + soft_noise(rng, w, h, cell / 4, 0.25)
        cx, cy = rng.uniform(0, w), rng.uniform(0, h)
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        r = rng.uniform(0.25, 0.6) * min(w, h)
        f = f + 1.2 * (1 - np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / r)
        inside = blur(np.clip((f - 0.6) * 6, 0, 1), 6)
        ring = np.clip(inside - blur(inside, 4), 0, 1) * 4
        ring = blur(np.clip(ring, 0, 1), 1.2)
        k = (inside * 0.35 + ring * tide)[..., None] * strength
        a = a * (1 - k * np.array([0.25, 0.4, 0.55]))
    return a


def mould(rng, a, n):
    """Mildew: clusters of small grey-green specks."""
    h, w = a.shape[:2]
    m = np.zeros((h, w), np.float32)
    for _ in range(n):
        cx, cy = rng.uniform(0, w), rng.uniform(0, h)
        spread = rng.uniform(40, 140)
        for _ in range(rng.randint(400, 1100)):
            x, y = int(cx + rng.gauss(0, spread)), int(cy + rng.gauss(0, spread * 0.8))
            r = rng.uniform(0.8, 3.2)
            if 0 <= x < w and 0 <= y < h:
                x0, x1, y0, y1 = max(0, int(x - 3 * r)), min(w, int(x + 3 * r) + 1), max(0, int(y - 3 * r)), min(h, int(y + 3 * r) + 1)
                yy, xx = np.mgrid[y0:y1, x0:x1]
                m[y0:y1, x0:x1] += np.exp(-((xx - x) ** 2 + (yy - y) ** 2) / (2 * r * r)) * rng.uniform(0.2, 0.8)
    m = np.clip(m, 0, 1)
    bloom = np.clip(blur(m, 40) * 4, 0, 1)[..., None] * 0.6
    m = m[..., None]
    green = np.array([0.30, 0.36, 0.24])
    a = a * (1 - bloom) + (a * np.array([0.78, 0.9, 0.7])) * bloom
    return a * (1 - m * 0.8) + green * m * 0.8


def creases(rng, a, n, light=0.10):
    """Folds and crumples: a ridge lit from the top left, its shadow just beside it."""
    h, w = a.shape[:2]
    img = Image.new('L', (w, h), 128)
    d = ImageDraw.Draw(img)
    for _ in range(n):
        x0, y0 = rng.uniform(-0.1, 1.1) * w, rng.uniform(-0.1, 1.1) * h
        ang = rng.uniform(0, math.pi)
        L = rng.uniform(0.3, 1.4) * max(w, h)
        pts = []
        for i in range(12):
            t = i / 11
            pts.append((x0 + math.cos(ang) * L * t + rng.gauss(0, 6), y0 + math.sin(ang) * L * t + rng.gauss(0, 6)))
        d.line(pts, fill=128 + rng.choice([-1, 1]) * rng.randint(40, 90), width=rng.randint(3, 7))
    hm = np.asarray(img.filter(ImageFilter.GaussianBlur(2.5)), np.float32) / 255 - 0.5
    gx = np.roll(hm, -1, 1) - np.roll(hm, 1, 1)
    gy = np.roll(hm, -1, 0) - np.roll(hm, 1, 0)
    shade = (-gx - gy) * 18 * light
    return np.clip(a + shade[..., None], 0, 1)


def holes(rng, alpha, a, n, rmax, rim, sx=1.0, sy=1.0, where=None):
    """Holes eaten or torn out of the sheet: ragged blobs, their rims browned."""
    h, w = alpha.shape
    m = np.zeros((h, w), np.float32)
    for _ in range(n):
        cx, cy = rng.uniform(0.05, 0.95) * w, rng.uniform(0.05, 0.95) * h
        if where is not None:  # prefer worn ground
            for _ in range(30):
                if where[int(cy), int(cx)] > 0.5: break
                cx, cy = rng.uniform(0.05, 0.95) * w, rng.uniform(0.05, 0.95) * h
        r = rng.uniform(rmax * 0.35, rmax)
        x0, x1, y0, y1 = max(0, int(cx - 2.5 * r * sx)), min(w, int(cx + 2.5 * r * sx)), max(0, int(cy - 2.5 * r * sy)), min(h, int(cy + 2.5 * r * sy))
        if x1 - x0 < 4 or y1 - y0 < 4:
            continue
        yy, xx = np.mgrid[y0:y1, x0:x1].astype(np.float32)
        rr = np.sqrt(((xx - cx) / sx) ** 2 + ((yy - cy) / sy) ** 2) / r
        f = 1 - rr + soft_noise(rng, x1 - x0, y1 - y0, r * 0.6, 0.45) + soft_noise(rng, x1 - x0, y1 - y0, r * 0.15, 0.12)
        m[y0:y1, x0:x1] = np.maximum(m[y0:y1, x0:x1], np.clip(f * 6, 0, 1))
    near = np.clip(blur(m, rim) * 2.5, 0, 1)
    a = a * (1 - near[..., None] * np.array([0.35, 0.55, 0.7]))
    return alpha * (1 - m), a


def crumple(rng, a, strength, n=40, folds=2):
    """Crumpled and smoothed out again: many short straight creases, each a shallow tent in the height map
    that fades out along its length, so the sheet breaks into facets; and a fold or two right across.
    Lit from the top left."""
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h:2, 0:w:2].astype(np.float32)  # half resolution, the facets are large
    hm = np.zeros(xx.shape, np.float32)
    for _ in range(n):
        px, py = rng.uniform(0, w), rng.uniform(0, h)
        ang = rng.uniform(0, math.pi)
        ux, uy = math.cos(ang), math.sin(ang)
        along = (xx - px) * ux + (yy - py) * uy
        across = -(xx - px) * uy + (yy - py) * ux
        L = rng.uniform(0.15, 0.6) * max(w, h)
        reach = rng.uniform(25, 110)
        fade = np.exp(-(along / L) ** 2) * np.exp(-(across / (reach * 2)) ** 2)
        hm += rng.choice([-1, 1]) * rng.uniform(0.2, 0.7) * np.minimum(np.abs(across), reach) / 60 * fade
    for _ in range(folds):
        if rng.random() < 0.5:
            y = rng.uniform(0.3, 0.7) * h + 0 * xx
            hm += np.minimum(np.abs(yy - y), 60) / 60 * rng.choice([-1, 1]) * 0.8
        else:
            x = rng.uniform(0.3, 0.7) * w
            hm += np.minimum(np.abs(xx - x), 60) / 60 * rng.choice([-1, 1]) * 0.8
    hm = np.asarray(Image.fromarray(((hm - hm.min()) / (np.ptp(hm) + 1e-6) * 65535).astype(np.uint16)).resize((w, h), Image.BILINEAR), np.float32) / 65535 * (np.ptp(hm) + 1e-6)
    gx = np.roll(hm, -1, 1) - np.roll(hm, 1, 1)
    gy = np.roll(hm, -1, 0) - np.roll(hm, 1, 0)
    shade = (-gx - gy) * strength
    return np.clip(a + shade[..., None], 0, 1)


def specks(rng, h, w, n, rmin, rmax):
    """n small soft dots at random, as a 0..1 mask (fast: stamped by size class)."""
    m = np.zeros((h, w), np.float32)
    g = np.random.default_rng(rng.getrandbits(32))
    for r in [float(v) for v in np.linspace(rmin, rmax, 4)]:
        k = n // 4
        ys, xs = g.integers(0, h, k), g.integers(0, w, k)
        layer = np.zeros((h, w), np.float32)
        np.add.at(layer, (ys, xs), g.uniform(0.4, 1.0, k).astype(np.float32))
        m += blur(np.clip(layer, 0, 1), r) * (2 * math.pi * r * r) ** 0.5 * 1.2
    return np.clip(m, 0, 1)


def porous(rng, a, density, strength, fade=0.0):
    """Long dry storage: the surface goes open and grainy (fibres stand up, the size has gone), tiny pits
    and lighter flecks where fibre has rubbed away, and a dry, dusty fading in clouds."""
    h, w = a.shape[:2]
    g = np.random.default_rng(rng.getrandbits(32))
    grain = blur(g.random((h, w)).astype(np.float32), 0.7) - 0.5
    grain2 = blur(g.random((h, w)).astype(np.float32), 1.6) - 0.5
    a = a * (1 + (grain * 0.5 + grain2 * 0.9)[..., None] * strength)
    n = int(w * h * density / 1000)
    pits = specks(rng, h, w, n, 0.6, 1.8)
    flecks = specks(rng, h, w, n // 2, 0.6, 1.4)
    a = a * (1 - pits[..., None] * np.array([0.35, 0.45, 0.55]) * strength * 2.2)
    a = a + (flecks[..., None] * (np.array([0.93, 0.88, 0.78]) - a)) * strength * 1.6
    if fade:
        f = np.clip(soft_noise(rng, w, h, 300, 1) * 0.6 + 0.5, 0, 1)[..., None] * fade
        grey = a.mean(-1, keepdims=True)
        a = a * (1 - f) + (grey * 0.55 + a * 0.45 + 0.07) * f
    return np.clip(a, 0, 1)


def bookworm(rng, alpha, a, n):
    """Bookworm: short wandering trails of small round holes, eaten through the stack."""
    h, w = alpha.shape
    img = Image.new('L', (w, h), 0)
    d = ImageDraw.Draw(img)
    for _ in range(n):
        x, y = rng.uniform(0.05, 0.95) * w, rng.uniform(0.05, 0.95) * h
        ang = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(2.5, 4.5)
        for _ in range(rng.randint(3, 14)):
            if rng.random() < 0.8:
                rr = r * rng.uniform(0.8, 1.15)
                d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=255)
            ang += rng.gauss(0, 0.5)
            step = r * rng.uniform(1.6, 4)
            x, y = x + math.cos(ang) * step, y + math.sin(ang) * step
    m = np.asarray(img.filter(ImageFilter.GaussianBlur(0.6)), np.float32) / 255
    near = np.clip(blur(m, 3) * 2.5, 0, 1)
    a = a * (1 - near[..., None] * np.array([0.3, 0.45, 0.6]))
    return alpha * (1 - m), a


def chips(rng, alpha, a, dist, n, rmax):
    """A brittle edge: small pieces broken out of the rim, each a ragged bite."""
    h, w = alpha.shape
    m = np.zeros((h, w), np.float32)
    for _ in range(n):
        side = rng.randrange(4)
        t = rng.uniform(0.02, 0.98)
        cx, cy = [(t * w, 0), (t * w, h), (0, t * h), (w, t * h)][side]
        # walk in to the sheet's edge
        for _ in range(200):
            ix, iy = int(min(w - 1, max(0, cx))), int(min(h - 1, max(0, cy)))
            if dist[iy, ix] > 0: break
            cx += [0, 0, 1, -1][side] * 1.5; cy += [1, -1, 0, 0][side] * 1.5
        r = rng.uniform(rmax * 0.3, rmax)
        x0, x1, y0, y1 = max(0, int(cx - 2.5 * r)), min(w, int(cx + 2.5 * r)), max(0, int(cy - 2.5 * r)), min(h, int(cy + 2.5 * r))
        if x1 - x0 < 4 or y1 - y0 < 4: continue
        yy, xx = np.mgrid[y0:y1, x0:x1].astype(np.float32)
        rr = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / r
        f = 1 - rr + soft_noise(rng, x1 - x0, y1 - y0, max(3, r * 0.4), 0.5) + soft_noise(rng, x1 - x0, y1 - y0, max(2, r * 0.12), 0.15)
        m[y0:y1, x0:x1] = np.maximum(m[y0:y1, x0:x1], np.clip(f * 8, 0, 1))
    near = np.clip(blur(m, 5) * 2, 0, 1)
    a = a * (1 - near[..., None] * np.array([0.25, 0.4, 0.55]))
    return alpha * (1 - m), a


def abrasion(rng, a, amount):
    """Papyrus rubbed thin: patches where the top layer of strips is worn off, lighter and greyer, the
    fibre running through it in streaks; small gaps open along the fibre."""
    h, w = a.shape[:2]
    patch = np.clip(soft_noise(rng, w, h, 200, 1) + soft_noise(rng, w, h, 50, 0.35) - (0.55 - amount * 0.4), 0, 1)
    patch = np.clip(patch * 3, 0, 1)
    g = np.random.default_rng(rng.getrandbits(32))
    streak = np.asarray(Image.fromarray((g.random((h // 40 + 2, w)) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255
    streak = blur(streak, 0.8) - 0.5
    grey = a.mean(-1, keepdims=True)
    worn = (a * 0.78 + grey * 0.22) * (1.07 + streak[..., None] * 0.3) + np.array([0.03, 0.02, 0.0])
    k = patch[..., None] * amount
    return np.clip(a * (1 - k) + worn * k, 0, 1), patch


def laid_lines(rng, a, spacing, strength):
    """Laid paper: the chain lines of the mould, faintly lighter, a little wavy."""
    h, w = a.shape[:2]
    xx = np.arange(w, dtype=np.float32)[None, :] + np.zeros((h, 1), np.float32)
    wav = soft_noise(rng, w, h, 300, 2.0)
    ph = rng.uniform(0, spacing)
    k = np.exp(-(((xx + wav + ph) % spacing) - spacing / 2) ** 2 / 4.0)
    fine = (np.sin((np.arange(h, dtype=np.float32)[:, None]) * 2 * math.pi / 2.4) * 0.5 + 0.5) * 0.25
    return np.clip(a + (k * strength + fine * strength * 0.25)[..., None], 0, 1)


def vignette(a, strength, inner=0.35, tint=(0.06, 0.03, 0.02)):
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    dx, dy = (xx - w / 2) / (w / 2), (yy - h / 2) / (h / 2)
    r = np.sqrt(dx * dx * 1.0 + dy * dy * 0.75)
    k = np.clip((r - inner) / (1.15 - inner), 0, 1) ** 1.8 * strength
    return a * (1 - k[..., None]) + np.array(tint) * k[..., None]


def lay(a, alpha, shadow=True):
    """The sheet on the dark table, with a soft shadow below-right."""
    h, w = alpha.shape
    out = np.ones((h, w, 3), np.float32) * TABLE
    if shadow:
        s = blur(np.roll(np.roll(alpha, 10, 0), 6, 1), 9)
        out = out * (1 - s[..., None] * 0.7)
    return out * (1 - alpha[..., None]) + a * alpha[..., None]


# ── one ground per act ───────────────────────────────────────────────────────

def city(rng):
    """Act I: decent rag paper, lightly yellowed: chain lines, a few fox marks, a fold, a clean deckle edge."""
    a = crop_to(rng, load(rng.choice(['paperT', 'paper6', 'paper7'])), GW, GH, (0.75, 0.95))
    a = tone(a, rng.choice([(232, 216, 182), (226, 208, 170), (236, 222, 190)]), keep=0.7)
    a = laid_lines(rng, a, rng.uniform(46, 56), 0.018)
    a = foxing(rng, a, rng.randint(8, 25), 5)
    a = creases(rng, a, rng.randint(1, 2), 0.06)
    alpha, dist = edge_mask(rng, GW, GH, 26, 4, 30)
    a = brown_edges(a, dist, 120, 0.5)
    return lay(a, alpha)


def archive(rng):
    """Act II: older, worse: browned and foxed, damp marks, soot toward the rim, read by one candle."""
    a = crop_to(rng, load(rng.choice(['paper1', 'paper4'])), GW, GH, (0.7, 0.95))
    a = tone(a, rng.choice([(178, 142, 92), (168, 128, 80), (186, 150, 100)]), keep=0.8)
    a = foxing(rng, a, rng.randint(60, 140), 9)
    a = water_stain(rng, a, rng.randint(1, 2), 260, 0.8, tide=0.7)
    a = crumple(rng, a, 0.3, n=rng.randint(30, 50), folds=rng.randint(1, 2))
    alpha, dist = edge_mask(rng, GW, GH, 34, 16, 60)
    a = brown_edges(a, dist, 220, 0.9, burn=40)
    a = porous(rng, a, rng.uniform(5, 8), 0.16, fade=0.45)
    a = vignette(a, rng.uniform(0.75, 0.9), inner=rng.uniform(0.15, 0.3))
    alpha, a = chips(rng, alpha, a, dist, rng.randint(14, 26), 26)
    alpha, a = bookworm(rng, alpha, a, rng.randint(2, 5))
    alpha, a = holes(rng, alpha, a, rng.randint(0, 2), 26, 6)
    return lay(a, alpha)


def docket(rng):
    """Act III: the worst paper: soaked and dried more than once, mildewed, crumpled, torn and holed."""
    a = crop_to(rng, load(rng.choice(['paper2', 'paper4', 'paper1'])), GW, GH, (0.6, 0.9))
    a = tone(a, rng.choice([(196, 170, 120), (186, 162, 112), (200, 178, 128)]), keep=0.9)
    a = water_stain(rng, a, rng.randint(2, 4), 380, 1.3, tide=1.3)
    a = mould(rng, a, rng.randint(18, 26))
    a = foxing(rng, a, rng.randint(80, 160), 8)
    a = crumple(rng, a, rng.uniform(0.45, 0.6), n=rng.randint(160, 220), folds=rng.randint(1, 3))
    alpha, dist = edge_mask(rng, GW, GH, 40, 30, 70)
    a = brown_edges(a, dist, 200, 0.85)
    alpha, a = holes(rng, alpha, a, rng.randint(3, 7), 44, 8)
    return lay(a, alpha)


_PAP = {}


def papyrus_window(rng, w, h):
    """A w x h piece of the Met sheets, turned so the roll runs down the map, taken only where it is all
    papyrus (the white mount around the fragments is refused)."""
    name = rng.choice(['papyrus1', 'papyrus2'])
    if name not in _PAP:
        src = np.rot90(load(name), 1)
        bright = src.mean(-1)
        sat = src.max(-1) - src.min(-1)
        ok = ((bright < 0.72) & (sat > 0.12)).astype(np.float32)
        _PAP[name] = (src, ok.cumsum(0).cumsum(1))
    src, integral = _PAP[name]
    H, W = src.shape[:2]
    for scale in (0.95, 0.85, 0.75, 0.65, 0.55):
        cw, ch = int(w * scale), int(h * scale)
        if cw >= W or ch >= H:
            continue
        for _ in range(400):
            x, y = rng.randint(0, W - cw - 1), rng.randint(0, H - ch - 1)
            total = integral[y + ch, x + cw] - integral[y, x + cw] - integral[y + ch, x] + integral[y, x]
            if total >= cw * ch * 0.998:
                piece = src[y:y + ch, x:x + cw]
                if rng.random() < 0.5:
                    piece = piece[::-1]
                return resize(piece, w, h)
    raise RuntimeError('no clean papyrus window')


def papyrus(rng):
    """Act IV: papyrus, two lengths of a roll pasted side by side, frayed at the edges and gapped."""
    half = GW // 2
    a = np.concatenate([papyrus_window(rng, half, GH), papyrus_window(rng, GW - half, GH)], axis=1)
    # the join where one length was pasted over the next: a slightly darker, thicker strip
    j = np.exp(-((np.arange(GW) - half) / 7.0) ** 2)[None, :, None]
    a = a * (1 - 0.18 * j)
    a = tone(a, rng.choice([(176, 136, 88), (190, 150, 98), (166, 124, 78)]), keep=0.95)
    a, worn = abrasion(rng, a, rng.uniform(0.7, 0.85))
    a = porous(rng, a, rng.uniform(4, 6), 0.14)
    alpha, dist = edge_mask(rng, GW, GH, 40, 34, 40)
    a = brown_edges(a, dist, 110, 0.7)
    alpha, a = chips(rng, alpha, a, dist, rng.randint(10, 18), 34)
    alpha, a = holes(rng, alpha, a, rng.randint(6, 12), 22, 3, sx=0.45, sy=1.6, where=worn)
    alpha, a = holes(rng, alpha, a, rng.randint(1, 3), 40, 4)
    return lay(a, alpha)


SHARD_POOL = ['01', '04', '05', '06', '07', '08', '09', '10', '12', '13', '14', '16', '18', '19', '20', '26', '28']
ROPE = (196, 168, 120)


def shard_spots(rng):
    """Where Act V's three rooms sit in this prototype: top, middle, bottom, a little to either side.
    In the game these are the map's own node positions."""
    return [(GW * (0.5 + rng.uniform(-0.18, 0.18)), GH * f) for f in (0.18, 0.5, 0.82)]


def shards(rng, spots=None):
    """Act V: three clay fragments (Met Museum photos, CC0) drawn from a pool and laid on the dark table,
    one under each room, joined by straight lines from room to room."""
    spots = spots or shard_spots(rng)
    canvas = Image.new('RGBA', (GW, GH), tuple(int(c * 255) for c in TABLE) + (255,))
    # the lines first, so the fragments lie on them
    d = ImageDraw.Draw(canvas)
    for (x0, y0), (x1, y1) in zip(spots, spots[1:]):
        d.line([(x0, y0), (x1, y1)], fill=ROPE + (255,), width=5)
    tint = np.array(rng.choice([(186, 132, 92), (196, 146, 104), (178, 124, 86)]), np.float32) / 255
    for (x, y), k in zip(spots, rng.sample(SHARD_POOL, 3)):
        im = Image.open(f'{HERE}/shard_{k}.png')
        if rng.random() < 0.5:
            im = im.transpose(Image.FLIP_LEFT_RIGHT)
        im = im.rotate(rng.uniform(-30, 30), resample=Image.BICUBIC, expand=True)
        size = rng.uniform(440, 540)
        f = size / max(im.size)
        im = im.resize((int(im.width * f), int(im.height * f)), Image.LANCZOS)
        a = np.asarray(im, np.float32) / 255
        rgb, al = a[..., :3], a[..., 3]
        # pull every fragment toward one clay so the three look like one find
        mean = (rgb * al[..., None]).sum((0, 1)) / max(al.sum(), 1)
        rgb = np.clip((rgb - mean) * 1.0 + mean * 0.45 + tint * 0.55, 0, 1)
        im = Image.fromarray((np.dstack([rgb, al]) * 255).astype(np.uint8), 'RGBA')
        px, py = int(x - im.width / 2), int(y - im.height / 2)
        sh = Image.new('RGBA', im.size, (0, 0, 0, 0))
        sh.putalpha(im.getchannel('A').point(lambda v: int(v * 0.75)).filter(ImageFilter.GaussianBlur(10)))
        canvas.alpha_composite(sh, (px + 12, py + 16))
        canvas.alpha_composite(im, (px, py))
    return np.asarray(canvas.convert('RGB'), np.float32) / 255


def cut_shard(k):
    """One clay fragment cut out of its museum photo (src/shards/rawNN.jpg). The clay is warm (red well above
    blue); the backgrounds are teal, grey or white and the shadows on them bluish or grey, so "red minus blue"
    separates them. Largest piece, holes filled, edge eased; at most SHARD_MAX on the long side."""
    from scipy import ndimage as ndi
    a = np.asarray(Image.open(f'{HERE}/src/shards/raw{k}.jpg').convert('RGB'), np.float32) / 255
    warm = a[..., 0] - a[..., 2]
    b = np.concatenate([warm[:8].ravel(), warm[-8:].ravel(), warm[:, :8].ravel(), warm[:, -8:].ravel()])
    m = ndi.gaussian_filter(warm, 1.5) > max(0.08, np.percentile(b, 95) + 0.05)
    m = ndi.binary_opening(m, iterations=2)
    lab, n = ndi.label(m)
    m = lab == (1 + int(np.argmax(ndi.sum(m, lab, range(1, n + 1)))))
    m = ndi.binary_erosion(ndi.binary_fill_holes(ndi.binary_closing(m, iterations=6)), iterations=2)
    alpha = ndi.gaussian_filter(m.astype(np.float32), 1.0)
    ys, xs = np.where(m)
    rgba = np.dstack([a, alpha])[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    im = Image.fromarray((np.clip(rgba, 0, 1) * 255).astype(np.uint8), 'RGBA')
    f = min(1.0, SHARD_MAX / max(im.size))
    return im.resize((round(im.width * f), round(im.height * f)), Image.LANCZOS)


SHARD_MAX = 600
ACTS = [(1, 'I', city), (2, 'II', archive), (3, 'III', docket), (4, 'IV', papyrus)]


def board(path):
    """Every variant side by side, Act V as a run might lay it, at a third of the size."""
    tw, th = GW // 4, GH // 4
    out = Image.new('RGB', (20 + 5 * (tw + 20), 20 + N * (th + 20)), (24, 18, 16))
    for i, (num, roman, _) in enumerate(ACTS + [(5, 'V', None)]):
        for v in range(N):
            if num == 5:
                im = to_img(shards(random.Random(f'V:{v}')))
            else:
                im = Image.open(f'{HERE}/ground_{num}_{v + 1}.jpg')
            out.paste(im.resize((tw, th), Image.LANCZOS), (20 + i * (tw + 20), 20 + v * (th + 20)))
    out.save(path)


if __name__ == '__main__':
    for k in SHARD_POOL:
        cut_shard(k).save(f'{HERE}/shard_{k}.png')
    print('wrote', len(SHARD_POOL), 'shards')
    for num, roman, make in ACTS:
        for v in range(N):
            to_img(make(random.Random(f'{roman}:{v}'))).save(f'{HERE}/ground_{num}_{v + 1}.jpg', quality=90)
            print('wrote ground', num, v + 1)
    if '--board' in sys.argv:
        board(sys.argv[sys.argv.index('--board') + 1])
