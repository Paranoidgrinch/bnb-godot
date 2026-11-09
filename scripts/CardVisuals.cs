using System;
using System.Collections.Generic;
using Godot;

namespace BnbGodot;

// THE CARD. One widget, used by the hand, the card pickers and everything else that has to show a card as a
// card. Since 2026-11-09 a card is a PAPER FORM filled in by a clerk: a sheet of yellowed paper, a form printed
// on it, a cost stamp, the picture in the form's box, a wax seal when the card was improved, and the clerk's
// handwriting over all of it. CardForm knows the pictures and where they go (layout.json, written by the
// generator that drew them); CardWriting is the hand. Every layer is anchored by fraction of the card, so one
// layout serves a hand card, a picker and a thumbnail alike.
//
// ⚠ NOTHING ON A CARD MAY REPORT A MINIMUM SIZE. Godot clamps a Control's size UP to the combined minimum of
// its container children — so the old face, a PanelContainer around a VBox around a wrapping Label, came out
// 24 px wider than the size it was handed and a further 23 px taller whenever its own title happened to wrap
// to two lines. A hand row of cards that are each a different shape, none of them the shape asked for, is
// what "the card changes format when I click it" looks like from the outside. The root here is a PLAIN
// Control (its combined minimum is exactly its CustomMinimumSize, whatever it holds), every field sits in a
// fixed, clipped window anchored by fraction, and `--smoke-format` measures it.
public static class CardVisuals
{
    // +20 %, as asked — and the old 112 × 156 was a 0.718 card. 134 × 190 is the design canvas every card
    // picture is drawn on (the paper is 8 × that), so nothing is ever stretched.
    public const int CardW = 134;
    public const int CardH = 190; // 134 / 0.7048

    // Junk, curses and the cards a fight forces into the hand are served on the office's red notice; every
    // card the player chose is on the office's own form (user, 2026-11-09). The archive's shelf says which.
    private static CardForm.Look LookOf(string id)
    {
        var section = Presented(id)?.Extra.GetValueOrDefault("archiveSection");
        return section is "Junk & curses" or "Fight cards" ? CardForm.Look.Notice : CardForm.Look.Office;
    }

    // The kind of paper, from the card's own tags: Deed, Working, Rite, …
    private static readonly string[] Kinds = ["deed", "working", "rite", "form", "argument", "action", "junk"];

    private static string KindOf(string id)
    {
        var tags = Presented(id)?.Tags ?? [];
        var kind = Kinds.FirstOrDefault(tags.Contains);
        return kind is null ? "" : char.ToUpperInvariant(kind[0]) + kind[1..];
    }

    // What the card said before it was improved, so the clerk can strike the old numbers out.
    private static string? WasOf(string id) => Presented(id.TrimEnd('+'))?.FlavorText;

    private static RogueDeck.Run.EntityPresentation? Presented(string id)
    {
        var host = GameHost.Instance;
        var presentation = host is null ? null : host.Blueprint?.Presentation;
        return presentation?.Cards.GetValueOrDefault(id);
    }

    // ── the art slots ────────────────────────────────────────────────────────────
    // THE CONTRACT'S PATH IS THE PATH ON DISK. Every card and every relic the document ships already names its
    // own picture — `Presentation.Art` reads "cards/levy_stamp.png", written for every one of them by
    // BlueprintAssembler, and 363 cards, 210 relics, 269 bodies and 1 character name 709 distinct files — so
    // nothing is invented here:
    // the file is that path under `res://assets/art/`, and dropping it in is the whole act of filling a slot.
    // The list of all of them, with the design canon's brief beside every relic, is `bnb-content/ART_SLOTS.md`.
    //
    // ⚠ A DROPPED FILE IS INVISIBLE UNTIL GODOT HAS IMPORTED IT. `res://` holds what the importer has seen and
    // nothing else, so a PNG that was merely copied into the folder does not exist for the running game — the
    // editor imports on focus, a headless run never does: `tools/import-art.sh`.
    //
    // Finding nothing is the NORMAL state and has to stay cheap: every miss is remembered too, so an unfilled
    // card asks the filesystem once in a session rather than once per redraw.
    private static readonly Dictionary<string, Texture2D?> ArtCache = [];

