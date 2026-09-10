#!/usr/bin/env python3
"""One placeholder picture per card slot: the file's own name, the card's name, and the word PLACEHOLDER.

    tools/make-card-art.py                     # every card slot that has no picture yet
    tools/make-card-art.py --only levy_stamp    tools/make-card-art.py --force    tools/make-card-art.py --clean

The plate is 488 x 440 — four times the 122 x 110 window the frame prints it into, drawn large on purpose so
the mip chain does the reduction (a placeholder that glitters teaches nothing about the real picture).
Cropped to FILL that window, so nothing important may sit near an edge; everything here is centred.
"""
import argparse

from placeholder_art import DIM, FONT_BOLD, FONT_MONO, INK, arguments, block, card_slots, document, plate, run

SIZE = (488, 440)


def paint(code, name):
    image, draw = plate(SIZE)
    y = block(draw, f"{code}.png", top=40, width=SIZE[0], path=FONT_MONO, size=26, fill=DIM, margin=28)
    y = block(draw, name, top=y + 26, width=SIZE[0], path=FONT_BOLD, size=52, fill=INK, margin=34)
    block(draw, "PLACEHOLDER", top=max(y + 34, SIZE[1] - 96), width=SIZE[0], path=FONT_BOLD, size=40,
          fill=DIM, margin=34)
    return image


if __name__ == "__main__":
    args = arguments(argparse.ArgumentParser(description=__doc__)).parse_args()
    run("cards", card_slots(document(args.blueprint)), paint, args)
