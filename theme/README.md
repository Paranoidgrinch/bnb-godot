# theme/

## Changing the font for the whole game

The whole game is written in one hand, **Clerk of Boston** (`theme/font/clerk-of-boston.ttf`), a quill hand
built for this game from 18th-century clerks' papers. `theme/font/make_font.py` makes it; it needs numpy,
shapely and fontTools. Symbols the hand does not have (⚔, ♥, arrows) come from Godot's own face, which is
set as its fallback.

To change it everywhere at once:

1. Drop a `.ttf` or `.otf` in this folder, e.g. `theme/bnb.ttf`.
2. In `scripts/MoonvineTheme.cs`, set the one field near the top of the file:

   ```csharp
   private static readonly string? FontPath = "res://theme/bnb.ttf";
   ```

That is the whole change. `MoonvineTheme.Build()` writes the face into the Theme's **default font**, and
every `Label`, `Button`, `RichTextLabel` and card face in the game inherits it. A path that does not resolve
prints a warning and falls back to the Godot default rather than crashing the run.

**No screen may load a font of its own.** The moment one does, "change the font later" stops being this
one line and becomes twenty edits scattered across `SessionScreen`, `MapView` and `Boot`.

**A long card name is written smaller, then narrower.** `CardWriting` sets a card's name in the largest size
that fits its rule and, when even the smallest is too wide, squeezes it sideways, the way a clerk crams a long
name into a short space. Nothing is cut off.

## Colours

There is no `.tres` theme file — the whole palette is `scripts/MoonvineTheme.cs`, which is also the only
place in the frontend allowed to name a colour. (The four ember shades in `MapView.RoleColor` are the single
documented exception, and they say why in a comment.)
