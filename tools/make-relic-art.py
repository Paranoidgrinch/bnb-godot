#!/usr/bin/env python3
"""One placeholder picture per relic slot: the file's own name, the relic's name, and the word PLACEHOLDER.

    tools/make-relic-art.py                      # every relic slot that has no picture yet
    tools/make-relic-art.py --only levy_stamp     tools/make-relic-art.py --force    tools/make-relic-art.py --clean

The plate is square, 512 x 512, because the shelf draws a relic as a small square (about 34 points, larger on
hover). Nothing written on a placeholder survives that reduction, and it does not have to: the shelf already
prints the slot code itself while a slot is empty. What a placeholder proves is that the right FILE reached
the right slot, which is read at the size the hover gives it.
"""
import argparse

from placeholder_art import DIM, FONT_BOLD, FONT_MONO, INK, arguments, block, document, plate, relic_slots, run

SIZE = (512, 512)


def paint(code, name):
    image, draw = plate(SIZE)
    y = block(draw, f"{code}.png", top=46, width=SIZE[0], path=FONT_MONO, size=26, fill=DIM, margin=30)
    y = block(draw, name, top=y + 30, width=SIZE[0], path=FONT_BOLD, size=56, fill=INK, margin=36)
    block(draw, "PLACEHOLDER", top=max(y + 36, SIZE[1] - 104), width=SIZE[0], path=FONT_BOLD, size=42,
          fill=DIM, margin=36)
    return image


if __name__ == "__main__":
    args = arguments(argparse.ArgumentParser(description=__doc__)).parse_args()
    run("relics", relic_slots(document(args.blueprint)), paint, args)
