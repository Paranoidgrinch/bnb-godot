#!/usr/bin/env python3
"""The map art that comes from scans, made once here so the game only places pictures (scripts/MapArt draws
the rest at run time). Writes next to itself:

    egypt_<id>.png     Act IV: pieces of Book of the Dead papyri (Hunefer and Ani, public domain; Nany, The
                       Metropolitan Museum of Art, CC0), their own papyrus taken away so only paint and ink remain
    sphinx_l/r.png     Act IV: the sphinxes along the processional way, the lion sign (U+130ED) of Noto Sans
                       Egyptian Hieroglyphs (SIL OFL 1.1, src/OFL-NotoSansEgyptianHieroglyphs.txt) filled sand-gold
    weik_<name>.png    Act III: the eagle cartouche, the painted compass and the scale of the Weikersheim forest map
                       (Landesarchiv Baden-Wuerttemberg, CC BY 4.0), as ink that darkens what it lies on
    pieces.json        the pieces, their kind (script or painted figure), where they go, their size

The sources are the crops in src/ (see src/sources.json); every picture is drawn at twice the size it shows at,
as the rest of the map is. Deterministic. Needs numpy, Pillow, scipy."""
import json, os
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

HERE = os.path.dirname(os.path.abspath(__file__))
S = 2
SIDE_MAX = 124          # a margin piece is at most this wide on the map
FRIEZE_W = 140          # each half of the frieze of gods, beside the boss
BAND_W = 300            # the band of script under the title


