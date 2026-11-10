#!/usr/bin/env python3
# The UI font, "Moonvine Polter": writes polter.ttf next to itself from src/ShortStack-Regular.ttf.
#     python3 theme/font/make_ui_font.py
# Needs fontTools and skia-pathops. Deterministic: the same script writes the same glyphs.
#
# Short Stack made heavy for the screens (user, 2026-11-10, mix "A" of three): every glyph is grown by an oval
# nib (stems a little thicker than bars), condensed so the heavier letters take no more room than the thin
# ones did, and each letter hops and leans a little on the line, after Henny Penny's bounce. Figures and marks
# stay straight, so a number reads level. Short Stack is under the OFL with a Reserved Font Name, so the result
# carries a name of its own; OFL-polter.txt is its licence.
import math, random
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.cu2quPen import Cu2QuPen
from fontTools.misc.transform import Transform
import pathops

HERE = Path(__file__).resolve().parent
FAMILY = 'Moonvine Polter'
NIB = (68, 42)        # half-widths of the nib across and along, font units (Short Stack's stem is ~180 of 2048)
NIB_TILT = -12        # degrees
CONDENSE = 0.84
HOP, LEAN, SWELL = 36, 3.5, 0.03  # largest lift (units), lean (degrees) and change of size per letter
MIDDLE = 550          # the height a letter leans about: half Short Stack's x-height
LETTERS = set('ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz')


def grown(font, name):
    """The glyph's outline grown by the nib: squash the plane so the nib is round, stroke round, unsquash."""
    rec = DecomposingRecordingPen(font.getGlyphSet())
    font.getGlyphSet()[name].draw(rec)
    r = 100.0
    nib = Transform().rotate(math.radians(NIB_TILT)).scale(NIB[0] / r, NIB[1] / r)
    path = pathops.Path()
    rec.replay(TransformPen(path.getPen(), nib.inverse()))
    if not list(path.segments):
        return None, nib
    stroke = pathops.Path(path)
    stroke.stroke(2 * r, pathops.LineCap.ROUND_CAP, pathops.LineJoin.ROUND_JOIN, 4)
    stroke.convertConicsToQuads()
    out = pathops.op(path, stroke, pathops.PathOp.UNION, fix_winding=True, keep_starting_points=False)
    out.convertConicsToQuads()
    return out, nib


def build(out_path):
    font = TTFont(HERE / 'src' / 'ShortStack-Regular.ttf')
    letters = {g for c, g in font.getBestCmap().items() if chr(c) in LETTERS}
    glyf, hmtx = font['glyf'], font['hmtx']
    a = NIB[0]
    done = {}
    for name in font.getGlyphOrder():
        adv, _ = hmtx[name]
        # the outline grows by `a` on each side; the letter keeps half of that as extra room, so it sets tighter
        new_adv = (adv + a) * CONDENSE
        path, nib = grown(font, name)
        pen = TTGlyphPen(None)
        if path is not None:
            place = Transform().scale(CONDENSE, 1).translate(a / 2, 0)
            if name in letters:
                rng = random.Random(f'A:{name}')  # the seed of the mix that was chosen
                cx = adv / 2 * CONDENSE
                size = 1 + rng.uniform(-SWELL, SWELL)
                place = (Transform().translate(0, rng.uniform(-HOP, HOP))
                         .translate(cx, MIDDLE).rotate(math.radians(rng.uniform(-LEAN, LEAN)))
                         .scale(size).translate(-cx, -MIDDLE).transform(place))
            path.draw(Cu2QuPen(TransformPen(pen, place.transform(nib)), max_err=1.0, reverse_direction=True))
        glyph = pen.glyph()
        glyph.recalcBounds(glyf)
        done[name] = (glyph, round(new_adv), getattr(glyph, 'xMin', 0))
    for name, (glyph, adv, lsb) in done.items():
        glyf[name] = glyph
        hmtx[name] = (adv, lsb)
    # Short Stack's pairs were spaced for the thin letters; the build tables name a tool that no longer made it
    for table in ('GPOS', 'GDEF', 'FFTM'):
        if table in font:
            del font[table]
    for record in font['name'].names:
        if record.nameID in (1, 4, 16):
            record.string = FAMILY
        elif record.nameID == 6:
            record.string = FAMILY.replace(' ', '')
        elif record.nameID == 3:
            record.string = FAMILY + ';1.0'
        elif record.nameID == 0:
            record.string = str(record.toUnicode()) + ' Modified as Moonvine Polter (theme/font/make_ui_font.py).'
    font['OS/2'].usWeightClass = 700
    font.save(out_path)
    print('wrote', out_path)


if __name__ == '__main__':
    import sys
    build(sys.argv[1] if len(sys.argv) > 1 else str(HERE / 'polter.ttf'))
