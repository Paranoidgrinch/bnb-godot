using System;
using Godot;

namespace BnbGodot;

// WHO MADE THE MUSIC AND THE SOUNDS, said properly.
//
// Every track in BnB is somebody else's work used under a Creative Commons licence, and all but one of those
// licences REQUIRE attribution — this screen is not a courtesy, it is the condition the music is used on.
// Nine of the ten tracks (2026-11-08) come from free-stock-music.com, each with the licence notice that came with
// it: title, composer and the composer's own site. The tenth, the Act V theme, is from OpenGameArt.
//
// `Note` is the other half of the bargain: CC BY asks that modifications be indicated. The nine were faded in and
// out so they can loop whole; the Act V theme was cut to a loop.
//
// The SOUND EFFECTS follow the same rule: three packs are CC0 and are credited anyway; "Spell 3" is CC BY
// 3.0 (Bart Kelsey) and "Falling body" CC BY-SA 3.0 (remaxim, from a recording by qubodup), and both of those
// REQUIRE the line. The hits were converted from FLAC to Ogg and nothing else was done to any of them.
//
// The UI FONT is Short Stack (Sorkin Type Co) made heavier as Moonvine Polter, under the SIL OFL 1.1, which
// asks for no screen but is credited all the same; its licence ships as theme/font/OFL-polter.txt.
//
// The URLs are real but never shown: a screen full of https://opengameart.org/content/… is unreadable, so
// the word "Source" carries the link and the licence name carries its own.
public partial class CreditsPanel : PanelContainer
{
    private sealed record Entry(string Track, string Composer, string License, string Url, string? Note = null);

    private const string By3 = "CC BY 3.0";
    private const string By4 = "CC BY 4.0";
    private const string Zero = "CC0";
    private const string BySa3 = "CC BY-SA 3.0";
    private const string Ofl = "SIL OFL 1.1";

    private static readonly Entry[] Music =
    [
        new("Unraveling", "Scott Buckley — soundcloud.com/scottbuckley", By4,
            "https://soundcloud.com/scottbuckley", Faded),
        new("The Things That Keep Us Here", "Scott Buckley — soundcloud.com/scottbuckley", By4,
            "https://soundcloud.com/scottbuckley", Faded),
        new("Adventure", "Alexander Nakarada (CreatorChords) — creatorchords.com", By4,
            "https://creatorchords.com", Faded),
        new("Lurking Sloth", "Alexander Nakarada (CreatorChords) — creatorchords.com", By4,
            "https://creatorchords.com", Faded),
        new("The Long Dark", "Scott Buckley — soundcloud.com/scottbuckley", By4,
            "https://soundcloud.com/scottbuckley", Faded),
        new("Fairy Of The Forest", "Alexander Nakarada (CreatorChords) — creatorchords.com", By4,
            "https://creatorchords.com", Faded),
        new("The Legend of Narmer", "WombatNoisesAudio — soundcloud.com/user-734462061", By3,
            "https://soundcloud.com/user-734462061", Faded),
        new("Hearts Of Iron", "Alex-Productions — onsound.eu", By3,
            "https://onsound.eu/", Faded),
        new("Endless Storm", "Makai Symphony — makai-symphony.com", BySa3,
            "http://makai-symphony.com", Faded),
        new("Land of the Great Gods", "Alexandr Zhelanov", By4,
            "https://opengameart.org/content/land-of-the-great-gods", "edited to loop"),
    ];

    private const string Faded = "faded in and out · via free-stock-music.com";

    private static readonly Entry[] Sounds =
    [
        new("37 hits/punches", "Independent.nu (Ljudbank)", Zero,
            "https://opengameart.org/content/37-hitspunches", "converted to Ogg"),
        new("Metal Interactions", "qubodup", Zero,
            "https://opengameart.org/content/metal-interactions"),
        new("Energy Drain", "qubodup", Zero,
            "https://opengameart.org/content/energy-drain"),
        new("Spell 3", "Bart Kelsey", By3,
            "https://opengameart.org/content/spell-3"),
        new("Falling body", "remaxim, based on a recording by qubodup", BySa3,
            "https://opengameart.org/content/falling-body"),
    ];

    private static readonly Entry[] Fonts =
    [
        new("Short Stack", "Sorkin Type Co — sorkintype.com", Ofl,
            "https://fonts.google.com/specimen/Short+Stack", "made heavier and narrower as Moonvine Polter"),
    ];

    private static string LicenseUrl(string license) => license switch
    {
        Ofl => "https://openfontlicense.org/open-font-license-official-text/",
        Zero => "https://creativecommons.org/publicdomain/zero/1.0/",
        By4 => "https://creativecommons.org/licenses/by/4.0/",
        BySa3 => "https://creativecommons.org/licenses/by-sa/3.0/",
        _ => "https://creativecommons.org/licenses/by/3.0/",
    };

    private readonly Action? _onClose;

    public CreditsPanel(Action? onClose = null) => _onClose = onClose;

    public override void _Ready()
    {
        // 600 of the design canvas's 720, which is as tall as this may be and still sit clear of the edges.
        // At 460 the list showed three of ten entries and looked like an accident; the point of a credits
        // screen is that the credits are ON it, not one scroll-flick away.
        CustomMinimumSize = new Vector2(620, 600);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);

        var title = new Label { Text = "Credits", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        column.AddChild(title);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);

        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(list);

        list.AddChild(Heading("Music"));
        foreach (var entry in Music)
            list.AddChild(EntryRow(entry));
        list.AddChild(Heading("Sound effects"));
        foreach (var entry in Sounds)
            list.AddChild(EntryRow(entry));
        list.AddChild(Heading("Font"));
        foreach (var entry in Fonts)
            list.AddChild(EntryRow(entry));

        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(0, 40) };
        close.Pressed += () => _onClose?.Invoke();
        column.AddChild(close);
    }

    private static Control Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
        return label;
    }

    private static Control EntryRow(Entry entry)
    {
        var row = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 2);

        var track = new Label { Text = entry.Track, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        track.AddThemeColorOverride("font_color", MoonvineTheme.Text);
        row.AddChild(track);

        var composer = new Label { Text = entry.Composer, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        composer.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        row.AddChild(composer);

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 6);
        line.AddChild(Link(entry.License, LicenseUrl(entry.License)));
        line.AddChild(Dot());
        line.AddChild(Link("Source", entry.Url));
        if (entry.Note is { } note)
        {
            line.AddChild(Dot());
            var edited = new Label { Text = note };
            edited.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
            line.AddChild(edited);
        }
        row.AddChild(line);
        return row;
    }

    private static Control Dot()
    {
        var dot = new Label { Text = "·" };
        dot.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        return dot;
    }

    // A word that opens a page. Flat, gold, and it says so with the pointer — a link the player cannot tell
    // from a label is not a link.
    private static Control Link(string text, string url)
    {
        var link = new LinkButton
        {
            Text = text,
            Underline = LinkButton.UnderlineMode.OnHover,
            TooltipText = url,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        link.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        link.AddThemeColorOverride("font_hover_color", MoonvineTheme.AccentLight);
        link.Pressed += () => OS.ShellOpen(url);
        return link;
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
        center.AddChild(new CreditsPanel(onClose));
        veil.AddChild(center);
        return veil;
    }
}
