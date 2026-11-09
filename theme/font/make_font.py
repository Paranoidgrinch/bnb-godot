#!/usr/bin/env python3
# The card font, "Clerk of Boston": writes clerk-of-boston.ttf next to itself.
#     python3 theme/font/make_font.py
# Needs numpy, shapely and fontTools. Deterministic: the same script writes the same glyphs.
#
# Quill: a readable clerk's hand after the Boston Overseers of the Poor papers (1739-1801).
# Every glyph is a set of pen strokes (centre lines, upright); a broad quill nib is swept along each stroke and
# the union is the outline. The whole hand is slanted afterwards, so the nib keeps its angle.
import math, random
import numpy as np
from shapely.geometry import Polygon, MultiPolygon, Point
from shapely.ops import unary_union
from shapely.geometry.polygon import orient

UPM = 1000
X, CAP, ASC, DESC = 430, 640, 720, -250
SLANT = math.radians(14)
NIB_W, NIB_RATIO, NIB_ANGLE = 92, 0.21, math.radians(35)
HSCALE = 1.14


def arc(cx, cy, rx, ry, a0, a1, n=None):
    n = n or max(8, int(abs(a1 - a0) / 6))
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


class S:
    """A stroke: points (tuples or lists of tuples), smoothed by Catmull-Rom; `w` scales the nib; `t0`/`t1` taper."""
    def __init__(self, *parts, w=1.0, t0=1.08, t1=0.62, dx=0, dy=0):
        pts = []
        for p in parts:
            if isinstance(p, list):
                pts.extend(p)
            else:
                pts.append(p)
        self.pts = [(x + dx, y + dy) for x, y in pts]
        self.w, self.t0, self.t1 = w, t0, t1

    def moved(self, dx, dy):
        s = S(*self.pts, w=self.w, t0=self.t0, t1=self.t1)
        s.pts = [(x + dx, y + dy) for x, y in s.pts]
        return s


def catmull(pts, step=7.0):
    if len(pts) == 1:
        return [pts[0]]
    p = [pts[0]] + list(pts) + [pts[-1]]
    out = []
    for i in range(1, len(p) - 2):
        p0, p1, p2, p3 = map(np.array, (p[i - 1], p[i], p[i + 1], p[i + 2]))
        seg = max(2, int(np.linalg.norm(p2 - p1) / step))
        for k in range(seg):
            t = k / seg
            out.append(tuple(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                                    + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)))
    out.append(tuple(pts[-1]))
    return out


def nib(x, y, scale):
    a, b = NIB_W / 2 * scale, NIB_W * NIB_RATIO / 2 * scale
    ca, sa = math.cos(NIB_ANGLE), math.sin(NIB_ANGLE)
    return [(x + a * math.cos(t) * ca - b * math.sin(t) * sa, y + a * math.cos(t) * sa + b * math.sin(t) * ca)
            for t in np.linspace(0, 2 * math.pi, 14, endpoint=False)]


def shear(x, y):
    return x + y * math.tan(SLANT), y


def sweep(stroke, rng):
    if isinstance(stroke, Blot):
        return blot_shape(stroke, rng)
    if isinstance(stroke, Dot):
        x, y = shear(stroke.x * HSCALE, stroke.y)
        return unary_union([Point(x, y).buffer(stroke.r, 8),
                            Point(x + rng.uniform(-8, 8), y + rng.uniform(-8, 8)).buffer(stroke.r * 0.8, 8)])
    path = [shear(x * HSCALE, y) for x, y in catmull(stroke.pts)]
    n = len(path)
    # a slow wobble of the hand and of the pressure, so no two strokes are quite alike
    ph, amp = rng.uniform(0, 6.28), rng.uniform(0.03, 0.07)
    hulls = []
    prev = None
    for i, (x, y) in enumerate(path):
        t = i / max(1, n - 1)
        taper = 1.0
        if t < 0.12:
            taper = stroke.t0 + (1 - stroke.t0) * (t / 0.12)
        elif t > 0.85:
            taper = 1 - (1 - stroke.t1) * ((t - 0.85) / 0.15)
        s = stroke.w * taper * (1 + amp * math.sin(ph + 9 * t))
        cur = nib(x, y, s)
        if prev is not None:
            hulls.append(Polygon(prev + cur).convex_hull)
        else:
            hulls.append(Polygon(cur))
        prev = cur
    return unary_union(hulls)


