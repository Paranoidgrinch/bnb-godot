#!/usr/bin/env python3
"""The fight backgrounds, one per act, made from paintings and prints in src/ (public domain or CC0; src/sources.json
names each one). The user chose them on 2026-11-11; this turns each original into the picture the game shows, by
fixed image operations only — cropping to 16:9, colour grading (duotones, contrast curves), light fall-off and
vignettes, and in Act IV the removal of the people from the temple hall by OpenCV's Telea inpainting (a classical
fill from the surrounding pixels, 2004; no trained model). No generative AI is involved anywhere.

    act 1  Luke Fildes, Applicants for Admission to a Casual Ward (1874): cold, green-grey, sickly lamp light, mist
    act 2  Piranesi, Carceri d'invenzione, The Gothic Arch (1761; The Met, CC0): warm sepia on near-black
    act 3  Gustave Dore, Inferno, The Wood of the Suicides (1861): deep green to mould green
    act 4  David Roberts, The Hypostyle Hall at Abu Simbel (1849): emptied, lit only from the far doorway
    act 5  Etienne Trouvelot, Part of the Milky Way (1881): turned across, black-blue sky, the stars brighter

The game lays its own scrim over these (SessionScreen.ActScrimAlpha), so they are written bright as the user saw
them unveiled. Writes ../act_N_*.png at 2560 x 1440. Deterministic. Needs numpy, Pillow, opencv-python-headless.
"""
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageEnhance, ImageFilter

SRC = Path(__file__).resolve().parent / 'src'
OUT = Path(__file__).resolve().parent
NAMES = {1: 'act_1_city', 2: 'act_2_archives', 3: 'act_3_green_docket', 4: 'act_4_licensing_labyrinth', 5: 'act_5_divine_ledger'}


def abu_simbel_without_people():
    """The temple hall with its visitors taken out: three regions (the group at the lower left, the men in the
    doorway, the group by the right-hand pillars), filled inward from their surroundings."""
    im = cv2.imread(str(SRC / '4f.jpg'))
    k = im.shape[1] / 1400   # the regions were drawn on a 1400 px wide copy
    mask = np.zeros(im.shape[:2], np.uint8)
    for p in REGIONS:
        cv2.fillPoly(mask, [np.array([(int(x * k), int(y * k)) for x, y in p])], 255)
    mask = cv2.dilate(mask, np.ones((9, 9), np.uint8))
    out = cv2.inpaint(im, mask, 25, cv2.INPAINT_TELEA)
    return Image.fromarray(cv2.cvtColor(out, cv2.COLOR_BGR2RGB))


REGIONS = [
    [(175, 640), (245, 635), (300, 680), (345, 720), (390, 725), (460, 695), (530, 740), (570, 790), (570, 900), (400, 905), (330, 870), (240, 860), (175, 820)],
    [(500, 588), (605, 588), (618, 640), (650, 712), (500, 712)],
    [(788, 640), (880, 640), (880, 752), (788, 752)],
]

W, H = 2560, 1440

def crop169(im, fy=0.5, zoom=1.0, fx=0.5):
    w0, h0 = im.size
    w = w0 / zoom; h = w * 9 / 16
    if h > h0: h = h0 / zoom; w = h * 16 / 9
    x = (w0 - w) * fx; y = (h0 - h) * fy
    return im.crop((int(x), int(y), int(x + w), int(y + h))).resize((W, H), Image.LANCZOS)

def arr(im): return np.asarray(im.convert('RGB'), np.float32) / 255
def img(a):
    a = np.asarray(a)
    if a.ndim == 3 and a.shape[2] == 1: a = a[..., 0]
    return Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))

def light(cx, cy, r, floor=0.25, p=1.6):
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    d = np.sqrt(((xx - cx * W) / W) ** 2 + ((yy - cy * H) / H * 0.8) ** 2) / r
    return (floor + (1 - floor) * np.clip(1 - d, 0, 1) ** p)[..., None]

def duotone(a, dark, light_, gamma=1.0):
    l = (a @ np.array([0.3, 0.59, 0.11], np.float32))[..., None] ** gamma
    return np.array(dark) / 255 * (1 - l) + np.array(light_) / 255 * l

def act1():
    a = arr(crop169(Image.open(SRC / '1h.jpg'), 0.5))
    # fantastical: the street goes cold and green-grey, the lamp light warm and sickly, a haze rises
    l = (a @ np.array([0.3, 0.59, 0.11], np.float32))[..., None]
    cold = duotone(a, (6, 10, 14), (150, 170, 150), 1.1)
    a = cold * 0.65 + a * 0.35
    glow = img(np.clip(l - 0.35, 0, 1) * 2.2).filter(ImageFilter.GaussianBlur(40))
    a = a + arr(glow)[..., :1] * np.array([0.9, 0.7, 0.35]) * 0.55
    fog = np.linspace(0, 1, H, dtype=np.float32)[:, None, None] ** 2 * 0.18
    a = a * (1 - fog) + np.array([0.45, 0.5, 0.48]) * fog
    return a * light(0.42, 0.55, 0.75, 0.35)

def act2():
    a = arr(crop169(Image.open(SRC / '2g.jpg'), 0.5, 1.06))
    a = duotone(a, (12, 9, 8), (214, 200, 176), 1.25)
    return a * light(0.5, 0.45, 0.8, 0.4)

def act3():
    a = arr(crop169(Image.open(SRC / '3g.jpg'), 0.42))
    a = duotone(a, (3, 12, 6), (150, 196, 120), 1.15)
    # mould-green haze between the trunks
    yy = np.linspace(0, 1, H, dtype=np.float32)[:, None, None]
    a = a + np.array([0.05, 0.12, 0.04]) * (1 - yy) * 0.6
    return a * light(0.5, 0.5, 0.85, 0.35)

def act4():
    a = arr(crop169(abu_simbel_without_people(), 0.62))
    a = arr(ImageEnhance.Color(img(a)).enhance(0.8))
    a = a ** 1.25
    # the only light comes from the doorway at the far end; the hall falls into torch-dark round it
    # the retouched floor at the lower left sinks into shadow
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    corner = np.clip(1 - np.sqrt(((xx / W) / 0.45) ** 2 + ((1 - yy / H) / 0.6) ** 2), 0, 1)[..., None]
    a = a * (1 - corner * 0.85)
    return a * light(0.42, 0.5, 0.68, 0.26, 1.2)

def act5():
    im = Image.open(SRC / '5f.jpg')
    im = im.crop((int(im.width * 0.08), int(im.height * 0.06), int(im.width * 0.92), int(im.height * 0.94)))
    im = im.rotate(90, expand=True)   # the band of the Milky Way across the arena
    a = arr(crop169(im, 0.5, 1.22, 0.42))
    l = (a @ np.array([0.3, 0.59, 0.11], np.float32))[..., None]
    bg = np.percentile(l, 40)
    s = np.clip((l - bg) / (np.percentile(l, 99.7) - bg), 0, 1) ** 1.6
    stars = arr(img(s[..., 0]).filter(ImageFilter.GaussianBlur(1.2)))[..., :1]
    a = np.array([0.012, 0.014, 0.035]) + s * np.array([1.0, 1.0, 1.05]) * 1.15 + stars * np.array([0.35, 0.4, 0.65])
    return a



if __name__ == '__main__':
    for n, f in ((1, act1), (2, act2), (3, act3), (4, act4), (5, act5)):
        img(f()).save(OUT / f'{NAMES[n]}.png')
        print('wrote', NAMES[n])
