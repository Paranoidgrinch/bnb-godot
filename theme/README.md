# theme/

## The two fonts

The game is written in two faces, both made by scripts in `theme/font/`:

- **Clerk of Boston** (`clerk-of-boston.ttf`, made by `make_font.py`; needs numpy, shapely and fontTools) is
  the quill hand the CARDS are written in, built for this game from 18th-century clerks' papers.
- **Moonvine Polter** (`polter.ttf`, made by `make_ui_font.py`; needs fontTools and skia-pathops) is
  everything else: screens, buttons, tooltips, the map. It is Short Stack (`src/ShortStack-Regular.ttf`) made
  heavy and narrower, with letters that hop and lean a little. Short Stack is under the SIL Open Font
  License with a Reserved Font Name, so the result has its own name; `OFL-polter.txt` is its licence.

Symbols neither face has (⚔, ♥, arrows) come from Godot's own face, which is set as their fallback.
`--ui-font <file>` sets the UI face for one session from any .ttf/.otf on disk, to try one in the real screens.

To change one of them everywhere at once:

1. Drop a `.ttf` or `.otf` in `theme/font/`, e.g. `theme/font/bnb.ttf`.
2. In `scripts/MoonvineTheme.cs`, set one of the two fields near the top of the file (`FontPath` for the
   UI, `CardFontPath` for the cards):

   ```csharp
   private static readonly string? FontPath = "res://theme/font/bnb.ttf";
   ```

That is the whole change. `MoonvineTheme.Build()` writes the UI face into the Theme's **default font**, and
every `Label`, `Button` and `RichTextLabel` in the game inherits it; the card's writing asks for the card
face. A path that does not resolve prints a warning and falls back to the Godot default rather than crashing the run.

**No screen may load a font of its own.** The moment one does, "change the font later" stops being this
one line and becomes twenty edits scattered across `SessionScreen`, `MapView` and `Boot`.

**A long card name is written smaller, then narrower.** `CardWriting` sets a card's name in the largest size
that fits its rule and, when even the smallest is too wide, squeezes it sideways, the way a clerk crams a long
name into a short space. Nothing is cut off.

## Colours

There is no `.tres` theme file — the whole palette is `scripts/MoonvineTheme.cs`, which is also the only
place in the frontend allowed to name a colour. (The four ember shades in `MapView.RoleColor` are the single
documented exception, and they say why in a comment.)