def roughen(geom, rng, amp=3.4):
    def edge(coords):
        out = []
        ph = rng.uniform(0, 6.28)
        raw = [rng.uniform(-1, 1) for _ in coords]
        k = len(raw)
        smooth = [(raw[i - 2] + 2 * raw[i - 1] + 3 * raw[i] + 2 * raw[(i + 1) % k] + raw[(i + 2) % k]) / 9 for i in range(k)]
        for i, (x, y) in enumerate(coords):
            r = amp * (math.sin(ph + i * 0.7) * 0.5 + smooth[i] * 1.4)
            out.append((x + r * 0.5, y + r * 0.5))
        return out
    polys = geom.geoms if isinstance(geom, MultiPolygon) else [geom]
    res = []
    for p in polys:
        p = p.simplify(1.2)
        q = Polygon(edge(list(p.exterior.coords)[:-1]), [edge(list(h.coords)[:-1]) for h in p.interiors])
        res.append(q.buffer(0))
    return unary_union(res)


class Dot:
    def __init__(self, x, y, r):
        self.x, self.y, self.r = x, y, r

class Blot:
    def __init__(self, x, y, r, drops=1):
        self.x, self.y, self.r, self.drops = x, y, r, drops


def blot_shape(b, rng):
    x, y = shear(b.x * HSCALE, b.y)
    parts = [Point(x, y).buffer(b.r, 10)]
    for _ in range(5):
        a, d = rng.uniform(0, 6.28), rng.uniform(0.3, 0.8) * b.r
        parts.append(Point(x + d * math.cos(a), y + d * math.sin(a)).buffer(b.r * rng.uniform(0.45, 0.75), 8))
    for _ in range(b.drops):
        a, d = rng.uniform(0.2, 1.4), rng.uniform(1.5, 2.4) * b.r
        parts.append(Point(x + d * math.cos(a), y + d * math.sin(a)).buffer(b.r * rng.uniform(0.14, 0.26), 6))
    return unary_union(parts)


def dot(x, y, w=1.25):
    return Dot(x, y, 30 * w)


# ── the glyphs: (advance, strokes) in upright units ───────────────────────────────────────────────────────────
G = {}

def bowl(cx=175, rx=124):
    return S(arc(cx, 215, rx, 212, 35, 340), t0=0.9, t1=0.9)

def exitflick(x, y=60):
    return [(x, y), (x + 28, 4), (x + 70, 12), (x + 112, 70)]

def lead(x, top=430):
    # the hairline the quill comes up on before the first downstroke
    return [(x - 95, top - 210), (x - 40, top - 60), (x, top)]

def loop_asc(x=82):
    # an ascender written as a loop: a hairline up to the right, back down on the stem
    return S((x - 50, 280), (x + 50, 500), (x + 128, 670), (x + 108, 752), (x + 30, 735), (x + 2, 600), (x, 420),
             (x - 4, 0), t0=0.5)

def desc_loop(x):
    # a descender that swings left and closes back towards the line
    return [(x, 430), (x - 2, -90), (x - 40, -225), (x - 150, -275), (x - 255, -215), (x - 205, -110), (x - 60, -40),
            (x + 80, 10)]

def cap_stem(x=100, foot=True):
    # a capital's stem: comes in with a curl at the top, leaves with a curl at the foot
    pts = [(x - 60, 520), (x - 125, 580), (x - 95, 668), (x - 10, 690), (x + 5, 600), (x, 300), (x - 6, 40)]
    if foot:
        pts += [(x - 45, -12), (x - 115, 0), (x - 125, 60)]
    return S(*pts, t0=0.5)

# lowercase
G['a'] = (410, [bowl(), S((302, 430), *exitflick(298))])
G['b'] = (380, [loop_asc(), S((80, 290), (180, 420), (285, 395), (318, 230), (270, 60), (170, 0), (78, 35), (60, 90))])
G['c'] = (370, [S(arc(185, 215, 124, 212, 26, 318)), S((300, 360), (318, 405), (290, 425), t0=1.4, t1=0.6)])
G['d'] = (420, [bowl(), S((40, 615), (95, 715), (215, 752), (305, 690), (303, 420), *exitflick(298))])
G['e'] = (380, [S((70, 200), (170, 222), (275, 275), (288, 370), (222, 430), (128, 415), (64, 320), (56, 175),
                  (110, 48), (200, 0), (300, 12), (380, 80))])
G['f'] = (290, [S((322, 610), (285, 712), (205, 740), (152, 676), (140, 400), (130, 0), (115, -200), (62, -262),
                  (20, -215), t1=0.5), S((30, 405), (100, 430), (255, 428))])
