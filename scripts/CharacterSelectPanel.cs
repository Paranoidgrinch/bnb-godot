using Godot;
using RogueDeck.Run;

namespace BnbGodot;

// WHO WILL YOU BE (user, 2026-10-23): "New run" opens this screen instead of the title carrying the roster. Every
// character is shown, in the order they are earned; a locked one is greyed and says how it is unlocked
// (CharacterUnlocks). The first time a character is taken, the game offers its five-minute tutorial.
public partial class CharacterSelectPanel : PanelContainer
{
    public const string OverlayName = "CharacterSelectOverlay";
    private const string QuestionName = "TutorialQuestion";

    // The order they are earned in: the Lieutenant first, the Bureaucrat last.
    private static readonly string[] Order = ["lieutenant", "sorcerers_apprentice", "hedge_witch", "bureaucrat"];

    private readonly GameHost _host;
    private readonly Action<string> _onRun;
    private readonly Action<string> _onTutorial;
    private readonly Action _onClose;
    private string? _selected;
    private HBoxContainer _cards = null!;
    private Button _begin = null!;
    private Button _tutorial = null!;

    public CharacterSelectPanel(GameHost host, Action<string> onRun, Action<string> onTutorial, Action onClose)
    {
        _host = host;
        _onRun = onRun;
        _onTutorial = onTutorial;
        _onClose = onClose;
    }

    public static IReadOnlyList<RunCharacter> Roster(RunBlueprint blueprint) =>
        [.. blueprint.Characters.OrderBy(c => Array.IndexOf(Order, c.Id) is var i and >= 0 ? i : Order.Length)];

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);

        var title = new Label { Text = "Choose your character", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        column.AddChild(title);

        _cards = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _cards.AddThemeConstantOverride("separation", 12);
        column.AddChild(_cards);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        var back = new Button { Text = "◂ Back", CustomMinimumSize = new Vector2(140, 40) };
        back.Pressed += _onClose;
        buttons.AddChild(back);
        _tutorial = new Button { Name = "CharacterTutorial", Text = "Tutorial", CustomMinimumSize = new Vector2(140, 40),
            TooltipText = "The five-minute lesson for this character." };
        _tutorial.Pressed += () => { if (_selected is { } id) Teach(id); };
        buttons.AddChild(_tutorial);
        _begin = new Button { Name = "BeginRun", Text = "Start run ▸", CustomMinimumSize = new Vector2(180, 40) };
        _begin.Pressed += () => { if (_selected is { } id) Begin(id); };
        buttons.AddChild(_begin);
        column.AddChild(buttons);

        _selected = Roster(_host.Blueprint).FirstOrDefault(c => IsOpen(c))?.Id;
        Refresh();
    }

    private bool IsOpen(RunCharacter character) => _host.AvailableCharacters.Any(c => c.Id == character.Id);

    private void Refresh()
    {
        foreach (var child in _cards.GetChildren())
            child.QueueFree();
        foreach (var character in Roster(_host.Blueprint))
            _cards.AddChild(Card(character, IsOpen(character)));
        _begin.Disabled = _selected is null;
        _tutorial.Disabled = _selected is null || !_host.Blueprint.HasTutorialFor(_selected);
    }

    private Control Card(RunCharacter character, bool open)
    {
        var presentation = _host.Blueprint.Presentation.Characters.GetValueOrDefault(character.Id);
        var selected = _selected == character.Id;
        var panel = new PanelContainer { Name = $"Character_{character.Id}", CustomMinimumSize = new Vector2(272, 350) };
        panel.AddThemeStyleboxOverride("panel", open
            ? MoonvineTheme.WoodPanel(rim: selected)
            : MoonvineTheme.Panel(MoonvineTheme.BgPanelStrong, new Color(MoonvineTheme.TextMuted, 0.2f)));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        if (CardVisuals.CharacterArt(character.Id) is { } portrait)
            column.AddChild(new TextureRect
            {
                Texture = portrait,
                CustomMinimumSize = new Vector2(0, 170),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
                MouseFilter = MouseFilterEnum.Ignore,
                Modulate = open ? Colors.White : new Color(1, 1, 1, 0.3f),
            });
        var name = new Label { Text = character.Start.HeroName ?? character.Id, HorizontalAlignment = HorizontalAlignment.Center };
        name.AddThemeFontSizeOverride("font_size", 18);
        name.AddThemeColorOverride("font_color", open ? MoonvineTheme.Text : MoonvineTheme.TextMuted);
        column.AddChild(name);
        column.AddChild(Muted(open ? $"{character.Start.MaxHealth} HP" : "Locked"));
        var words = new Label
        {
            Text = open ? presentation?.FlavorText ?? "" : CharacterUnlocks.Hint(_host.Blueprint, character.Id),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(240, 0),
        };
        words.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        column.AddChild(words);
        panel.AddChild(column);

        if (open)
        {
            var button = new Button { Flat = true, TooltipText = $"Play as {name.Text}." };
            button.SetAnchorsPreset(LayoutPreset.FullRect);
            button.Pressed += () =>
            {
                _selected = character.Id;
                Refresh();
            };
            panel.AddChild(button);
        }
        return panel;
    }

    private static Label Muted(string text)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        return label;
    }

    // The first time a character is taken, ask whether to learn it first — once, whatever the answer.
    private void Begin(string character)
    {
        if (CharacterUnlocks.HasPlayed(_host.Meta, character) || !_host.Blueprint.HasTutorialFor(character))
        {
            _onRun(character);
            return;
        }
        AskTutorial(character);
    }

    private void Teach(string character)
    {
        CharacterUnlocks.MarkPlayed(_host, character);
        _onTutorial(character);
    }

    private void AskTutorial(string character)
    {
        var veil = new Control { Name = QuestionName, MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(center);
        var box = new PanelContainer { CustomMinimumSize = new Vector2(460, 0) };
        box.AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));
        center.AddChild(box);
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        box.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);
        var hero = _host.Blueprint.Characters.FirstOrDefault(c => c.Id == character)?.Start.HeroName ?? character;
        var question = new Label
        {
            Text = "Do you want to play a 5 min tutorial to get to know this character?",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        question.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(question);
        column.AddChild(Muted($"Two short fights and a rest, as {hero}. You can skip it at any time."));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        var yes = new Button { Name = "TutorialYes", Text = "Play the tutorial", CustomMinimumSize = new Vector2(180, 40) };
        yes.Pressed += () =>
        {
            veil.QueueFree();
            Teach(character);
        };
        var no = new Button { Name = "TutorialNo", Text = "Start the run", CustomMinimumSize = new Vector2(180, 40) };
        no.Pressed += () =>
        {
            veil.QueueFree();
            CharacterUnlocks.MarkPlayed(_host, character);
            _onRun(character);
        };
        row.AddChild(yes);
        row.AddChild(no);
        column.AddChild(row);
        GetParent().GetParent().AddChild(veil);
        yes.CallDeferred(Control.MethodName.GrabFocus);
    }

    public static void Open(Godot.Node screen, GameHost host, Action<string> onRun, Action<string> onTutorial)
    {
        if (screen.GetNodeOrNull(OverlayName) is not null)
            return;
        var veil = new Control { MouseFilter = MouseFilterEnum.Stop, Name = OverlayName };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.7f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(center);
        void Close() => screen.GetNodeOrNull(OverlayName)?.QueueFree();
        center.AddChild(new CharacterSelectPanel(host,
            id => { Close(); onRun(id); },
            id => { Close(); onTutorial(id); },
            Close)
        { Name = nameof(CharacterSelectPanel) });
        screen.AddChild(veil);
    }
}