    public static Texture2D? CardArt(string id) => Slot("cards", id);

    public static Texture2D? RelicArt(string id) => Slot("relics", id);

    // A BODY IS A PICTURE ON THE SAME TERMS. Every enemy, elite and boss declares `enemies/<id>.png`; until
    // the file is there the arena keeps drawing the stick figure it draws today.
    public static Texture2D? EnemyArt(string id) => Slot("enemies", id);

    // The player's own body. Declared exactly like the others (`Presentation.Characters[id].Art`) and missed by
    // every count until D5, because the census had three folders in it and the document has four.
    public static Texture2D? CharacterArt(string id) => Slot("characters", id);

    // What file a thing asks for, as ART_SLOTS.md names it. The probe prints this, so what a missing picture
    // is called is answered by the game rather than by a rule someone has to remember.
    public static string SlotPath(string kind, string id) =>
        Declared(kind, id) ?? $"{kind}/{id.TrimEnd('+')}.png";

    private static Texture2D? Slot(string kind, string id)
    {
        var key = $"{kind}/{id}";
        if (ArtCache.TryGetValue(key, out var cached))
            return cached;
        // An upgraded card has no picture of its own — "levy_stamp+" is drawn from levy_stamp.png, because an
        // improvement changes what a card DOES and not what it is a picture of. The document says so itself
        // (both ids declare the same path); the trim is what a card that arrives without a declaration falls
        // back to, and it is why no file name in this game contains a "+".
        var found = Load(Declared(kind, id)) ?? Load($"{kind}/{id.TrimEnd('+')}.png");
        ArtCache[key] = found;
        return found;
    }

    private static string? Declared(string kind, string id)
    {
        var host = GameHost.Instance;
        var presentation = host is null ? null : host.Blueprint?.Presentation;
        if (presentation is null)
            return null;
        return kind switch
        {
            "relics" => presentation.Relics.GetValueOrDefault(id)?.Art,
            "enemies" => presentation.Enemies.GetValueOrDefault(id)?.Art,
            "characters" => presentation.Characters.GetValueOrDefault(id)?.Art,
            _ => presentation.Cards.GetValueOrDefault(id)?.Art,
        };
    }