G['g'] = (410, [bowl(), S(*desc_loop(302), t1=0.5)])
G['h'] = (420, [loop_asc(), S((80, 280), (150, 400), (240, 430), (290, 372), *exitflick(286))])
G['i'] = (230, [S(*lead(92), (86, 60), (122, 2), (165, 8), (205, 60)), dot(108, 610)])
G['j'] = (230, [S(*lead(122), *desc_loop(120)[1:6], t1=0.5), dot(138, 610)])
G['k'] = (390, [loop_asc(), S((82, 220), (190, 380), (268, 412), (250, 330), (140, 270), t1=1),
                S((140, 270), (240, 60), (290, 4), (330, 12), (370, 60))])
G['l'] = (250, [S((30, 280), (132, 500), (210, 670), (190, 752), (112, 735), (84, 600), (82, 420), (84, 60), (122, 2),
                  (165, 8), (205, 60), t0=0.5)])
G['m'] = (570, [S(*lead(82), (78, 0)), S((80, 318), (142, 415), (205, 428), (245, 362), (240, 0)),
                S((242, 318), (305, 415), (368, 428), (408, 362), *exitflick(404))])
G['n'] = (420, [S(*lead(82), (78, 0)), S((80, 318), (152, 415), (225, 428), (268, 362), *exitflick(264))])
G['o'] = (370, [S(arc(180, 215, 128, 213, 100, 470), t0=0.9, t1=0.9)])
G['p'] = (390, [S(*lead(98), (90, -250), t1=0.5),
                S((94, 320), (180, 425), (282, 400), (312, 230), (262, 60), (170, 0), (94, 38))])
G['q'] = (400, [bowl(), S((302, 430), (296, -250), (330, -200), (360, -120), t1=0.6)])
G['r'] = (320, [S(*lead(82), (78, 0)), S((80, 300), (150, 410), (200, 430), (210, 380), (250, 400), (300, 420))])
G['s'] = (330, [S((272, 382), (222, 427), (130, 432), (72, 370), (92, 292), (190, 232), (262, 150), (250, 52),
                  (170, 0), (80, 10), (30, 62), (40, 100))])
G['t'] = (270, [S((118, 620), (100, 350), (94, 60), (138, 2), (180, 8), (230, 70)), S((22, 400), (110, 425), (232, 418))])
G['u'] = (420, [S(*lead(72), (66, 90), (112, 5), (192, 10), (262, 122)), S((268, 430), *exitflick(264))])
G['v'] = (380, [S(*lead(75), (125, 240), (180, 0), t1=1), S((180, 0), (245, 240), (292, 400), (322, 440), (360, 410), (340, 380))])
G['w'] = (530, [S(*lead(78), (120, 220), (160, 0), t1=1), S((160, 0), (212, 200), (255, 330), t1=1),
                S((255, 330), (298, 150), (330, 0), t1=1), S((330, 0), (392, 250), (432, 405), (462, 440), (500, 410), (480, 380))])
G['x'] = (380, [S(*lead(80), (150, 248), (268, 0), (322, 8), (360, 50)), S((292, 430), (42, 0))])
G['y'] = (400, [S(*lead(72), (66, 90), (112, 5), (192, 10), (262, 122)), S(*desc_loop(272), t1=0.5)])
G['z'] = (360, [S((20, 330), (52, 395), (120, 430), (250, 422), (282, 432), t1=1), S((282, 432), (52, 2), t0=1, t1=1),
                S((52, 2), (150, 18), (262, 0), (315, 32), (330, 80))])

# capitals
G['A'] = (470, [S((-30, 60), (0, 5), (40, 10), (95, 220), (205, 648), t1=1),
                S((205, 648), (272, 300), (330, 42), (382, 2), (440, 30)), S((100, 248), (300, 262))])
G['B'] = (440, [cap_stem(), S((95, 640), (250, 660), (330, 570), (252, 352), (100, 340)),
                S((100, 340), (300, 320), (360, 180), (300, 30), (150, 0), (60, 20))])
G['C'] = (440, [S((300, 520), (340, 600), (322, 652), (250, 662), (150, 610), (62, 450), (52, 250), (100, 90),
                  (200, 0), (320, 20), (392, 90))])
G['D'] = (480, [cap_stem(foot=False), S((92, 40), (40, -5), (0, 20), (40, 50), (200, 10), (330, 80), (400, 300),
                (380, 520), (260, 650), (100, 655))])
