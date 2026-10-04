using Godot;

namespace BnbGodot;

// THE RECIPE BOOK, AS A BOOK (plan G4): three sections, the canon's own — Everyday Brewing (the family recipes,
// open from the start), Things That Worked Once (the Hidden Recipes found), Notes in the Margin (a clue for each
// one still hidden, never its cards). Opened from the title screen beside the archive and from the cauldron.
public partial class RecipeBookPanel : PanelContainer
{
    public const string OverlayName = "RecipeBookOverlay";

    public enum Section { Everyday, Worked, Margin }

    private readonly Action? _onClose;
    private Section _section = Section.Everyday;
    private VBoxContainer _tabs = null!;
    private VBoxContainer _page = null!;

    public RecipeBookPanel(Action? onClose = null) => _onClose = onClose;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(1000, 620);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        margin.AddChild(body);

        var head = new HBoxContainer();
        var title = new Label { Text = "Recipe Book", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 24);
        head.AddChild(title);
        var close = new Button { Text = "Close" };
        close.Pressed += () => _onClose?.Invoke();
        head.AddChild(close);
        body.AddChild(head);

        _tabs = new VBoxContainer();
        var tabRow = new HBoxContainer();
        tabRow.AddThemeConstantOverride("separation", 8);
        foreach (var section in Enum.GetValues<Section>())
        {
            var chosen = section;
            var tab = new Button { Text = Title(section), Name = $"Tab_{section}", ToggleMode = true };
            tab.Pressed += () => Show(chosen);
            tabRow.AddChild(tab);
        }
        _tabs.AddChild(tabRow);
        body.AddChild(_tabs);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _page.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_page);
        body.AddChild(scroll);

        Show(Section.Everyday);
    }

    private static string Title(Section section)
    {
        var blueprint = GameHost.Instance.Blueprint;
        return section switch
        {
            Section.Everyday => "Everyday Brewing",
            Section.Worked => $"Things That Worked Once  {RecipeBook.DiscoveredCount}/{RecipeBook.Hidden(blueprint).Count}",
            _ => "Notes in the Margin",
        };
    }

    // What is on the open page — for the probe, which says what it photographed.
    internal string Showing => Title(_section);

    public void Show(Section section)
    {
        _section = section;
        foreach (var tab in _tabs.FindChildren("Tab_*", "Button", recursive: true, owned: false).OfType<Button>())
            tab.ButtonPressed = tab.Name == $"Tab_{section}";
        foreach (var child in _page.GetChildren())
            child.QueueFree();

        var blueprint = GameHost.Instance.Blueprint;
        switch (section)
        {
            case Section.Everyday:
                Note("Three of one family make a concentrated brew; any other mix gives what each ingredient gives.");
                foreach (var recipe in RecipeBook.Family(blueprint))
                    Row(recipe.Name, recipe.Families, recipe.Effect);
                break;

            case Section.Worked:
                var found = RecipeBook.Hidden(blueprint).Where(r => RecipeBook.Discovered(r.Number)).ToList();
                if (found.Count == 0)
                    Note("Nothing yet. Some three cards, brewed together, make something the book does not know.");
                var names = blueprint.Cards.ToDictionary(c => c.Id, c => c.NameKey, StringComparer.Ordinal);
                foreach (var recipe in found)
                    Row(recipe.Name,
                        string.Join(" + ", recipe.Cards.Select(c => c == "dregs" ? "Dregs" : names.GetValueOrDefault(c, c))),
                        recipe.Effect);
                break;

            default:
                var hidden = RecipeBook.Hidden(blueprint).Where(r => !RecipeBook.Discovered(r.Number)).ToList();
                if (hidden.Count == 0)
                    Note("Every margin is filled in: the book knows all it has to tell.");
                foreach (var recipe in hidden)
                    Row("???", $"Note {recipe.Number}", recipe.Clue);
                break;
        }
    }

    private void Note(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        _page.AddChild(label);
    }

    private void Row(string name, string what, string effect)
    {
        var plate = new PanelContainer();
        plate.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(MoonvineTheme.BgPanelStrong, MoonvineTheme.Hairline, radius: 6));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        plate.AddChild(box);
        var line = new HBoxContainer();
        var title = new Label { Text = name, CustomMinimumSize = new Vector2(240, 0) };
        title.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
        line.AddChild(title);
        var cards = new Label { Text = what, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cards.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        line.AddChild(cards);
        box.AddChild(line);
        var said = new Label { Text = effect, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(said);
        _page.AddChild(plate);
    }

    public static RecipeBookPanel? Open(Control screen)
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
            center.AddChild(new RecipeBookPanel(() => screen.GetNodeOrNull(OverlayName)?.QueueFree())
            {
                Name = nameof(RecipeBookPanel),
            });
            veil.AddChild(center);
            screen.AddChild(veil);
            already = veil;
        }
        return already.FindChild(nameof(RecipeBookPanel), recursive: true, owned: false) as RecipeBookPanel;
    }
}
