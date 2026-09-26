using System.Text.RegularExpressions;
using Godot;
using RogueDeck.Run;

namespace BnbGodot;

// THE COMPENDIUM — the game's wiki, from the title screen and from Esc inside a run (playtest 2026-09-26: "eine
// art wiki, wo jeder effekt und status im spiel erklaert ist in einfacher sprache mit beispiel").
//
// It is BUILT FROM THE DOCUMENT, like the archive, so nobody maintains a list:
//   • the basics (Energy, Block, Exhaust, a Queue card …) and the plain words and example of every common status
//     come from `Presentation.Game.Extra` (bnb-content's Compendium.cs);
//   • every status a card or an enemy's telegraph NAMES is listed too, with the rule the document gives it —
//     so a boss's own mechanic is findable here the moment it is in the game, words or no words;
//   • each entry says which cards use the term, because "where does this come from" is the next question.
// Unlike the archive it is not gated on having met anything: a rule is not a spoiler, and the player who needs
// it most is the one who has not met it yet.
public partial class CompendiumPanel : PanelContainer
{
    public const string OverlayName = "CompendiumOverlay";

    private sealed record Entry(
        string Id, string Name, bool Concept, string? Rule, string? Plain, string? Example, string? Polarity,
        IReadOnlyList<string> Cards);

    private readonly Action? _onClose;
    private List<Entry> _entries = [];
    private VBoxContainer _list = null!;
    private VBoxContainer _detail = null!;
    private LineEdit _search = null!;
    private Entry? _picked;