G['E'] = (400, [S((300, 520), (340, 590), (300, 650), (170, 655), (100, 560), (150, 390), (232, 352), t1=1),
                S((232, 352), (110, 330), (60, 180), (120, 30), (250, 0), (350, 60))])
G['F'] = (410, [S((40, 560), (20, 620), (70, 655), (160, 625), (260, 645), (412, 668)),
                S((222, 645), (200, 300), (170, 60), (100, 0), (40, 30), (55, 90)), S((110, 338), (300, 350))])
G['G'] = (460, [S((300, 520), (340, 600), (322, 652), (250, 662), (150, 610), (62, 450), (52, 250), (100, 90),
                  (200, 0), (320, 20), (372, 120), (372, 285)), S((262, 285), (372, 290), (430, 300))])
G['H'] = (470, [cap_stem(), S((372, 680), (345, 640), (335, 300), *exitflick(330)), S((100, 340), (220, 360), (335, 330))])
G['I'] = (320, [S((40, 560), (30, 630), (90, 660), (180, 640), (270, 650)), S((170, 645), (160, 300), (155, 20)),
                S((60, 10), (150, 22), (262, 0))])
G['J'] = (340, [S((40, 560), (30, 630), (90, 660), (200, 640), (300, 650)), S((222, 650), (216, 100), (190, -80),
                (120, -200), (40, -220), (20, -150), (90, -80))])
G['K'] = (450, [cap_stem(), S((372, 660), (330, 640), (240, 500), (100, 320)),
                S((170, 390), (270, 120), (330, 20), (400, 30), (440, 80))])
G['L'] = (420, [S((262, 600), (240, 660), (180, 670), (140, 600), (130, 250), (102, 52)),
                S((10, 60), (40, 10), (102, 52), (200, 22), (340, 0), (412, 50))])
G['M'] = (560, [S((-20, 40), (20, 0), (50, 60), (80, 350), (120, 645), t0=0.6, t1=1), S((120, 645), (240, 60), t0=1, t1=1),
                S((240, 60), (380, 645), t0=1, t1=1), S((380, 645), (400, 250), *exitflick(398))])
G['N'] = (460, [S((-20, 40), (20, 0), (50, 60), (80, 350), (110, 645), t0=0.6, t1=1), S((110, 645), (330, 0), t0=1, t1=1),
                S((330, 0), (350, 645), (390, 680), (430, 650))])
G['O'] = (460, [S(arc(220, 320, 172, 322, 100, 468), t0=0.9, t1=0.9), S((180, 630), (280, 600), (380, 640), t0=0.6, t1=0.5)])
G['P'] = (420, [cap_stem(), S((95, 640), (262, 660), (345, 560), (312, 380), (200, 320), (95, 340))])
G['Q'] = (470, [S(arc(220, 320, 172, 322, 100, 468), t0=0.9, t1=0.9),
                S((150, 80), (220, 20), (300, -20), (400, -30), (470, 10))])
G['R'] = (450, [cap_stem(), S((95, 640), (262, 660), (345, 560), (312, 380), (200, 330), (95, 340)),
                S((200, 330), (280, 150), (330, 20), (400, 10), (440, 60))])
G['S'] = (400, [S((322, 570), (262, 650), (160, 655), (80, 560), (110, 420), (250, 320), (322, 180), (272, 40),
                  (150, 0), (40, 30), (0, 110), (40, 150))])
G['T'] = (430, [S((40, 560), (20, 620), (70, 655), (160, 625), (260, 645), (412, 668)),
                S((222, 645), (200, 300), (170, 60), (100, 0), (40, 30), (55, 90))])
G['U'] = (480, [S((-10, 560), (20, 640), (80, 665), (90, 400), (80, 180), (140, 20), (240, 10), (332, 140)),
                S((342, 645), *exitflick(338))])
G['V'] = (440, [S((-10, 560), (20, 640), (80, 665), (140, 350), (202, 0), t1=1),
                S((202, 0), (332, 450), (382, 640), (430, 670), (460, 620))])
G['W'] = (580, [S((-10, 560), (20, 640), (80, 665), (130, 300), (170, 0), t1=1), S((170, 0), (262, 420), t0=1, t1=1),
                S((262, 420), (332, 0), t0=1, t1=1), S((332, 0), (452, 450), (502, 640), (550, 670), (580, 620))])
