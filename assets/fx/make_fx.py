#!/usr/bin/env python3
"""Pictures for the fight's numbers, made from the sources in src/. Writes next to itself:

    shield_block.png   the shield beside a block number: the outline of lorc's "skull-shield" from game-icons.net
                       (CC BY 3.0, https://game-icons.net/1x1/lorc/skull-shield.html), the skull left out, filled
                       steel blue with a pale rim (user, 2026-11-11)
    drops/drop_NN.png  the drops and small blots flung round a wound's number, cut one by one out of real ink:
                       "Ink-stain-texture-6" and "-1" by designshard (CC BY 2.0, Wikimedia Commons) and "Slightly blue
                       ink splatter" by Balthazar (public domain). White, their ink as alpha: the game tints them.
"""
from pathlib import Path
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

HERE = Path(__file__).resolve().parent


def shield():
    a = np.asarray(Image.open(HERE / 'src' / 'skull-shield.png').convert('RGBA'))[..., 3] > 127
    solid = ndi.binary_fill_holes(a)                       # the outline, filled: the skull goes
    rim = solid & ~ndi.binary_erosion(solid, iterations=22)
    h, w = a.shape
    out = np.zeros((h, w, 4), np.float32)
    steel = np.array([0.36, 0.50, 0.60]); light = np.array([0.79, 0.85, 0.90])
    body = steel * (1.12 - 0.3 * np.linspace(0, 1, h)[:, None, None])
    out[..., :3] = np.where(rim[..., None], light, body)
    out[..., 3] = ndi.gaussian_filter(solid.astype(np.float32), 0.8)
    img = Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), 'RGBA')
    img.resize((128, 128), Image.LANCZOS).save(HERE / 'shield_block.png')


def ink_of(path):
    """The ink of a scan as 0..1: its paper (the bright side of each neighbourhood) divided out."""
    l = np.asarray(Image.open(path).convert('RGB'), np.float32).mean(-1) / 255
    paper = ndi.gaussian_filter(ndi.maximum_filter(l, size=max(5, min(l.shape) // 12)), max(3, min(l.shape) // 18))
    return np.clip((1 - l / np.maximum(paper, 1e-3) - 0.08) / 0.55, 0, 1)


def drops():
    out = HERE / 'drops'
    out.mkdir(exist_ok=True)
    for old in out.glob('drop_*.png'):
        old.unlink()
    n = 0
    # (scan, smallest area kept, largest): the big splashes themselves are left out, only drops and small blots
    for name, lo, hi in (('ink-stain-6.jpg', 30, 2000), ('ink-stain-1.jpg', 60, 2000), ('ink-splatter.png', 30, 2000)):
        ink = ink_of(HERE / 'src' / name)
        lab, k = ndi.label(ndi.binary_dilation(ink > 0.25, iterations=1))
        for i, sl in enumerate(ndi.find_objects(lab), start=1):
            area = (lab[sl] == i).sum()
            if not lo <= area <= hi:
                continue
            ys, xs = sl
            pad = 3
            y0, y1 = max(0, ys.start - pad), min(ink.shape[0], ys.stop + pad)
            x0, x1 = max(0, xs.start - pad), min(ink.shape[1], xs.stop + pad)
            a = ink[y0:y1, x0:x1] * (lab[y0:y1, x0:x1] == i)
            a = ndi.gaussian_filter(a, 0.4)
            rgba = np.zeros(a.shape + (4,), np.uint8)
            rgba[..., :3] = 255
            rgba[..., 3] = (np.clip(a * 1.3, 0, 1) * 255).astype(np.uint8)
            img = Image.fromarray(rgba, 'RGBA')
            f = min(1.0, 64 / max(img.size))
            img.resize((max(1, round(img.width * f)), max(1, round(img.height * f))), Image.LANCZOS).save(out / f'drop_{n:02d}.png')
            n += 1
    print('drops', n)


if __name__ == '__main__':
    shield()
    drops()
