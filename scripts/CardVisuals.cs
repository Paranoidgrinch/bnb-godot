using System;
using System.Collections.Generic;
using Godot;

namespace BnbGodot;

// THE CARD. One widget, printed on the master frame, used by the hand, the card pickers and everything else
// that has to show a card as a card. `assets/cards/card-frame.png` is the artwork: an ornate near-black
// border with HOLES cut in it — a title band across the top, a ring at its left end, a big window in the
// middle and a plaque at the foot. The face draws its fields into those holes and lays the frame over the
// top, so the frame and the text can never disagree about where a field is.
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
    // +20 %, as asked — and the old 112 × 156 was a 0.718 card, which the master frame is not. 134 / 190 is
    // the frame's own ratio, so the artwork is never stretched.
    public const int CardW = 134;
    public const int CardH = 190; // 134 / 0.7048

    // ── the frame's geometry, MEASURED off its alpha channel ─────────────────────
    // Every hole in card-frame.png (1053 × 1494) is a field, expressed as a fraction of the card so one
    // layout serves a hand card, a picker and a thumbnail alike.
    private const float ArtL = 0.0437f, ArtT = 0.1419f, ArtR = 0.9554f, ArtB = 0.7209f;
    private const float PlaqueL = 0.0437f, PlaqueT = 0.7416f, PlaqueR = 0.9554f, PlaqueB = 0.9645f;

    // ⚠ THE TITLE BAND IS NOT A RECTANGLE. It is a long lens: pinched at both ends, widest at its foot
    // (x 0.048–0.952 down at y 0.098), with the cost ring biting into its left end, the broom badge into its
    // right, and an ornamental diamond hanging into its middle from above. The title TEXT therefore gets the
    // band's honest middle — anywhere wider and a long name is printed on the frame instead of in it.
    private const float TitleL = 0.11f, TitleT = 0.033f, TitleR = 0.89f, TitleB = 0.115f;

    // The ring at the band's left end. Its hole is small — 6.8 % of the card's width — so the cost is set in
    // the largest digit that fits it and no larger.
    private const float CostCx = 0.0684f, CostCy = 0.0495f;

    private static VideoStream? _stream;
    private static Texture2D? _poster;
    private static Texture2D? _frame;

    private static VideoStream Stream => _stream ??= GD.Load<VideoStream>("res://assets/cards/card-back.ogv");
    private static Texture2D Poster => _poster ??= GD.Load<Texture2D>("res://assets/cards/card-back.png");
    private static Texture2D Frame => _frame ??= GD.Load<Texture2D>("res://assets/cards/card-frame.png");

    // ── the art slots ────────────────────────────────────────────────────────────
    // THE CONTRACT'S PATH IS THE PATH ON DISK. Every card and every relic the document ships already names its
    // own picture — `Presentation.Art` reads "cards/levy_stamp.png", written for every one of them by
    // BlueprintAssembler, and 413 cards + 210 relics name 464 distinct files — so nothing is invented here:
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
        return kind == "relics"
            ? presentation.Relics.GetValueOrDefault(id)?.Art
            : presentation.Cards.GetValueOrDefault(id)?.Art;
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

        // ⚠ THE GROUND IS ROUNDED TO THE FRAME'S OWN CORNER. The card ground is lit (it is what makes the
        // black frame visible at all), so a square of it behind a frame with 7.4 %-radius corners shows as
        // four bright nubs poking out past the artwork. Everywhere else the frame is opaque and covers the
        // ground itself, so this is the only place the shape has to be repeated.
        var ground = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        ground.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = MoonvineTheme.CardGround,
            CornerRadiusTopLeft = (int)Mathf.Round(0.074f * w),
            CornerRadiusTopRight = (int)Mathf.Round(0.074f * w),
            CornerRadiusBottomLeft = (int)Mathf.Round(0.074f * w),
            CornerRadiusBottomRight = (int)Mathf.Round(0.074f * w),
        });
        ground.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(ground);

        // ── the picture, or the socket where one goes ────────────────────────────
        var art = Window(root, ArtL, ArtT, ArtR, ArtB);
        // A shade under the rest of the card — a recess waiting for a picture — but still lit enough that the
        // frame's inner rail has something to be a silhouette against.
        var socket = new ColorRect { Color = MoonvineTheme.CardGround.Darkened(0.3f), MouseFilter = Control.MouseFilterEnum.Ignore };
        socket.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        art.AddChild(socket);
        if (CardArt(card.Id) is { } picture)
        {
            var image = new TextureRect
            {
                Texture = picture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                // Both halves of D1's lesson: the file is imported WITH a mip chain (project.godot's
                // importer defaults) and it is drawn through one. A picture painted at 488x440 lands in a
                // 122x110 window; either half missing and that reduction glitters.
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            image.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            art.AddChild(image);
        }
        else
        {
            var code = new Label
            {
                Text = card.Id,
                ClipText = true,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            code.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            code.AddThemeColorOverride("font_color", new Color(MoonvineTheme.TextMuted, 0.55f));
            code.AddThemeFontSizeOverride("font_size", Math.Max(7, (int)Mathf.Round(8 * scale)));
            art.AddChild(code);
        }

        // ── the name, in the band ────────────────────────────────────────────────
        var titleWindow = Window(root, TitleL, TitleT, TitleR, TitleB);
        var title = new Label
        {
            Text = card.Title,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        title.AddThemeColorOverride("font_color", card.Dimmed ? MoonvineTheme.TextMuted : MoonvineTheme.RarityColor(card.Rarity));
        title.AddThemeFontSizeOverride("font_size",
            FitLine(card.Title, (TitleR - TitleL) * w, (int)Mathf.Round(12 * scale), Math.Max(6, (int)Mathf.Round(8 * scale))));
        titleWindow.AddChild(title);

        // ── the cost, in the ring ────────────────────────────────────────────────
        // A box anchored from the card's edge to twice the ring's centre is a box CENTRED on the ring, which
        // an anchor cannot express directly (they are clamped to 0–1 and the ring sits near the corner).
        var costWindow = Window(root, 0f, 0f, CostCx * 2f, CostCy * 2f);
        var cost = new Label
        {
            Text = card.Cost,
            ClipText = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        cost.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        cost.AddThemeColorOverride("font_color", card.Dimmed ? MoonvineTheme.TextMuted : MoonvineTheme.Signal);
        cost.AddThemeFontSizeOverride("font_size",
            FitLine(card.Cost, CostCx * 2f * w * 0.9f, (int)Mathf.Round(12 * scale), Math.Max(6, (int)Mathf.Round(7 * scale))));
        costWindow.AddChild(cost);

        // ── the rules, on the plaque ─────────────────────────────────────────────
        var plaque = Window(root, PlaqueL + 0.035f, PlaqueT + 0.014f, PlaqueR - 0.035f, PlaqueB - 0.012f);
        var box = new Vector2((PlaqueR - PlaqueL - 0.07f) * w, (PlaqueB - PlaqueT - 0.026f) * h);
        var rules = new Label
        {
            Text = card.Rules,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
        };
        rules.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        rules.AddThemeConstantOverride("line_spacing", 0);
        rules.AddThemeColorOverride("font_color", card.Dimmed ? MoonvineTheme.TextMuted : MoonvineTheme.TextSoft);
        rules.AddThemeFontSizeOverride("font_size",
            FitBlock(card.Rules, box, (int)Mathf.Round(11 * scale), Math.Max(6, (int)Mathf.Round(7 * scale))));
        plaque.AddChild(rules);

        // ── the frame itself, over all of it ─────────────────────────────────────
        // IgnoreSize, or the TextureRect would report the artwork's own 1053 × 1494 as a minimum and blow the
        // card up to the size of the file it is drawn from.
        var frame = new TextureRect
        {
            Texture = Frame,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // The artwork is 1053 px wide and a card is 134: without mipmaps that eightfold shrink samples
            // one pixel in eight and the filigree comes out as glitter. (The matching mipmaps/generate=true
            // is in card-frame.png.import — the flag has to be set on BOTH sides or there is nothing to
            // sample.)
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
        };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(frame);

        // A card you cannot pay for is dimmed as an OBJECT — frame, plaque and picture together — rather than
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
            var ring = MoonvineTheme.Panel(new Color(0, 0, 0, 0), MoonvineTheme.AccentLight, (int)Mathf.Round(10 * scale));
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
        // that plays the card, and the card behaves the same wherever on it you click.
        if (card.Marks is { Count: > 0 } marks)
        {
            var strip = new HBoxContainer
            {
                AnchorLeft = ArtL + 0.02f, AnchorTop = ArtT + 0.012f, AnchorRight = ArtR - 0.02f, AnchorBottom = ArtT + 0.075f,
                Alignment = BoxContainer.AlignmentMode.End,
            };
            strip.AddThemeConstantOverride("separation", 3);
            root.AddChild(strip);
            foreach (var (label, explanation) in marks)
            {
                var chip = new Button { Text = label, Flat = true, TooltipText = explanation };
                chip.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
                chip.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
                chip.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
                chip.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
                chip.AddThemeColorOverride("font_color", MoonvineTheme.Signal);
                chip.AddThemeColorOverride("font_hover_color", MoonvineTheme.AccentLight);
                chip.AddThemeFontSizeOverride("font_size", Math.Max(7, (int)Mathf.Round(8 * scale)));
                if (onClick is not null)
                    chip.Pressed += onClick;
                strip.AddChild(chip);
            }
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

    // ── the back ─────────────────────────────────────────────────────────────────
    // THE BACK IS ALREADY A WHOLE CARD. It arrives ornamented — corner medallions, a silver-and-violet
    // border, and the card's rounded silhouette cut into the picture with plain black outside the round —
    // so the old wrapper, a bordered panel of card ground, would have drawn a second frame around the first
    // and lit four nubs of ground in the gaps at the corners: exactly the bug the front had in D1, arriving
    // from the other direction. The back therefore gets no chrome at all. The picture IS the card, edge to
    // edge, and the gold this corner owes the rest of the screen is paid by the pile's count, not by a ring
    // around a painting.
    //
    // ⚠⚠ A VIDEO TEXTURE CANNOT BE MIPMAPPED. Its frame is rebuilt every tick, so there is no chain to
    // build and no `LinearWithMipmaps` to reach for — D1's glitter trap with the exit welded shut. The only
    // remaining lever is the encode, so each rendition is cut to the size it is actually drawn at: the clip
    // at 134x190, where 1:1 means it is never resampled at all, and the poster at 2x WITH mipmaps, because
    // a still can carry the chain the clip cannot. ⚠ Both are cut from `BaB-cardback-master.mp4`, which is
    // NOT in this repo (34 MB); if CardW/CardH ever move, re-cut both — see VISUAL_OVERHAUL_PLAN.md D2 for
    // the two ffmpeg lines.
    //
    // animated=true plays the looping clip (Godot decodes Theora on the CPU, so only a FEW should ever
    // animate at once — the deck's top card); everywhere else uses the still poster.
    public static Control Back(bool animated)
    {
        var root = new Control
        {
            CustomMinimumSize = new Vector2(CardW, CardH),
            Size = new Vector2(CardW, CardH),
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        Control picture;
        if (animated)
        {
            // No Play() here: Autoplay already starts the clip the moment the player enters the tree, and
            // calling it before that only prints "Condition !is_inside_tree() is true" — once per card back,
            // which under a headless probe is often enough to bury everything else the run has to say.
            picture = new VideoStreamPlayer
            {
                Stream = Stream,
                Autoplay = true,
                Loop = true,
                Expand = true,
            };
        }
        else
        {
            picture = new TextureRect
            {
                Texture = Poster,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
            };
        }

        picture.MouseFilter = Control.MouseFilterEnum.Ignore;
        picture.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(picture);
        return root;
    }
}