G['X'] = (430, [S((-10, 560), (20, 640), (90, 655), (200, 330), (330, 20), (382, 10), (420, 60)), S((362, 645), (60, 0))])
G['Y'] = (420, [S((-10, 560), (20, 640), (80, 665), (132, 450), (215, 330)),
                S((380, 655), (262, 380), (200, 150), (182, 0), (140, -150), (60, -240), (0, -200))])
G['Z'] = (440, [S((40, 560), (60, 630), (150, 640), (300, 630), (372, 645), t1=1), S((372, 645), (50, 0), t0=1, t1=1),
                S((50, 0), (180, 20), (300, 0), (382, 40), (400, 100))])

# figures, all the same width so costs and damage line up
FIG = 360
def fig(*strokes):
    return (FIG, list(strokes))
G['0'] = fig(S(arc(180, 300, 120, 300, 95, 460), t0=0.9, t1=0.9))
G['1'] = fig(S((95, 470), (190, 605), (180, 0), t0=0.5), S((110, 2), (260, 10)))
G['2'] = fig(S((68, 470), (115, 580), (205, 612), (285, 560), (285, 430), (185, 260), (60, 2), t1=1),
             S((60, 2), (200, 22), (312, 2), (330, 30)))
G['3'] = fig(S((72, 520), (152, 605), (252, 592), (272, 470), (172, 342), t1=1),
             S((172, 342), (272, 292), (302, 152), (232, 22), (122, 0), (42, 62)))
G['4'] = fig(S((232, 600), (42, 182), (322, 192)), S((248, 410), (238, 0)))
G['5'] = fig(S((292, 602), (112, 596)), S((112, 600), (92, 350), (182, 382), (282, 322), (302, 172), (222, 22),
             (112, 0), (42, 62)))
G['6'] = fig(S((262, 582), (192, 612), (100, 540), (52, 300), (82, 80), (172, 0), (262, 40), (292, 172), (232, 302),
             (132, 302), (62, 222)))
G['7'] = fig(S((42, 578), (122, 602), (312, 600), t1=1), S((312, 600), (190, 300), (132, 0)))
G['8'] = fig(S((252, 562), (172, 612), (82, 562), (102, 422), (242, 282), (282, 122), (182, 0), (72, 40), (62, 172),
             (232, 342), (262, 502), (242, 572)))
G['9'] = fig(S((262, 442), (172, 332), (82, 372), (62, 502), (152, 612), (262, 582), (272, 400), (232, 150),
             (170, 0)))

# punctuation and signs
G['.'] = (170, [dot(80, 22)])
G[','] = (170, [S((82, 40), (84, 12), (52, -100), w=1.1, t0=1.1, t1=0.5)])
G[':'] = (190, [dot(90, 22), dot(102, 300)])
G[';'] = (190, [S((92, 40), (94, 12), (62, -100), w=1.1, t0=1.1, t1=0.5), dot(104, 300)])
G['!'] = (200, [S((112, 650), (90, 180), t0=1.2, t1=0.6), dot(82, 22)])
G['?'] = (330, [S((62, 540), (122, 630), (222, 642), (282, 560), (252, 450), (162, 362), (152, 200), t1=0.7),
                dot(150, 22)])
G["'"] = (150, [S((82, 720), (70, 570), t0=1.1, t1=0.5)])
G['"'] = (240, [S((82, 720), (70, 570), t0=1.1, t1=0.5), S((162, 720), (150, 570), t0=1.1, t1=0.5)])
G['’'] = (170, [S((92, 720), (94, 690), (62, 580), w=1.1, t0=1.1, t1=0.5)])
G['‘'] = (170, [S((92, 580), (60, 660), (72, 720), w=1.1, t0=0.6, t1=1.1)])
G['“'] = (260, [S((92, 580), (60, 660), (72, 720), w=1.1, t0=0.6, t1=1.1), S((182, 580), (150, 660), (162, 720), w=1.1, t0=0.6, t1=1.1)])
G['”'] = (260, [S((92, 720), (94, 690), (62, 580), w=1.1, t0=1.1, t1=0.5), S((182, 720), (184, 690), (152, 580), w=1.1, t0=1.1, t1=0.5)])
G['-'] = (260, [S((40, 240), (222, 252))])
G['–'] = (500, [S((40, 240), (462, 252))])
G['—'] = (860, [S((40, 240), (420, 256), (822, 248))])
G['−'] = (400, [S((50, 270), (352, 278))])
G['_'] = (500, [S((20, -80), (480, -72))])
G['('] = (250, [S((212, 700), (110, 500), (82, 200), (112, 0), (202, -150), t0=0.6, t1=0.6)])
G[')'] = (250, [S((48, 700), (140, 500), (168, 200), (138, 0), (48, -150), t0=0.6, t1=0.6)])
G['['] = (250, [S((192, 700), (92, 700), (92, -150), (192, -150), t0=1, t1=1)])
G[']'] = (250, [S((58, 700), (158, 700), (158, -150), (58, -150), t0=1, t1=1)])
G['/'] = (330, [S((30, -100), (300, 720), t0=0.7, t1=0.7)])
G['\\'] = (330, [S((300, -100), (30, 720), t0=0.7, t1=0.7)])
G['|'] = (200, [S((100, 720), (100, -150), t0=0.8, t1=0.8)])
G['+'] = (400, [S((200, 110), (200, 430)), S((48, 268), (352, 272))])
G['='] = (400, [S((52, 200), (348, 205)), S((52, 340), (348, 345))])
G['×'] = (400, [S((80, 130), (320, 410)), S((320, 130), (80, 410))])
G['<'] = (400, [S((340, 470), (60, 270), (340, 70), t0=0.8, t1=0.8)])
G['>'] = (400, [S((60, 470), (340, 270), (60, 70), t0=0.8, t1=0.8)])
G['%'] = (500, [S(arc(120, 500, 72, 100, 90, 452), w=0.85), S(arc(380, 140, 72, 100, 90, 452), w=0.85),
                S((70, 0), (430, 640))])
