using System;
using Godot;

namespace BnbGodot;

// WHO MADE THE MUSIC, said properly.
//
// Every track in BnB is somebody else's work used under a Creative Commons licence, and all but one of those
// licences REQUIRE attribution — this screen is not a courtesy, it is the condition the music is used on.
// So the data below is not a summary of the integration document; it is what each author's own OpenGameArt
// page asks for, which in three cases is more than a name:
//
//   · Viktor Kraus asks for the sentence, not the credit line ("composed, performed, mixed and mastered by").
//   · Marcelo Fernandez asks for the title, his name, and his site — and his page carries TWO licences, CC BY
//     3.0 in the site's metadata and CC BY 4.0 in his own notice. The stricter reading of his own words wins:
//     4.0 is what he wrote, so 4.0 is what is shown.
//   · Cleyton Kauffman released under CC0, which requires nothing at all, and asked to be credited anyway.
//     He is credited anyway.
//
// `Note` is the other half of the bargain. Every one of these files was CUT — the tracks were written with
// endings and a game needs loops — and CC BY asks that modifications be indicated. Saying "edited to loop"
// under the track is how that is done.
//
// The URLs are real but never shown: a screen full of https://opengameart.org/content/… is unreadable, so
// the word "Source" carries the link and the licence name carries its own.
public partial class CreditsPanel : PanelContainer
{
    private sealed record Entry(string Track, string Composer, string License, string Url, string? Note = null);

    private const string By3 = "CC BY 3.0";
    private const string By4 = "CC BY 4.0";
    private const string Zero = "CC0";

    private static readonly Entry[] Music =
    [
        new("Secret Sanctum", "composed, performed, mixed and mastered by Viktor Kraus", By3,
            "https://opengameart.org/content/secret-sanctum", "edited to loop"),
        new("Waystone Inn", "Enkrez", By4,
            "https://opengameart.org/content/waystone-inn", "edited to loop"),
        new("Fantasy Music - The Savvy Merchant", "HitCtrl", By3,
            "https://opengameart.org/content/fantasy-music-the-savvy-merchant", "edited to loop"),
        new("Victoriana Loop", "Joe Baxter-Webb (BossLevelVGM)", By3,
            "https://opengameart.org/content/victoriana-loop", "edited to loop"),
        new("Dark chamber", "Marcelo Fernandez — marcelofernandezmusic.com", By4,
            "https://opengameart.org/content/dark-chamber", "edited to loop"),
        new("Forest Whisper Theme", "Cleyton Kauffman — soundcloud.com/cleytonkauffman", Zero,
            "https://opengameart.org/content/forest-whisper-theme"),
        new("Fantasy Music - The Eternal Sands", "HitCtrl", By3,
            "https://opengameart.org/content/fantasy-music-the-eternal-sands", "edited to loop"),
        new("Dark Descent", "Matthew Pablo — matthewpablo.com", By3,
            "https://opengameart.org/content/dark-descent", "edited to loop"),
        new("The Desecrated Temple", "Insydnis", By3,
            "https://opengameart.org/content/the-descecrated-temple", "edited to loop"),
        new("Land of the Great Gods", "Alexandr Zhelanov", By4,
            "https://opengameart.org/content/land-of-the-great-gods", "edited to loop"),
    ];

    private static string LicenseUrl(string license) => license switch
    {
        Zero => "https://creativecommons.org/publicdomain/zero/1.0/",
        By4 => "https://creativecommons.org/licenses/by/4.0/",
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
        AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(MoonvineTheme.BgPanelStrong, MoonvineTheme.Accent));

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
            list.AddChild(MusicRow(entry));

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

    private static Control MusicRow(Entry entry)
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
