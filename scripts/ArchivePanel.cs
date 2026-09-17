using Godot;

namespace BnbGodot;

// THE ARCHIVE, AS A ROOM YOU CAN WALK BACK INTO.
//
// Six shelves (the player's own order: enemies, elites, cards, relics, bosses, gods), a grid of what stands
// on the chosen one, and a plate for whatever is picked up. Two rules run the whole screen:
//
//   • A THING NOT YET MET IS STILL A SLOT. The grid always shows the full shelf, and what has not been found
//     is a dark square with a question mark — so the archive says how much there is to find, which is the
//     only reason a collection screen is worth opening twice.
//   • EXCEPT THE GODS. Their shelf is not a shelf until the first one is met: the tab itself reads "???" and
//     does not open. Act V is the game's last secret and an empty grid of six squares would spend it.
public partial class ArchivePanel : PanelContainer
{
    private const int TileArt = 104;
    // 104 of picture and the rest for the name. The band holds three lines at 11 pt, which is what "Ask for
    // Expedited Service" and "Enlil, Voice of the Unalterable Decree" need; a shorter band clipped both.
    private const int TileHeight = 156;
    // 420 of the 1160 the panel has inside its margins. What is left — 724, less the scrollbar — is still
    // six tile columns (6 × 104 + 5 × 12 = 684), which is the number the grid is laid out around.
    private const int DetailWidth = 420;

    private readonly Action? _onClose;
    private ArchiveKind _kind = ArchiveKind.Enemies;
    private ArchiveEntry? _picked;
    private bool _confirmingReset;

    private VBoxContainer _body = null!;

    public ArchivePanel(Action? onClose = null) => _onClose = onClose;