G['&'] = (440, [S((352, 20), (122, 400), (142, 580), (212, 625), (272, 560), (202, 400), (62, 250), (62, 82), (152, 0),
                  (262, 42), (360, 222), (400, 260))])
G['*'] = (330, [S((165, 700), (165, 470)), S((60, 640), (270, 530)), S((270, 640), (60, 530))])
G['#'] = (480, [S((180, 620), (140, 0)), S((340, 620), (300, 0)), S((60, 420), (430, 425)), S((50, 200), (420, 205))])
G['@'] = (600, [S(arc(300, 240, 92, 140, 30, 340)), S((392, 380), (390, 140), (440, 100), (520, 160), (550, 300),
                (500, 470), (370, 560), (220, 540), (90, 400), (70, 220), (130, 50), (280, -20), (420, 10))])
G['^'] = (330, [S((60, 480), (165, 650), (270, 480), t0=0.8, t1=0.8)])
G['~'] = (420, [S((50, 250), (120, 310), (210, 270), (300, 230), (370, 290))])
G['$'] = (380, [S((322, 530), (262, 590), (160, 595), (80, 520), (110, 400), (250, 300), (322, 170), (272, 40),
                  (150, 0), (60, 40), (30, 100)), S((190, 680), (180, -80))])
G['§'] = (360, [S((272, 600), (220, 650), (130, 640), (90, 570), (150, 470), (250, 400), (280, 300), (220, 230)),
                S((140, 400), (90, 330), (110, 230), (210, 160), (262, 80), (230, 0), (130, -20), (70, 40))])
G['·'] = (190, [dot(95, 260)])
G['•'] = (300, [S(arc(150, 260, 40, 40, 0, 360), w=1.3, t0=1, t1=1)])
G['…'] = (600, [dot(100, 22), dot(300, 22), dot(500, 22)])
G['«'] = (380, [S((180, 380), (70, 230), (180, 80)), S((330, 380), (220, 230), (330, 80))])
G['»'] = (380, [S((70, 380), (180, 230), (70, 80)), S((220, 380), (330, 230), (220, 80))])
G['→'] = (640, [S((40, 260), (580, 270)), S((460, 380), (590, 268), (460, 150), t0=0.8, t1=0.8)])
G['←'] = (640, [S((60, 270), (600, 260)), S((180, 380), (50, 268), (180, 150), t0=0.8, t1=0.8)])
G['▸'] = (400, [S((90, 400), (330, 260), (90, 120), (90, 400), w=1.2, t0=1, t1=1)])
G['○'] = (500, [S(arc(250, 260, 170, 170, 95, 460), t0=1, t1=1)])
G['◆'] = (440, [S((220, 470), (390, 260), (220, 50), (50, 260), (220, 470), w=1.1, t0=1, t1=1),
                S((220, 380), (300, 260), (220, 140), (140, 260), (220, 380), w=1.6, t0=1, t1=1)])
G['★'] = (520, [S(*[(260 + (230 if k % 2 == 0 else 95) * math.sin(math.radians(36 * k)),
                     280 + (230 if k % 2 == 0 else 95) * math.cos(math.radians(36 * k))) for k in range(11)],
                  t0=1, t1=1)])
