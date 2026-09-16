using System;
using Godot;
using RogueDeck.Run;

namespace BnbGodot;

// THE ONE QUESTION "New run ▸" ASKS, and it asks it because two map generators ship and they make genuinely
// different games (docs/bnb-act-map-specs.md). Built in code like every other window here, in the same shape
// as BugReportPanel: a dimmed sheet with a panel on it, one question, two answers, no version numbers doing
// the explaining.
//
// The choice is REMEMBERED (RunPreferences) and preselected next time, because a playtester comparing the two
// starts a great many runs and being asked afresh every time is a tax on the thing they are here to do.
//
// And it is asked ONCE, at the start, on purpose: from the moment the run exists the generator belongs to the
// run and travels in its save. "Continue run" never opens this window — a resumed run rebuilds the maps it had,
// not the ones this dialog currently prefers.
public partial class NewRunPanel : PanelContainer
{
    private readonly Action<string> _onBegin;
    private readonly Action _onCancel;
    private string _choice = RunPreferences.MapGenerator;

    public NewRunPanel(Action<string> onBegin, Action onCancel)
    {
        _onBegin = onBegin;
        _onCancel = onCancel;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(620, 0);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        var title = new Label { Text = "Start a new run", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        column.AddChild(title);

        var ask = new Label
        {
            Text = "How should this run's maps be laid out?",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        ask.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        column.AddChild(ask);

        var group = new ButtonGroup();
        foreach (var generator in new[] { MapGenerators.Strategic, MapGenerators.RuleBased })
            column.AddChild(Option(generator, group));

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        actions.AddThemeConstantOverride("separation", 10);

        var cancel = new Button { Text = "Not yet", CustomMinimumSize = new Vector2(120, 40) };
        cancel.Pressed += () => _onCancel();
        actions.AddChild(cancel);

        var begin = new Button { Text = "Begin ▸", CustomMinimumSize = new Vector2(160, 40) };
        begin.Pressed += () =>
        {
            RunPreferences.SetMapGenerator(_choice);
            _onBegin(_choice);
        };
        actions.AddChild(begin);
        column.AddChild(actions);
    }

    // One answer: a toggle that carries the plain-English name, and under it the sentence that says what it
    // actually means for the act the player is about to walk.
    private Control Option(string generator, ButtonGroup group)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);

        var pick = new Button
        {
            Text = RunPreferences.Title(generator),
            ToggleMode = true,
            ButtonGroup = group,
            ButtonPressed = generator == _choice,
            Alignment = HorizontalAlignment.Left,
            // Named so the probe can find and press one without knowing where the dialog put it.
            Name = OptionName(generator),
        };
        pick.Pressed += () => _choice = generator;
        box.AddChild(pick);

        var blurb = new Label
        {
            Text = RunPreferences.Blurb(generator),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        blurb.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        box.AddChild(blurb);
        return box;
    }

    public static string OptionName(string generator) =>
        generator == MapGenerators.RuleBased ? "OptionRuleBased" : "OptionStrategic";

    public const string OverlayName = "NewRunOverlay";

    // The dimmed sheet, the same one BugReportPanel hangs on. `onBegin` is handed the chosen generator.
    public static void Open(Godot.Node screen, Action<string> onBegin)
    {
        if (screen.GetNodeOrNull(OverlayName) is not null)
            return;

        var veil = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Name = OverlayName };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        center.AddChild(new NewRunPanel(
            generator =>
            {
                screen.GetNodeOrNull(OverlayName)?.QueueFree();
                onBegin(generator);
            },
            () => screen.GetNodeOrNull(OverlayName)?.QueueFree())
        { Name = nameof(NewRunPanel) });
        veil.AddChild(center);
        screen.AddChild(veil);
    }
}