    public override void _Ready()
    {
        // 1200 x 690 of the design canvas's 1280 x 720 — as large as this may be and still sit clear of the
        // edges. Every pixel of the height goes to the plate, which is the half that has to hold a god's
        // decree and a boss's nine moves.
        CustomMinimumSize = new Vector2(1200, 690);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));
        Archive.Build(GameHost.Instance.Blueprint);

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);

        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 12);
        margin.AddChild(_body);

        // The first shelf with anything on it, so a player who has only ever met a god does not open the
        // archive onto an empty grid of enemies.
        _kind = Archive.Kinds.FirstOrDefault(k => Archive.SeenCount(k) > 0, ArchiveKind.Enemies);
        if (_kind == ArchiveKind.Gods && Archive.SeenCount(ArchiveKind.Gods) == 0)
            _kind = ArchiveKind.Enemies;
        Draw();
    }

    private void Draw()
    {
        foreach (var child in _body.GetChildren())
            child.QueueFree();

        _body.AddChild(Header());
        _body.AddChild(Tabs());
        _body.AddChild(Shelf());
        _body.AddChild(Footer());
    }

    // ── the head ─────────────────────────────────────────────────────────────────

    private Control Header()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var title = new Label { Text = "Archive" };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        row.AddChild(title);

        var said = new Label
        {
            Text = "Everything a run has shown you, kept.",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        said.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        row.AddChild(said);

        var found = Archive.Kinds.Sum(Archive.SeenCount);
        var all = Archive.Kinds.Sum(k => Archive.Entries(k).Count);
        var tally = new Label { Text = $"{found} of {all} found", VerticalAlignment = VerticalAlignment.Center };
        tally.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        row.AddChild(tally);
        return row;
    }

    private Control Tabs()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        foreach (var kind in Archive.Kinds)
        {
            var seen = Archive.SeenCount(kind);
            // THE GODS ARE NOT NAMED BEFORE THEY ARE MET. Not the count, not the shelf, not the word.
            var secret = kind == ArchiveKind.Gods && seen == 0;
            var button = new Button
            {
                Text = secret ? "???" : $"{Archive.Title(kind)}  {seen}/{Archive.Entries(kind).Count}",
                CustomMinimumSize = new Vector2(0, 36),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Disabled = secret,
                TooltipText = secret
                    ? "Something is filed here that you have not met."
                    : $"{Archive.Title(kind)} — {seen} of {Archive.Entries(kind).Count} met.",
            };
            var chosen = kind == _kind;
            var style = MoonvineTheme.Panel(
                chosen ? MoonvineTheme.BgControl : MoonvineTheme.BgPanel,
                chosen ? MoonvineTheme.Accent : MoonvineTheme.Hairline);
            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("pressed", style);
            button.AddThemeStyleboxOverride("disabled", MoonvineTheme.Panel(MoonvineTheme.BgPanelStrong, MoonvineTheme.Hairline));
            button.AddThemeColorOverride("font_color", chosen ? MoonvineTheme.AccentLight : MoonvineTheme.TextSoft);
            button.AddThemeColorOverride("font_disabled_color", MoonvineTheme.TextMuted);
            var pick = kind;
            button.Pressed += () =>
            {
                _kind = pick;
                _picked = null;
                Draw();
            };
            row.AddChild(button);
        }
        return row;
    }

    // ── the shelf and the plate ──────────────────────────────────────────────────

    private Control Shelf()
    {
        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 16);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        // ⚠ NOT inside a CenterContainer, and the flow container must EXPAND: a flow container handed its own
        // minimum width is one tile wide, which stacks a shelf of a hundred and sixty into a single column.
        var grid = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        foreach (var entry in Archive.Entries(_kind))
            grid.AddChild(Tile(entry));
        scroll.AddChild(grid);
        row.AddChild(scroll);

        var plate = new PanelContainer { CustomMinimumSize = new Vector2(DetailWidth, 0) };
        plate.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(MoonvineTheme.BgPanelStrong, MoonvineTheme.Hairline));
        var inset = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            inset.AddThemeConstantOverride(side, 14);
        plate.AddChild(inset);
        var reading = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        inset.AddChild(reading);
        reading.AddChild(Plate());
        row.AddChild(plate);
        return row;
    }

    private Control Tile(ArchiveEntry entry)
    {
        var met = Archive.Seen(entry);
        var root = new Control
        {
            CustomMinimumSize = new Vector2(TileArt, TileHeight),
            TooltipText = met ? entry.Name : "Not met yet.",
        };

        // ⚠ ANCHORS, NOT `Position`/`Size`. The tile's root is a plain Control (so a flow container is told one
        // size and never argues with a long name), and a plain Control does not lay its children out — a
        // Label handed a Size keeps its own minimum instead, which is how "Blank Death Certificate" came to
        // be printed straight across the neighbouring tile. Anchored to the root, the band IS the tile's
        // width and the name wraps inside it.
        var window = new PanelContainer
        {
            OffsetRight = TileArt,
            OffsetBottom = TileArt,
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var chosen = _picked?.Id == entry.Id && _picked.Kind == entry.Kind;
        window.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(
            met ? MoonvineTheme.BgPanel : MoonvineTheme.BgPanelStrong,
            chosen ? MoonvineTheme.AccentLight : met ? MoonvineTheme.Hairline : new Color(MoonvineTheme.TextMuted, 0.18f),
            radius: 4));
        root.AddChild(window);

        if (met && Art(entry) is { } picture)
        {
            window.AddChild(new TextureRect
            {
                Texture = picture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        else
        {
            // A SLOT WITH NOTHING IN IT STILL HAS TO READ AS A SLOT. A question mark, not an empty square —
            // an empty square is a layout accident and a question mark is a promise.
            var mark = new Label
            {
                Text = met ? "·" : "?",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            mark.AddThemeFontSizeOverride("font_size", 34);
            mark.AddThemeColorOverride("font_color", new Color(MoonvineTheme.TextMuted, 0.55f));
            window.AddChild(mark);
        }

        var name = new Label
        {
            Text = met ? entry.Name : "???",
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetTop = TileArt + 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", 11);
        name.AddThemeColorOverride("font_color", met ? MoonvineTheme.TextSoft : MoonvineTheme.TextMuted);
        root.AddChild(name);

        var overlay = new Button { Flat = true };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.Pressed += () =>
        {
            _picked = entry;
            Draw();
        };
        root.AddChild(overlay);
        return root;
    }

    private Control Plate()
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10);

        if (_picked is not { } entry)
        {
            column.SizeFlagsVertical = SizeFlags.ExpandFill;
            column.Alignment = BoxContainer.AlignmentMode.Center;
            var invite = Muted("Pick something off the shelf.");
            invite.HorizontalAlignment = HorizontalAlignment.Center;
            column.AddChild(invite);
            return column;
        }
        if (!Archive.Seen(entry))
        {
            column.AddChild(Heading("???"));
            column.AddChild(Muted($"You have not met this one. {Where(entry.Kind)}"));
            return column;
        }

        // THE PLATE IS READ TOP DOWN: the thing, its name, WHAT IT DOES, then the numbers, then its moves.
        // The rules came after the fact table at first and a god's decree — the single most important line on
        // its page — sat below four wrapped rows, off the bottom of the plate and behind a scroll.
        column.AddChild(Portrait(entry));
        column.AddChild(Heading(entry.Name));

        if (entry.Prose is { Length: > 0 } prose)
        {
            if (entry.ProseTitle is { Length: > 0 } decree)
            {
                var named = new Label { Text = decree, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                named.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
                column.AddChild(named);
            }
            var rules = new Label
            {
                Text = prose,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                TooltipText = Glossary.Explain(prose),
            };
            rules.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
            column.AddChild(rules);
        }

        // WHAT THE SAME CARD SAYS ONCE IT HAS BEEN IMPROVED — the question a player takes to an archive before
        // spending a campfire on it, and the reason the upgrade is folded in here rather than shelved twice.
        if (entry.Upgraded is { Length: > 0 } better)
        {
            var mark = new Label { Text = "Upgraded" };
            mark.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
            column.AddChild(mark);
            var said = new Label
            {
                Text = better,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                TooltipText = Glossary.Explain(better),
            };
            said.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
            column.AddChild(said);
        }

        // ⚠ THE KEY COLUMN IS TOP-ALIGNED BY HAND. A Label in an HBoxContainer is centred in the row it is
        // given, so beside a value that wrapped onto four lines the word "Opens with" drifted down past its
        // own first line and read as though it belonged to the row underneath.
        foreach (var (label, value) in entry.Facts.Where(f => !string.IsNullOrWhiteSpace(f.Value)))
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            var key = new Label
            {
                Text = label,
                CustomMinimumSize = new Vector2(88, 0),
                SizeFlagsVertical = SizeFlags.Fill,
                VerticalAlignment = VerticalAlignment.Top,
            };
            key.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
            line.AddChild(key);
            var said = new Label
            {
                Text = value,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            said.AddThemeColorOverride("font_color", MoonvineTheme.Text);
            line.AddChild(said);
            column.AddChild(line);
        }

        // WHAT IT DOES TO YOU, in the words it says it in. These are the enemy's own telegraph lines — the
        // ones that stand over its head in a fight — so a player reading the archive is reading the same
        // sentence they will have to answer, and not a paraphrase of it.
        if (entry.Moves.Count > 0)
        {
            column.AddChild(Muted("Moves"));
            foreach (var move in entry.Moves)
            {
                var line = new Label
                {
                    Text = $"· {move}",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    TooltipText = Glossary.Explain(move),
                };
                line.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
                line.AddThemeFontSizeOverride("font_size", 12);
                column.AddChild(line);
            }
        }
        return column;
    }

    // THE THING ITSELF, in the form it is met in: a card is a card face, a relic is the framed object it
    // wears on the shelf, and a body is a body.
    private static Control Portrait(ArchiveEntry entry)
    {
        var holder = new CenterContainer();
        switch (entry.Kind)
        {
            case ArchiveKind.Cards:
                holder.AddChild(CardVisuals.Face(new CardVisuals.CardFace(
                    Id: entry.Id,
                    Title: entry.Name,
                    Cost: entry.Facts.FirstOrDefault(f => f.Label == "Cost").Value ?? "⚡0",
                    Rules: entry.Prose ?? "",
                    Rarity: entry.Frame,
                    Tooltip: Glossary.Explain(entry.Prose),
                    Dimmed: false,
                    Armed: false), scale: 1.3f));
                break;
            case ArchiveKind.Relics:
                holder.AddChild(CardVisuals.Tile(new CardVisuals.RelicFace(
                    Id: entry.Id,
                    Title: entry.Name,
                    Pool: entry.Frame,
                    Tooltip: Glossary.Explain(entry.Prose),
                    Off: false), 120));
                break;
            default:
                holder.AddChild(CardVisuals.Body(
                    entry.Id, MoonvineTheme.Harm, facing: -1, dead: false, width: 180, height: 200));
                break;
        }
        return holder;
    }

    private static Texture2D? Art(ArchiveEntry entry) => entry.Kind switch
    {
        ArchiveKind.Cards => CardVisuals.CardArt(entry.Id),
        ArchiveKind.Relics => CardVisuals.RelicArt(entry.Id),
        _ => CardVisuals.EnemyArt(entry.Id),
    };

    private static string Where(ArchiveKind kind) => kind switch
    {
        ArchiveKind.Enemies => "Ordinary rooms hold them.",
        ArchiveKind.Elites => "The rooms marked ☣.",
        ArchiveKind.Cards => "Won, bought, or forced on you.",
        ArchiveKind.Relics => "Won, bought, or found behind a door.",
        ArchiveKind.Bosses => "At the foot of an act.",
        _ => "The Divine Ledger.",
    };

    // ── the foot ─────────────────────────────────────────────────────────────────

    private Control Footer()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);

        if (_confirmingReset)
        {
            // A RESET IS ASKED TWICE. What it throws away took whole runs to gather and nothing brings it
            // back, so the button that does it is never the button that was under the pointer a moment ago.
            var warn = new Label
            {
                Text = "This erases every discovery and the whole meta profile. It cannot be undone.",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            warn.AddThemeColorOverride("font_color", MoonvineTheme.Harm);
            row.AddChild(warn);

            var erase = new Button { Text = "Erase it all", CustomMinimumSize = new Vector2(150, 40) };
            erase.AddThemeColorOverride("font_color", MoonvineTheme.Harm);
            erase.Pressed += () =>
            {
                Archive.Reset();
                _confirmingReset = false;
                _picked = null;
                Draw();
            };
            row.AddChild(erase);

            var back = new Button { Text = "Keep it", CustomMinimumSize = new Vector2(120, 40) };
            back.Pressed += () =>
            {
                _confirmingReset = false;
                Draw();
            };
            row.AddChild(back);
            return row;
        }

        // ⚠⚠ NOT WHILE A RUN IS LIVE, and this is not squeamishness — it would half-work. `RunPlayback`
        // holds the meta profile it loaded when the run started and writes that object back when the run
        // ends, so a profile deleted mid-run is RECREATED at the finish line with every unlock the reset was
        // meant to throw away. Half a reset is worse than none: the player would be told it happened. The
        // archive is readable from inside a run either way; only the button waits for the title screen.
        if (GameHost.Instance.Play is not null)
        {
            var later = Muted("Finish or leave the run to reset your progress.");
            later.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            later.VerticalAlignment = VerticalAlignment.Center;
            row.AddChild(later);
        }
        else
        {
            var reset = new Button
            {
                Text = "Reset progress",
                Flat = true,
                CustomMinimumSize = new Vector2(0, 40),
                TooltipText = "Throw away every discovery and start the archive empty.",
            };
            reset.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
            reset.Pressed += () =>
            {
                _confirmingReset = true;
                Draw();
            };
            row.AddChild(reset);
        }

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(140, 40) };
        close.Pressed += () => _onClose?.Invoke();
        row.AddChild(close);
        return row;
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", 17);
        label.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        return label;
    }

    private static Label Muted(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        return label;
    }

    // FOR THE PROBE: stand the screen up already holding one thing. The plate is half of what this screen is
    // for and an unopened shelf never shows it, so a picture of the shelf alone would leave the more
    // important half of the work unphotographed.
    internal void Show(ArchiveKind kind, string id)
    {
        _kind = kind;
        _picked = Archive.Entries(kind).FirstOrDefault(e => e.Id == id);
        Draw();
    }

    // ⚠⚠ AND WHAT IS ACTUALLY ON THE PLATE, so a probe can say what it photographed rather than what it
    // meant to. Three of D7's screenshots were the same defeat screen and every assertion about them was
    // true; the only cure is that the screen names what it is showing.
    internal string Photographed => _picked is null
        ? $"{Archive.Title(_kind)} shelf, nothing picked"
        : $"{Archive.Title(_kind)}/{_picked.Id}{(Archive.Seen(_picked) ? "" : " — UNMET, so the plate says ???")}";

    public const string OverlayName = "ArchiveOverlay";

    // ONE ROUTE IN, used by both callers: the title screen's Archive button and the Esc menu's inside a run.
    // Returns the panel so a probe can ask it what it is showing.
    public static ArchivePanel? Open(Control screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (screen.GetNodeOrNull(OverlayName) is not { } already)
        {
            var fresh = Overlay(() => screen.GetNodeOrNull(OverlayName)?.QueueFree());
            fresh.Name = OverlayName;
            screen.AddChild(fresh);
            already = fresh;
        }
        return already.FindChild(nameof(ArchivePanel), recursive: true, owned: false) as ArchivePanel;
    }

    public static Control Overlay(Action onClose)
    {
        var veil = new Control { MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        // NAMED, so the probe can find it. An unnamed Godot node is called whatever the engine decides, and
        // `FindChild("ArchivePanel")` on that answers null — which the probe correctly reported as "the
        // archive did not open at all".
        center.AddChild(new ArchivePanel(onClose) { Name = nameof(ArchivePanel) });
        veil.AddChild(center);
        return veil;
    }
}