def mode_of(pix, qq):
    keys = qq[:, 0] * 256 + qq[:, 1] * 16 + qq[:, 2]
    vals, cnt = np.unique(keys, return_counts=True)
    mk = vals[np.argmax(cnt)]
    m = np.array([mk // 256, (mk // 16) % 16, mk % 16])
    return pix[(np.abs(qq - m) <= 1).all(1)].mean(0)


def matte(im, whole, width_px):
    """RGBA of a papyrus piece at `width_px`: the papyrus out, paint and ink kept, their edges un-tinted.
    The papyrus is, tile by tile, the commonest colour (a figure or a column of signs never fills a tile's mode),
    held to the scroll's own papyrus colour `whole` where a tile is all paint."""
    f = width_px / im.width
    im = im.resize((max(1, round(im.width * f)), max(1, round(im.height * f))), Image.LANCZOS)
    a = np.asarray(im, np.float32) / 255
    h, w = a.shape[:2]
    q = np.clip((a * 16).astype(np.int32), 0, 15)
    T = 64
    ty, tx = max(1, round(h / T)), max(1, round(w / T))
    grid = np.zeros((ty, tx, 3), np.float32)
    for i in range(ty):
        for j in range(tx):
            ys, ye = i * h // ty, (i + 1) * h // ty
            xs, xe = j * w // tx, (j + 1) * w // tx
            ys2, ye2, xs2, xe2 = max(0, ys - T // 2), min(h, ye + T // 2), max(0, xs - T // 2), min(w, xe + T // 2)
            loc = mode_of(a[ys2:ye2, xs2:xe2].reshape(-1, 3), q[ys2:ye2, xs2:xe2].reshape(-1, 3))
            grid[i, j] = loc if np.abs(loc - whole).max() < 0.09 else whole
    paper = np.asarray(Image.fromarray((grid * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255
    diff = a - paper
    lum = diff.mean(-1)
    chroma = np.sqrt(((diff - lum[..., None]) ** 2).sum(-1))
    d = np.maximum(np.abs(lum), chroma * 1.6)
    near = d[d < 0.12]
    noise = np.percentile(near, 75) if near.size else 0.05
    alpha = np.clip((d - (noise * 1.25 + 0.025)) / 0.10, 0, 1)
    lab, n = ndi.label(alpha > 0.5)   # papyrus fibres leave lone specks: the smallest islands go
    if n:
        sizes = ndi.sum(np.ones_like(alpha), lab, range(1, n + 1))
        alpha = np.where(np.isin(lab, 1 + np.where(sizes < max(4, w * h / 20000))[0]), 0, alpha)
    alpha = ndi.gaussian_filter(alpha, 0.5)
    rgb = np.clip((a - paper * (1 - alpha[..., None])) / np.maximum(alpha[..., None], 0.08), 0, 1)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    edge = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy))
    alpha = alpha * np.clip(edge / 3, 0, 1)
    return Image.fromarray((np.dstack([rgb, alpha]) * 255).astype(np.uint8), 'RGBA')


def ink_of(im, width_px):
    """A piece of a paper map as ink: its paper (the bright side of each neighbourhood) divided out, then turned
    into colour + alpha so that laid over white it gives back the multiplied ink."""
    f = width_px / im.width
    im = im.resize((round(im.width * f), round(im.height * f)), Image.LANCZOS)
    a = np.asarray(im, np.float32) / 255
    h, w = a.shape[:2]
    paper = np.stack([ndi.gaussian_filter(ndi.maximum_filter(a[..., c], size=max(5, int(min(h, w) * 0.12))),
                                          max(3, min(h, w) * 0.08)) for c in range(3)], -1)
    m = np.clip(a / np.maximum(paper, 1e-3), 0, 1)
    m = np.clip((m - 0.1) / 0.8, 0, 1) ** 1.2
    m = np.where(m.mean(-1, keepdims=True) > 0.93, 1.0, m)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    edge = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy))
    m = 1 - (1 - m) * np.clip(edge / max(4, min(h, w) * 0.06), 0, 1)[..., None]
    alpha = 1 - m.min(-1)
    rgb = np.clip((m - (1 - alpha[..., None])) / np.maximum(alpha[..., None], 1e-3), 0, 1)
    return Image.fromarray((np.dstack([rgb, alpha]) * 255).astype(np.uint8), 'RGBA')


def sphinx(size_px, flip):
    """The lion sign at `size_px`, its outline black and its body filled, as the prototype's glyph() drew it."""
    from PIL import ImageDraw, ImageFont
    f = ImageFont.truetype(f'{HERE}/src/NotoSansEgyptianHieroglyphs-Regular.ttf', size_px)
    im = Image.new('L', (int(size_px * 1.7), int(size_px * 1.7)), 0)
    ImageDraw.Draw(im).text((im.width / 2, im.height / 2), chr(0x130ED), font=f, fill=255, anchor='mm')
    if flip:
        im = im.transpose(Image.FLIP_LEFT_RIGHT)
    im = Image.fromarray(ndi.grey_dilation(np.asarray(im), size=2))
    a = np.asarray(im) > 90
    sil = ndi.binary_fill_holes(ndi.binary_closing(a, iterations=max(2, size_px // 12)))
    out = np.zeros(a.shape + (4,), np.uint8)
    out[sil] = (226, 196, 120, 255)
    out[a] = (28, 22, 16, 255)
    bb = Image.fromarray(out, 'RGBA').getbbox()
    return Image.fromarray(out, 'RGBA').crop(bb)


def main():
    spec = json.load(open(f'{HERE}/src/pieces.json'))
    out = []
    for p in spec['pieces']:
        im = Image.open(f"{HERE}/src/{p['id']}.jpg").convert('RGB')
        whole = np.array(spec['papyrus'][p['scan']], np.float32)
        if p['where'] == 'top':            # the frieze: two halves
            for half, crop in (('a', (0, 0, im.width // 2, im.height)), ('b', (im.width // 2, 0, im.width, im.height))):
                m = matte(im.crop(crop), whole, FRIEZE_W * S)
                m.save(f"{HERE}/egypt_{p['id']}{half}.png")
                out.append(dict(id=p['id'] + half, label=p['label'], kind='frieze', w=m.width / S, h=m.height / S))
            continue
        width = BAND_W if p['where'] == 'foot' else min(p['width'], SIDE_MAX)
        m = matte(im, whole, width * S)
        m.save(f"{HERE}/egypt_{p['id']}.png")
        kind = 'band' if p['where'] == 'foot' else ('script' if p['text'] else 'figure')
        out.append(dict(id=p['id'], label=p['label'], kind=kind, w=m.width / S, h=m.height / S))
    for name, width in (('cartouche', 140), ('compass', 96), ('scala', 180)):
        m = ink_of(Image.open(f'{HERE}/src/weik_{name}.jpg').convert('RGB'), width * S)
        m.save(f'{HERE}/weik_{name}.png')
        out.append(dict(id=f'weik_{name}', label=name, kind='weik', w=m.width / S, h=m.height / S))
    for name, flip in (('l', False), ('r', True)):
        m = sphinx(12 * S, flip)
        m.save(f'{HERE}/sphinx_{name}.png')
        out.append(dict(id=f'sphinx_{name}', label='sphinx', kind='sphinx', w=m.width / S, h=m.height / S))
    json.dump(out, open(f'{HERE}/pieces.json', 'w'), indent=1)
    print('wrote', len(out), 'pieces')


if __name__ == '__main__':
    main()
