# theme/

## Changing the font for the whole game

The game runs on Godot's built-in face. To change it everywhere at once:

1. Drop a `.ttf` or `.otf` in this folder, e.g. `theme/bnb.ttf`.
2. In `scripts/MoonvineTheme.cs`, set the one field near the top of the file:

   ```csharp
   private static readonly string? FontPath = "res://theme/bnb.ttf";
   ```

That is the whole change. `MoonvineTheme.Build()` writes the face into the Theme's **default font**, and
every `Label`, `Button`, `RichTextLabel` and card face in the game inherits it. A path that does not resolve
prints a warning and falls back to the Godot default rather than crashing the run.

⚠ **No screen may load a font of its own.** The moment one does, "change the font later" stops being this
one line and becomes twenty edits scattered across `SessionScreen`, `MapView` and `Boot`.

**The card face is the strongest argument for changing it.** `CardVisuals` sizes a card's name to the frame's
title band and shrinks it until it fits, but the longest names in the game (*Break the Great Seal of
Execution*, 33 characters) still reach the ellipsis at the floor size in a 105 px band. A condensed face is
the one edit that buys them back — and it is this edit.

## Colours

There is no `.tres` theme file — the whole palette is `scripts/MoonvineTheme.cs`, which is also the only
place in the frontend allowed to name a colour. (The four ember shades in `MapView.RoleColor` are the single
documented exception, and they say why in a comment.)
