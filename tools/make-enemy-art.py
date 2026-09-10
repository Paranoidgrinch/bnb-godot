#!/usr/bin/env python3
"""One placeholder picture per body — every enemy, every elite, every boss, and the player's own — as a stick
figure with its file name over its head.

    tools/make-enemy-art.py                         # every body slot that has no picture yet
    tools/make-enemy-art.py --only queue_imp         tools/make-enemy-art.py --force    tools/make-enemy-art.py --clean

The figure's SIZE is the kind of body, which the document already worked out from the rooms the body is met in
(`Presentation.Enemies[id].Frame`): a standard body is small, an elite is bigger, a boss is big and wears
horns. A mimic sits with the elites — it is met where they are met — and every plate prints its role in words
as well, so nothing has to be read off a silhouette.

The plate is 600 x 900 portrait on TRANSPARENCY, which is the shape the arena asks for: a body is drawn 150
points tall with its aspect kept and is never mirrored, so it faces LEFT, towards the player. Transparent
because a body stands in the arena, not in a frame — a placeholder with a ground would draw a box around
every combatant and teach the wrong thing about the real picture.

THE PLAYER'S OWN BODY IS A BODY and is painted here too, into `characters/`: the document declares a slot for
it (`Presentation.Characters[id].Art`) and nothing had ever filled it, because the census only ever counted
cards, relics and enemies. It faces RIGHT — the one thing about it that is not the same as an enemy — because
it stands on the other side of the arena and looks across.
"""
import argparse

from PIL import Image, ImageDraw

from placeholder_art import (
    DIM, FONT_BOLD, FONT_MONO, INK, arguments, block, character_slots, document, enemy_slots, run)

SIZE = (600, 900)

# role -> how much of the plate the figure is allowed to be. The three sizes ARE the difference between a
# filler body, an elite and a boss, so they are far enough apart to be told apart at 150 points.
HEIGHT = {"standard": 0.55, "mimic": 0.64, "elite": 0.74, "boss": 0.88, "hero": 0.66}
HORNED = {"boss"}
# Which way a body looks. Everything in the arena looks at the player; the player looks back.
FACING = {"hero": 1}


def figure(draw, cx, top, height, *, horns, facing=-1):
    """A stick figure. `facing` is -1 for left and +1 for right, and it is the only difference between the
    player's body and an enemy's: the nose, the leading arm, the leading knee and both feet point that way."""
    line = max(4, round(height * 0.018))
    radius = height * 0.12
    head = (cx, top + radius)
    draw.ellipse([head[0] - radius, head[1] - radius, head[0] + radius, head[1] + radius],
                 outline=INK, width=line)
    # The nose is the whole of the facing: two sticks are the same stick until one of them has a front.
    draw.line([(cx + facing * radius, head[1]),
               (cx + facing * (radius + height * 0.055), head[1] + height * 0.012)],
              fill=INK, width=line)

    neck, hip = top + 2 * radius, top + height * 0.60
    draw.line([(cx, neck), (cx, hip)], fill=INK, width=line)

    shoulder = neck + height * 0.07
    draw.line([(cx, shoulder), (cx + facing * height * 0.27, shoulder - height * 0.05)], fill=INK, width=line)
    draw.line([(cx, shoulder), (cx - facing * height * 0.22, shoulder + height * 0.10)], fill=INK, width=line)

    # The two legs are not symmetrical — one strides forward and one trails — so which is which turns with the
    # body rather than being mirrored into a figure walking backwards.
    for knee in (0.17, -0.14):
        ankle = (cx + facing * height * knee, top + height * 0.92)
        draw.line([(cx, hip), ankle], fill=INK, width=line)
        # Both feet point the way the nose does.
        draw.line([ankle, (ankle[0] + facing * height * 0.09, ankle[1])], fill=INK, width=line)

    if horns:
        for side in (-1, 1):
            draw.line([(cx + side * 0.65 * radius, top + 0.35 * radius),
                       (cx + side * 1.55 * radius, top - 0.40 * radius),
                       (cx + side * 1.35 * radius, top - 1.30 * radius)], fill=INK, width=line, joint="curve")


def paint(code, slot):
    name, role = slot
    image = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    # The name goes over the head and the role rides at the foot with the word placeholder, because every line
    # written up there is height the figure does not get: a body is drawn 150 points tall, and what is not
    # figure is barely a few points.
    y = block(draw, name, top=16, width=SIZE[0], path=FONT_BOLD, size=56, fill=INK, margin=22)
    y = block(draw, f"{code}.png", top=y + 6, width=SIZE[0], path=FONT_MONO, size=32, fill=DIM, margin=20)

    # The figure stands on the floor and grows upwards, and it is only ever made SMALLER than its role asks
    # for — by the room the name above it leaves, and by the horns a boss wears over its head.
    horns = role in HORNED
    floor, ceiling = SIZE[1] - 62, y + 12
    height = min(SIZE[1] * HEIGHT.get(role, HEIGHT["standard"]),
                 (floor - ceiling) / (0.92 + (0.16 if horns else 0)))
    figure(draw, SIZE[0] / 2, floor - height * 0.92, height, horns=horns, facing=FACING.get(role, -1))

    block(draw, f"placeholder · {role}", top=SIZE[1] - 50, width=SIZE[0], path=FONT_BOLD, size=34,
          fill=DIM, margin=22)
    return image


if __name__ == "__main__":
    args = arguments(argparse.ArgumentParser(description=__doc__)).parse_args()
    doc = document(args.blueprint)
    enemies, characters = enemy_slots(doc), character_slots(doc)
    # Two calls rather than one merged dictionary, because the folder a slot lives in is part of what the slot
    # IS — and each call is told the other's codes so `--only bureaucrat` is a slot and not a typo.
    run("enemies", enemies, paint, args, siblings=characters)
    if not args.out:
        run("characters", characters, paint, args, siblings=enemies)
