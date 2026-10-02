using System;
using Godot;
using RogueDeck.Run;

namespace BnbGodot;

// THE ONE QUESTION "New run ▸" ASKS, and it asks it because two map generators ship and they make genuinely
// different games (docs/bnb-act-map-specs.md). Built in code like every other window here, in the same shape
// as BugReportPanel: a dimmed sheet with a panel on it, one question, two answers, no version numbers doing
// the explaining.
//
// It also takes a SEED (RunSeeds): empty for a random run, a shared one typed in, or today's daily.
//
// The choice is REMEMBERED (RunPreferences) and preselected next time, because a playtester comparing the two
// starts a great many runs and being asked afresh every time is a tax on the thing they are here to do.
//
// And it is asked ONCE, at the start, on purpose: from the moment the run exists the generator belongs to the
// run and travels in its save. "Continue run" never opens this window — a resumed run rebuilds the maps it had,
// not the ones this dialog currently prefers.
public partial class NewRunPanel : PanelContainer
{
    private readonly Action<string, int?> _onBegin;
    private readonly Action _onCancel;
    private readonly int? _presetSeed;
    private string _choice = RunPreferences.MapGenerator;
    private LineEdit _seed = null!;

    public NewRunPanel(Action<string, int?> onBegin, Action onCancel, int? presetSeed = null)
    {
        _onBegin = onBegin;
        _onCancel = onCancel;
        _presetSeed = presetSeed;
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

        // NO MAP QUESTION ANY MORE (playtest feedback 2, D1): the old rule-based maps are archived. Every new run
        // walks the design's own maps; a save made on the old ones still resumes on them, and the probes still
        // reach them with `--legacy`.
        _choice = MapGenerators.Strategic;

        column.AddChild(SeedRow());

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        actions.AddThemeConstantOverride("separation", 10);

        var cancel = new Button { Text = "Not yet", CustomMinimumSize = new Vector2(120, 40) };
        cancel.Pressed += () => _onCancel();
        actions.AddChild(cancel);

        var begin = new Button { Text = "Begin ▸", CustomMinimumSize = new Vector2(160, 40) };
        begin.Pressed += () =>
        {
            RunPreferences.SetMapGenerator(_choice);
            _onBegin(_choice, RunSeeds.Parse(_seed.Text));
        };
        actions.AddChild(begin);
        column.AddChild(actions);
    }

    // THE SEED: empty for a random run, a number or a word to play a shared one, or today's daily. The daily
    // also sets the map generator to the design's own (v0.0.1), because "the same run for everybody" has to be
    // the same MAPS for everybody too — a player may still change it, and the run then is simply not the daily.
    private Control SeedRow()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var label = new Label { Text = "Seed", CustomMinimumSize = new Vector2(60, 0) };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        row.AddChild(label);
        _seed = new LineEdit
        {
            PlaceholderText = "random",
            Text = _presetSeed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MaxLength = 40,
            Name = "SeedField",
            TooltipText = "A number or any word. The same seed gives the same maps, rewards and shops.",
        };
        row.AddChild(_seed);
        var today = DateTime.UtcNow;
        var daily = new Button
        {
            Text = $"Today's run ({RunSeeds.DailyLabel(today)})",
            Name = "DailyButton",
            TooltipText = "The same run for everybody today: today's seed on the design's own maps.",
        };
        daily.Pressed += () =>
        {
            _seed.Text = RunSeeds.Daily(today).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _choice = MapGenerators.Strategic;
        };
        row.AddChild(daily);
        box.AddChild(row);
        var note = new Label
        {
            Text = "Leave it empty for a new run of your own. The seed of every run is in its history, to share or play again.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        note.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        note.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(note);
        return box;
    }

    public const string OverlayName = "NewRunOverlay";

    // The dimmed sheet, the same one BugReportPanel hangs on. `onBegin` is handed the chosen generator.
    public static void Open(Godot.Node screen, Action<string, int?> onBegin, int? presetSeed = null)
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
            (generator, seed) =>
            {
                screen.GetNodeOrNull(OverlayName)?.QueueFree();
                onBegin(generator, seed);
            },
            () => screen.GetNodeOrNull(OverlayName)?.QueueFree(),
            presetSeed)
        { Name = nameof(NewRunPanel) });
        veil.AddChild(center);
        screen.AddChild(veil);
    }
}