G['✕'] = (400, [S((80, 90), (320, 430)), S((320, 90), (80, 430))])

# accented letters, built from the base and its mark
def mark_dots(base, xs, y):
    adv, strokes = G[base]
    return (adv, strokes + [dot(x, y, 1.05) for x in xs])
def mark_acute(base, x, y, flip=False):
    adv, strokes = G[base]
    s = S((x - 40, y + 80), (x + 30, y), t0=1, t1=0.6) if flip else S((x + 40, y + 80), (x - 30, y), t0=1, t1=0.6)
    return (adv, strokes + [s])
G['ä'] = mark_dots('a', [120, 250], 590); G['ö'] = mark_dots('o', [115, 250], 590)
G['ü'] = mark_dots('u', [110, 250], 590); G['Ä'] = mark_dots('A', [160, 300], 790)
G['Ö'] = mark_dots('O', [150, 300], 790); G['Ü'] = mark_dots('U', [150, 310], 790)
G['é'] = mark_acute('e', 190, 520); G['è'] = mark_acute('e', 170, 520, flip=True)
G['á'] = mark_acute('a', 190, 520); G['à'] = mark_acute('a', 170, 520, flip=True)
G['í'] = (220, [S((45, 385), (92, 430), (86, 60), (122, 2), (180, 35)), S((140, 600), (75, 510), t0=1, t1=0.6)])
G['ó'] = mark_acute('o', 190, 520); G['ú'] = mark_acute('u', 190, 520)
G['ç'] = (360, G['c'][1] + [S((170, 0), (200, -60), (150, -110), (100, -100), w=0.7)])
G['ß'] = (390, [S((60, -30), (70, 480), (130, 640), (230, 652), (290, 570), (210, 420), (302, 300), (310, 120),
                  (212, 0), (140, 20))])
G[' '] = (300, [])
G[' '] = (260, [])
G[' '] = (120, [])


FINALS = 'adehklmntu'
ALTS = 'abcdefghijklmnopqrstuvwxyz'
BLOTTED = 'gy'   # on their third form: about one blot in 120 letters of the game's text


def jitter(strokes, rng, amt=15):
    out = []
    for st in strokes:
        if isinstance(st, Blot):
            out.append(st)
            continue
        if isinstance(st, Dot):
            out.append(Dot(st.x + rng.uniform(-8, 8), st.y + rng.uniform(-8, 8), st.r * rng.uniform(0.9, 1.1)))
            continue
        n = len(st.pts)
        if n > 14:   # an arc: move it as one piece and breathe its size
            dx, dy, k = rng.uniform(-amt, amt), rng.uniform(-amt / 2, amt / 2), rng.uniform(0.96, 1.04)
            cx = sum(p[0] for p in st.pts) / n
            pts = [((x - cx) * k + cx + dx, y + dy) for x, y in st.pts]
        else:
            pts = [(x + rng.uniform(-amt, amt), y + rng.uniform(-amt, amt)) for x, y in st.pts]
        out.append(S(*pts, w=st.w * rng.uniform(0.95, 1.06), t0=st.t0, t1=st.t1))
    return out


def with_tail(strokes):
    # the clerk's last stroke of a word runs on, low and rising, into the space after it
    out = list(strokes)
    i = max(k for k, st in enumerate(out) if isinstance(st, S))
    st = out[i]
    (x0, y0), (x1, y1) = st.pts[-2], st.pts[-1]
    out[i] = S(*st.pts[:-1], (x1 + 10, y1 - 18), (x1 + 110, y1 + 2), (x1 + 215, y1 + 85), w=st.w, t0=st.t0, t1=0.3)
    return out