    public CompendiumPanel(Action? onClose = null) => _onClose = onClose;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(1100, 640);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));
        _entries = Build(GameHost.Instance.Blueprint);

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        margin.AddChild(body);

        var head = new HBoxContainer();
        var title = new Label { Text = "Compendium", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 24);
        head.AddChild(title);
        _search = new LineEdit { PlaceholderText = "Search…", CustomMinimumSize = new Vector2(260, 0) };
        _search.TextChanged += _ => Fill();
        head.AddChild(_search);
        var close = new Button { Text = "Close" };
        close.Pressed += () => _onClose?.Invoke();
        head.AddChild(close);
        body.AddChild(head);

        var split = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 18);
        body.AddChild(split);

        var listScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(300, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 2);
        listScroll.AddChild(_list);
        split.AddChild(listScroll);

        var detailScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 12);
        detailScroll.AddChild(_detail);
        split.AddChild(detailScroll);

        _picked = _entries.FirstOrDefault();
        Fill();
        _search.GrabFocus();
    }

    // How many entries there are, and how many carry plain words — what `--smoke-compendium` reports.
    public (int Entries, int Explained, int Concepts) Census() =>
        (_entries.Count, _entries.Count(e => e.Plain is not null), _entries.Count(e => e.Concept));

    public void Pick(string id)
    {
        _picked = _entries.FirstOrDefault(e => e.Id == id) ?? _picked;
        Fill();
    }

    private void Fill()
    {
        foreach (var child in _list.GetChildren())
            child.QueueFree();
        var query = _search.Text.Trim();
        var shown = _entries.Where(e => query.Length == 0
            || e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (e.Rule ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
            || (e.Plain ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var (heading, group) in new[]
                 {
                     ("The basics", shown.Where(e => e.Concept).ToList()),
                     ("Statuses and effects", shown.Where(e => !e.Concept).ToList()),
                 })
        {
            if (group.Count == 0)
                continue;
            var label = new Label { Text = heading };
            label.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
            label.AddThemeFontSizeOverride("font_size", 13);
            _list.AddChild(label);
            foreach (var entry in group)
            {
                var button = new Button
                {
                    Text = entry.Name,
                    Flat = entry != _picked,
                    Alignment = HorizontalAlignment.Left,
                };
                button.AddThemeColorOverride("font_color", Colour(entry));
                button.Pressed += () => { _picked = entry; Fill(); };
                _list.AddChild(button);
            }
        }
        if (_picked is not null && !shown.Contains(_picked) && shown.Count > 0)
            _picked = shown[0];
        Describe(_picked);
    }

    private void Describe(Entry? entry)
    {
        foreach (var child in _detail.GetChildren())
            child.QueueFree();
        if (entry is null)
            return;

        var name = new Label { Text = entry.Name };
        name.AddThemeFontSizeOverride("font_size", 26);
        name.AddThemeColorOverride("font_color", Colour(entry));
        _detail.AddChild(name);
        if (entry.Polarity is { } polarity)
            _detail.AddChild(Muted(polarity switch
            {
                "Buff" => "Helps whoever carries it.",
                "Debuff" => "Hurts whoever carries it.",
                _ => "Neither good nor bad by itself — it counts something.",
            }));

        if (entry.Plain is { } plain)
            _detail.AddChild(Section("In plain words", plain, MoonvineTheme.Text, 18));
        if (entry.Example is { } example)
            _detail.AddChild(Section("Example", example, MoonvineTheme.AccentLight, 16));
        if (entry.Rule is { } rule)
            _detail.AddChild(Section("The exact rule", rule, MoonvineTheme.TextSoft, 15));
        if (entry.Cards.Count > 0)
            _detail.AddChild(Section("Cards that use it",
                string.Join(", ", entry.Cards.Take(14)) + (entry.Cards.Count > 14 ? $" and {entry.Cards.Count - 14} more" : ""),
                MoonvineTheme.TextMuted, 14));
    }

    private static Control Section(string heading, string text, Color colour, int size)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        var head = new Label { Text = heading };
        head.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        head.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(head);
        var body = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        body.AddThemeColorOverride("font_color", colour);
        body.AddThemeFontSizeOverride("font_size", size);
        box.AddChild(body);
        return box;
    }

    private static Label Muted(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        return label;
    }

    private static Color Colour(Entry entry) => entry.Polarity switch
    {
        "Buff" => MoonvineTheme.Accent,
        "Debuff" => MoonvineTheme.Harm,
        _ => entry.Concept ? MoonvineTheme.Steel : MoonvineTheme.TextSoft,
    };

    private static List<Entry> Build(RunBlueprint blueprint)
    {
        var extra = blueprint.Presentation.Game?.Extra ?? new Dictionary<string, string>();
        string? Words(string key) => extra.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

        var cards = blueprint.Presentation.Cards
            .Where(c => !c.Key.EndsWith('+'))
            .Select(c => (Name: blueprint.Cards.FirstOrDefault(d => d.Id == c.Key)?.NameKey ?? c.Key,
                Text: c.Value.FlavorText ?? ""))
            .ToList();
        IReadOnlyList<string> Using(string name) => [.. cards
            .Where(c => Regex.IsMatch(c.Text, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase))
            .Select(c => c.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal)];

        var entries = new List<Entry>();
        // The basics first, in the order the content wrote them — that order is a lesson, not an index.
        foreach (var key in extra.Keys.Where(k => k.StartsWith("compendium.concept:", StringComparison.Ordinal)))
        {
            var id = key["compendium.concept:".Length..];
            var name = Words($"compendium:{id}") ?? id;
            entries.Add(new Entry(id, name, true, null, Words($"compendium.plain:{id}"),
                Words($"compendium.example:{id}"), null, Using(name)));
        }

        // Every status that has words here, and every status a card's text or an enemy's telegraph names.
        var telegraphs = string.Join(" ", blueprint.EnemyActions.Select(a => a.Intent?.Label ?? ""));
        var cardText = string.Join(" ", cards.Select(c => c.Text));
        var statuses = new List<Entry>();
        foreach (var status in blueprint.Statuses)
        {
            var name = string.IsNullOrWhiteSpace(status.NameKey) ? null : status.NameKey.Trim();
            if (name is null || statuses.Any(e => e.Name == name))
                continue;
            var explained = Words($"compendium:{status.Id}") is not null;
            var named = Regex.IsMatch(cardText, $@"\b{Regex.Escape(name)}\b")
                || Regex.IsMatch(telegraphs, $@"\b{Regex.Escape(name)} \+\d");
            if (!explained && !named)
                continue;
            statuses.Add(new Entry(status.Id, Words($"compendium:{status.Id}") ?? name, false,
                string.IsNullOrWhiteSpace(status.DescriptionKey) ? null : status.DescriptionKey,
                Words($"compendium.plain:{status.Id}"), Words($"compendium.example:{status.Id}"),
                status.Polarity.ToString(), Using(name)));
        }
        entries.AddRange(statuses.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase));
        return entries;
    }

    public static CompendiumPanel? Open(Control screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (screen.GetNodeOrNull(OverlayName) is not { } already)
        {
            var veil = new Control { Name = OverlayName, MouseFilter = MouseFilterEnum.Stop };
            veil.SetAnchorsPreset(LayoutPreset.FullRect);
            var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
            dim.SetAnchorsPreset(LayoutPreset.FullRect);
            veil.AddChild(dim);
            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            center.AddChild(new CompendiumPanel(() => screen.GetNodeOrNull(OverlayName)?.QueueFree())
            {
                Name = nameof(CompendiumPanel),
            });
            veil.AddChild(center);
            screen.AddChild(veil);
            already = veil;
        }
        return already.FindChild(nameof(CompendiumPanel), recursive: true, owned: false) as CompendiumPanel;
    }
}