    private static Texture2D? Load(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return null;
        var path = $"res://assets/art/{relative}";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    // ── type that fits ───────────────────────────────────────────────────────────
    private static Font TypeFace => MoonvineTheme.Font ?? ThemeDB.Singleton.FallbackFont;

    // The largest size at which one line of text fits a width. A card's name is a NAME: when it is too long
    // for the band it should get QUIETER, not shorter — and above all it must not change the shape of the
    // card it is printed on, which is what wrapping it used to do.
    private static int FitLine(string text, float width, int max, int min)
    {
        for (var size = max; size >= min; size--)
            if (TypeFace.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= width)
                return size;
        return min;
    }

    public const string WandLine = "\nWand: ";

    // A card's text split into what it does when played and what it does in a wand (null: it has no wand part).
    public static (string Play, string? Wand) SplitWand(string rules)
    {
        var at = rules.IndexOf(WandLine, StringComparison.Ordinal);
        return at < 0 ? (rules, null) : (rules[..at], rules[(at + WandLine.Length)..]);
    }

    // The largest size at which a wrapped paragraph fits a box. What still does not fit is clipped, and the
    // hover has it in full — a plaque that grew to fit its longest card is the bug this whole file exists for.
    // ⚠ MEASURE WITH THE SAME LINE SPACING THE LABEL WILL DRAW WITH. GetMultilineStringSize asks the FONT how
    // tall the block is; a Label then adds the theme's `line_spacing` (3 px by default) between every line,
    // so a paragraph measured to fit exactly loses its last line to the clip. The rules Label sets that
    // constant to 0 and this measures the same block — the two have to agree or the fit is a guess.
    private static int FitBlock(string text, Vector2 box, int max, int min)
    {
        for (var size = max; size >= min; size--)
            if (TypeFace.GetMultilineStringSize(text, HorizontalAlignment.Left, box.X, size).Y <= box.Y)
                return size;
        return min;
    }

    // ── the face ─────────────────────────────────────────────────────────────────

    // Everything the frontend knows about one card, flattened. The face renders; it does not look anything up
    // — the screen that owns the rules decides what is affordable, what is armed and what the hover says.
    public readonly record struct CardFace(
        string Id,
        string Title,
        string Cost,
        string Rules,
        string? Rarity,
        string Tooltip,
        bool Dimmed,
        bool Armed,
        IReadOnlyList<(string Label, string Explanation)>? Marks = null);

    public static Control Face(CardFace card, Action? onClick = null, float scale = 1f)
    {
        var w = Mathf.Round(CardW * scale);
        var h = Mathf.Round(CardH * scale);

        var root = new Control
        {
            CustomMinimumSize = new Vector2(w, h),
            Size = new Vector2(w, h),
            ClipContents = true,
            TooltipText = card.Tooltip,
        };

        // An improved copy fights as "<id>+" and is named "… +", or is drawn as the card with a "+" on its name
        // (the archive). The seal says it now, so one "+" comes off the name; a second improvement keeps its own.
        var sealed_ = card.Id.EndsWith('+') || card.Title.EndsWith('+');
        var title = card.Title.EndsWith('+') ? card.Title[..^1].TrimEnd() : card.Title;
        var look = LookOf(card.Id);

        // ── the sheet ────────────────────────────────────────────────────────────
        // The paper's own ragged cut is the card's edge; a notice is the same paper, tinted rose.
        var paper = Picture(root, CardForm.Paper(card.Id), 0, 0, 1, 1);
        if (look == CardForm.Look.Notice)
            paper.SelfModulate = CardForm.NoticeTint;

        // ── the form, and what sits in it ────────────────────────────────────────
        // The forme sat a little crooked in the press: the form, its title rule and the picture in its box
        // turn and shift TOGETHER, by the card's own amount, so the picture still sits square in its box.
        var (degrees, units) = CardForm.Jitter(card.Id);
        var forme = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            PivotOffset = new Vector2(w, h) / 2,
            RotationDegrees = degrees,
        };
        forme.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        forme.OffsetLeft = forme.OffsetRight = units.X * w / CardW;
        forme.OffsetTop = forme.OffsetBottom = units.Y * w / CardW;
        root.AddChild(forme);
        Picture(forme, CardForm.Form(card.Id, look), 0, 0, 1, 1);
        var strip = CardForm.TitleStripHeight / 2;
        Picture(forme, CardForm.TitleRule(look, sealed_), 0, CardForm.TitleRuleY - strip, 1, CardForm.TitleRuleY + strip);

        var box = CardForm.ArtBox(look);
        var inX = CardForm.ArtInset;
        var inY = CardForm.ArtInset * CardW / CardH;
        if (CardArt(card.Id) is { } art)
        {
            var window = Window(forme, box.Position.X + inX, box.Position.Y + inY, box.End.X - inX, box.End.Y - inY);
            var image = new TextureRect
            {
                Texture = art,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                // Both halves of D1's lesson: the file is imported WITH a mip chain (project.godot's
                // importer defaults) and it is drawn through one. A picture painted at 488x385 lands in a
                // 99x77 box; either half missing and that reduction glitters.
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            image.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            window.AddChild(image);
        }

        // ── the stamps ───────────────────────────────────────────────────────────
        // Square pictures, centred where they were struck: the anchors of a square on a card that is not one.
        void Centred(Texture2D texture, Vector2 centre, float side)
        {
            var half = new Vector2(side / 2, side / 2 * CardW / CardH);
            Picture(root, texture, centre.X - half.X, centre.Y - half.Y, centre.X + half.X, centre.Y + half.Y);
        }
        if (card.Cost.Length > 0)
            Centred(CardForm.Stamp(card.Id, CardForm.StampInk(card.Rarity, look)), CardForm.StampCentre,
                2 * CardForm.StampImageR * CardForm.StampR);
        if (look == CardForm.Look.Notice)
            Centred(CardForm.Served(card.Id), CardForm.ServedCentre, CardForm.ServedSize);

        // ── the clerk's hand ─────────────────────────────────────────────────────
        var writing = new CardWriting
        {
            Cost = card.Cost,
            Title = title,
            TitleInk = CardForm.TitleInk(card.Rarity, look),
            TitleEnd = CardForm.TitleEnd(look, sealed_),
            Kind = KindOf(card.Id),
            Rules = card.Rules,
            Was = sealed_ ? WasOf(card.Id) : null,
            Look = look,
            Seed = card.Id,
        };
        writing.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(writing);

        // The seal goes on last: it is pressed onto the finished card, over the end of the title's rule.
        if (sealed_)
            Centred(CardForm.Seal(card.Id), CardForm.SealCentre, 2 * CardForm.SealImageR * CardForm.SealR);

        // A card you cannot pay for is dimmed as an OBJECT — paper, stamp and picture together — rather than
        // having each of its texts recoloured, because it is the whole card that is out of reach.
        if (card.Dimmed)
        {
            var scrim = new ColorRect { Color = new Color(MoonvineTheme.Bg, 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore };
            scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(scrim);
        }

        // Picked up: a gold edge around the whole card. Gold means "yours to touch" everywhere else in the
        // game, so the card in your hand wears it while it waits for a target.
        if (card.Armed)
        {
            var edge = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            var ring = MoonvineTheme.Panel(new Color(0, 0, 0, 0), MoonvineTheme.AccentLight, (int)Mathf.Round(4 * scale));
            ring.BorderWidthTop = ring.BorderWidthBottom = ring.BorderWidthLeft = ring.BorderWidthRight = 2;
            edge.AddThemeStyleboxOverride("panel", ring);
            edge.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(edge);
        }

        if (onClick is not null)
        {
            var overlay = new Button { Flat = true, Disabled = card.Dimmed, TooltipText = card.Tooltip };
            overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            overlay.Pressed += onClick;
            root.AddChild(overlay);
        }

        // ── what has been DONE to this copy ──────────────────────────────────────
        // A per-instance mark is content's way of making one copy special, and a stamp only the engine can see
        // is a rule nobody was told. The chips go in the picture's top-right corner and ABOVE the click
        // overlay, or the overlay would swallow the hover that explains them — so each one is itself a button
        // that plays the card, and the card behaves the same wherever on it you click. Written in the stamp's
        // red, the one ink on the paper that says "the office did this".
        if (card.Marks is { Count: > 0 } marks)
        {
            var chips = new HBoxContainer
            {
                AnchorLeft = box.Position.X + 0.02f, AnchorTop = box.Position.Y + 0.012f,
                AnchorRight = box.End.X - 0.02f, AnchorBottom = box.Position.Y + 0.075f,
                Alignment = BoxContainer.AlignmentMode.End,
            };
            chips.AddThemeConstantOverride("separation", 3);
            root.AddChild(chips);
            foreach (var (label, explanation) in marks)
            {
                var chip = new Button { Text = label, Flat = true, TooltipText = explanation };
                chip.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
                chip.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
                chip.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
                chip.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
                chip.AddThemeColorOverride("font_color", CardForm.StampRed);
                chip.AddThemeColorOverride("font_hover_color", CardForm.Ink);
                chip.AddThemeFontSizeOverride("font_size", Math.Max(7, (int)Mathf.Round(8 * scale)));
                if (onClick is not null)
                    chip.Pressed += onClick;
                chips.AddChild(chip);
            }
        }

        return root;
    }

    // One of the card's pictures, laid at a fraction of the card. IgnoreSize, or the TextureRect would report
    // the picture's own 1072 × 1520 as a minimum and blow the card up to the size of the file it is drawn from;
    // mipmaps on the draw (the import makes them), or an eightfold shrink samples one pixel in eight.
    private static TextureRect Picture(Control parent, Texture2D texture, float l, float t, float r, float b)
    {
        var picture = new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = l, AnchorTop = t, AnchorRight = r, AnchorBottom = b,
        };
        parent.AddChild(picture);
        return picture;
    }

    // ── the shelf ────────────────────────────────────────────────────────────────

    // A RELIC IS NOT A CARD AND IS NOT A LINE OF TEXT. It is a small framed object on a shelf, and the whole
    // point of a shelf is that you read it by SHAPE before you read a word of it — which is exactly what the
    // visual canon builds its pool frames for (§10.4). A bullet list can hold sixty-nine relics; it cannot be
    // glanced at, and glancing is the only thing anyone actually does with the right-hand edge of the screen.
    //
    // The tile is the same construction as the card face and for the same reason: a plain Control root that
    // reports nothing but the size it was handed, everything inside it anchored and clipped. A shelf of these
    // can therefore be handed to a wrapping container without any one of them arguing about the row height.
    public readonly record struct RelicFace(string Id, string Title, string? Pool, string Tooltip, bool Off);

    // 50 px: five to a row in the 320 px sidebar with room for the gaps and the scrollbar, and still large
    // enough that a painted object reads. Under about 40 the art stops being an object and becomes a smudge.
    public const int TileSize = 50;

    public static Control Tile(RelicFace relic, float size = TileSize)
    {
        var frame = MoonvineTheme.RelicFrame(relic.Pool);
        var root = new Control
        {
            CustomMinimumSize = new Vector2(size, size),
            Size = new Vector2(size, size),
            ClipContents = true,
            TooltipText = relic.Tooltip,
            MouseFilter = Control.MouseFilterEnum.Stop, // the hover IS the relic's rules text; it must be hit-testable
        };

        // The canon asks for clipped corners on the quiet frames — a chamfer, which a StyleBox cannot cut, so
        // the nearest honest thing is a small radius that keeps the square reading as a square.
        var ground = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        var box = MoonvineTheme.Panel(frame.Ground, frame.Edge, radius: 3);
        box.BorderWidthTop = box.BorderWidthBottom = box.BorderWidthLeft = box.BorderWidthRight = frame.Width;
        box.ContentMarginLeft = box.ContentMarginRight = box.ContentMarginTop = box.ContentMarginBottom = 0;
        ground.AddThemeStyleboxOverride("panel", box);
        ground.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(ground);

        // The canon's Boss frame is a DOUBLE one and the Elite's a single line — the whole relationship
        // between the two ranks. At 50 px a second line drawn hard against the first is not a second line,
        // it is a thicker one, so it is set in one tile-width and drawn at half weight: enough to read as
        // two rails at a glance, not enough to close up the window.
        if (frame.Doubled)
        {
            var second = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
            // 0.8, not half: at 0.55 over the purple the second rail came out a dark gap rather than a
            // rail. Where it lands is the point — a dim gold a shade off `GoldDim`, so the Boss frame reads
            // as the Elite frame with a brighter line added around it, which is what the canon says it is.
            var inner = MoonvineTheme.Panel(new Color(0, 0, 0, 0), new Color(frame.Edge, 0.8f), radius: 2);
            inner.BorderWidthTop = inner.BorderWidthBottom = inner.BorderWidthLeft = inner.BorderWidthRight = 1;
            inner.ContentMarginLeft = inner.ContentMarginRight = inner.ContentMarginTop = inner.ContentMarginBottom = 0;
            second.AddThemeStyleboxOverride("panel", inner);
            second.AnchorRight = second.AnchorBottom = 1f;
            second.OffsetLeft = second.OffsetTop = frame.Width + 2f;
            second.OffsetRight = second.OffsetBottom = -(frame.Width + 2f);
            root.AddChild(second);
        }

        var inset = frame.Width + (frame.Doubled ? 5f : 2f);
        var window = new Control
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = inset, OffsetTop = inset, OffsetRight = -inset, OffsetBottom = -inset,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(window);

        if (RelicArt(relic.Id) is { } picture)
        {
            var image = new TextureRect
            {
                Texture = picture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                // The relic briefs are painted square and land here at a fraction of their size — D3's third
                // door into D1's glitter trap, and it needs both halves: the mip chain on the import and the
                // filter on the draw.
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            image.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            window.AddChild(image);
        }
        else
        {
            // AN EMPTY SOCKET STILL HAS TO BE TOLD APART FROM ITS NEIGHBOUR. The slot code is the only name a
            // relic has before it is painted, and `an_underscore_is_a_word_break` — the ids are written as
            // words, so setting them as words is what makes a 46 px square legible instead of one clipped
            // line of ellipsis. The same code names the file that fills the socket (ART_SLOTS.md).
            var code = relic.Id.Replace('_', '\n');
            var label = new Label
            {
                Text = code,
                ClipText = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            label.AddThemeConstantOverride("line_spacing", 0);
            label.AddThemeColorOverride("font_color", new Color(MoonvineTheme.TextMuted, 0.75f));
            // ⚠ A BLOCK THAT FITS BY HEIGHT CAN STILL BE TOO WIDE. FitBlock asks whether the wrapped
            // paragraph is short enough, and wrapping happens between words — so a single word longer than
            // the box (`conservators`, `commissionaire`) never wraps, overflows sideways and is clipped to
            // "onservato". The size the tile uses is therefore the smaller of the two answers: the one that
            // fits the block's HEIGHT and the one that fits the longest WORD's width.
            var longest = code.Split('\n').OrderByDescending(w => w.Length).First();
            var room = new Vector2(size - inset * 2, size - inset * 2);
            label.AddThemeFontSizeOverride("font_size",
                Math.Min(FitBlock(code, room, 9, 4), FitLine(longest, room.X, 9, 4)));
            window.AddChild(label);
        }

        // "(off)" is information, and a relic that has been switched off is still ON THE SHELF — it did not
        // leave, it stopped working, and it comes back. So it is dimmed as an object, exactly the way an
        // unaffordable card is, rather than removed or recoloured.
        if (relic.Off)
        {
            var scrim = new ColorRect { Color = new Color(MoonvineTheme.Bg, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore };
            scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(scrim);
        }

        return root;
    }

    // A fixed, clipped window at a fraction of the card. Anchors and no offsets: the field keeps its place at
    // any card size, and a Control reports no minimum of its own, so nothing inside one can push the card out
    // of shape.
    private static Control Window(Control parent, float l, float t, float r, float b)
    {
        var window = new Control
        {
            AnchorLeft = l, AnchorTop = t, AnchorRight = r, AnchorBottom = b,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(window);
        return window;
    }

    // THE BODY IN ITS COLUMN: the picture if the file is there, and the stick figure if it is not. The size
    // is the COLUMN's, not the picture's — a crowd of four shares the pane and every body in it is drawn
    // narrower, so the picture is fitted into the room the arena gave it and never the other way round.
    // ⚠ `KeepAspectCentered`, not `Scale`: a body drawn to fill a narrow column would be a squashed body, and
    // the one thing a picture of a creature may not do is change shape when a third enemy joins the fight.
    // ⚠ A PICTURE IS NOT MIRRORED. The stick figure is flipped to face its enemy because it is a symmetric
    // scribble; a drawn body that is flipped has its scar, its sash and its writing on the wrong side. The
    // bodies are drawn facing the player (ART_SLOTS.md says so), and `facing` reaches only the placeholder.
    public static Control Body(string? enemyId, Color color, int facing, bool dead, int width, int height)
    {
        var picture = enemyId is null ? null : EnemyArt(enemyId);
        if (picture is null)
        {
            return new StickFigure(color, facing, dead)
            {
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(Math.Min(90, width), height),
            };
        }

        var art = new TextureRect
        {
            Texture = picture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
            CustomMinimumSize = new Vector2(width, height),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // A dead body is dimmed rather than removed — the same thing the stick figure does.
            Modulate = dead ? new Color(1, 1, 1, 0.35f) : Colors.White,
        };
        return art;
    }

    // ── the back ─────────────────────────────────────────────────────────────────
    // THE BACK IS THE SAME FOR EVERY CARD, and still: an ink whirl on the back of a sheet with B&B in its calm
    // eye (assets/cards/back/make_back.py, user 2026-11-09 — no animation). The paper's own ragged cut is its
    // edge, so it gets no chrome at all; the gold this corner owes the rest of the screen is paid by the
    // pile's count, not by a ring around the card.
    public static Control Back(float scale = 1f)
    {
        var root = new Control
        {
            CustomMinimumSize = new Vector2(Mathf.Round(CardW * scale), Mathf.Round(CardH * scale)),
            Size = new Vector2(Mathf.Round(CardW * scale), Mathf.Round(CardH * scale)),
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Picture(root, CardForm.Back, 0, 0, 1, 1);
        return root;
    }
}