def build(path, family='Clerk of Boston', seed=1739):
    from fontTools.fontBuilder import FontBuilder
    from fontTools.pens.ttGlyphPen import TTGlyphPen
    import unicodedata
    names = {' ': 'space'}
    variants = {}
    vr = random.Random(seed + 1)
    for c in ALTS:
        variants[('uni%04X' % ord(c)) + '.alt'] = (G[c][0], jitter(G[c][1], vr))
    for c in ALTS:
        strokes = jitter(G[c][1], vr)
        if c in BLOTTED:
            first = next(st for st in strokes if isinstance(st, S))
            x, y = first.pts[0]
            strokes = strokes + [Blot(x + vr.uniform(0, 30), y + vr.uniform(20, 45), vr.uniform(78, 92),
                                      drops=vr.choice([2, 2, 3]))]
        variants[('uni%04X' % ord(c)) + '.alt2'] = (G[c][0], strokes)
    for c in FINALS:
        variants[('uni%04X' % ord(c)) + '.fina'] = (G[c][0], with_tail(G[c][1]))
    order = ['.notdef'] + [names.get(c) or ('uni%04X' % ord(c)) for c in G] + list(variants)
    cmap = {ord(c): (names.get(c) or 'uni%04X' % ord(c)) for c in G}
    fb = FontBuilder(UPM, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap(cmap)
    glyphs, metrics = {}, {}
    rng = random.Random(seed)
    pen = TTGlyphPen(None)
    pen.moveTo((50, 0)); pen.lineTo((50, 600)); pen.lineTo((350, 600)); pen.lineTo((350, 0)); pen.closePath()
    glyphs['.notdef'] = pen.glyph(); metrics['.notdef'] = (400, 50)
    shapes = {}
    items = [(cmap[ord(ch)], adv, strokes) for ch, (adv, strokes) in G.items()] + \
            [(name, adv, strokes) for name, (adv, strokes) in variants.items()]
    for name, adv, strokes in items:
        pen = TTGlyphPen(None)
        if strokes:
            geom = unary_union([sweep(s, rng) for s in strokes])
            from shapely import affinity
            geom = affinity.rotate(geom, rng.uniform(-2.6, 2.6), origin=(adv * HSCALE / 2, 0))
            geom = affinity.translate(geom, 0, rng.uniform(-11, 11))
            geom = roughen(geom, rng)
            shapes[name] = geom
            polys = geom.geoms if isinstance(geom, MultiPolygon) else [geom]
            for p in polys:
                if p.is_empty:
                    continue
                p = orient(p, sign=-1.0)
                for ring in [p.exterior] + list(p.interiors):
                    pts = [(round(x), round(y)) for x, y in list(ring.coords)[:-1]]
                    ded = [pts[0]] + [q for i, q in enumerate(pts[1:], 1) if q != pts[i - 1]]
                    if len(ded) < 3:
                        continue
                    pen.moveTo(ded[0])
                    for q in ded[1:]:
                        pen.lineTo(q)
                    pen.closePath()
        g = pen.glyph()
        glyphs[name] = g
        metrics[name] = (round(adv * HSCALE), 0)
    fb.setupGlyf(glyphs)
    for name, g in glyphs.items():
        g.recalcBounds(fb.font['glyf'])
        metrics[name] = (metrics[name][0], getattr(g, 'xMin', 0))
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=900, descent=-300)
    fb.setupNameTable({'familyName': family, 'styleName': 'Regular'})
    fb.setupOS2(sTypoAscender=900, sTypoDescender=-300, sTypoLineGap=100, usWinAscent=950, usWinDescent=330,
                sxHeight=X, sCapHeight=CAP)
    fb.setupPost()
    from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
    u = lambda c: 'uni%04X' % ord(c)
    letters = [cmap[ord(c)] for c in G if c.isalpha() or c in ".,;:!?)]’'-…\"”"] + list(variants)
    fea = f'''languagesystem DFLT dflt;
languagesystem latn dflt;
@letters = [{' '.join(letters)}];
@fbase = [{' '.join(u(c) for c in FINALS)}];
@fina = [{' '.join(u(c) + '.fina' for c in FINALS)}];
@def = [{' '.join(u(c) for c in ALTS)}];
@alt = [{' '.join(u(c) + '.alt' for c in ALTS)}];
@alt2 = [{' '.join(u(c) + '.alt2' for c in ALTS)}];
lookup TOFINA {{ sub @fbase by @fina; }} TOFINA;
lookup TOALT {{ sub @def by @alt; }} TOALT;
lookup TOALT2 {{ sub @def by @alt2; }} TOALT2;
feature calt {{
  lookup FINAL {{
    ignore sub @fbase' @letters;
    sub @fbase' lookup TOFINA;
  }} FINAL;
  lookup ALTERNATE {{
    sub @def @def' lookup TOALT;
    sub @alt @def' lookup TOALT2;
  }} ALTERNATE;
}} calt;
'''
    addOpenTypeFeaturesFromString(fb.font, fea)
    fb.save(path)
    return shapes


if __name__ == '__main__':
    import sys
    from pathlib import Path
    build(sys.argv[1] if len(sys.argv) > 1 else str(Path(__file__).resolve().parent / 'clerk-of-boston.ttf'))
    print('glyphs', len(G))
