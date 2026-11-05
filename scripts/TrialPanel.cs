using System;
using Godot;

namespace BnbGodot;

// ── ONE ACT'S ENCOUNTER TRIAL, ASKED HOW (user, 2026-11-05) ──────────────────────────────────────────────────
// The title has one entry per act's trial; this sheet asks which: the full act, as it always was (and taking a
// full trial up again), or one fight of the act on its own — listed stage by stage, normal, multi, elite, then
// the bosses, each by its name. A single fight first hands out what the full trial would have by then (the
// packages of every stage before its own, GameHost.TrialSingles), then fights that fight alone. Built the way
// NewRunPanel is: a dimmed sheet with a panel on it.
public partial class TrialPanel : PanelContainer
{
    private readonly GameHost _host;
    private readonly int _act;
    private readonly Action _onStarted;
    private readonly Action _onCancel;

    public TrialPanel(GameHost host, int act, Action onStarted, Action onCancel)
    {
        _host = host;
        _act = act;
        _onStarted = onStarted;
        _onCancel = onCancel;
    }

    public static string Numeral(int act) => act switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => act.ToString() };

    private static readonly string[] Kinds = ["normal", "multi", "elite"];

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(620, 0);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        var numeral = Numeral(_act);
        var title = new Label { Text = $"Act {numeral} encounter trial", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        column.AddChild(title);

        // (a) The full act, as it always was.
        var full = new Button { Text = "Full act trial", Name = "FullTrialButton", CustomMinimumSize = new Vector2(0, 40) };
        full.TooltipText = $"Every Act {numeral} fight in stage order as the Lieutenant, healed after each, rewards between the stages. "
            + "Every fight is written down for the balance.";
        full.Pressed += () => Begin(() => _host.StartTrial(_act));
        column.AddChild(full);
        if (_host.HasTrialSave(_act))
        {
            var resume = new Button { Text = "Continue the full act trial", Name = "ContinueTrialButton", CustomMinimumSize = new Vector2(0, 40) };
            resume.Pressed += () => Begin(() => _host.ResumeTrial(_act));
            column.AddChild(resume);
        }
        if (_host.SavedTrialSingle(_act) is { } saved)
        {
            var resume = new Button { Text = $"Continue: {saved.Name}", Name = "ContinueTrialEncounterButton", CustomMinimumSize = new Vector2(0, 40) };
            resume.Pressed += () => Begin(() => _host.ResumeTrial(_act, encounter: saved.Encounter));
            column.AddChild(resume);
        }

        // (b) One fight on its own.
        var note = new Label
        {
            Text = "Or one fight on its own: first the rewards of every stage before it, as the full trial hands them out, then that fight alone.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        note.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        note.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(note);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 380),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            Name = "TrialEncounterList",
        };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);
        column.AddChild(scroll);

        var singles = _host.TrialSingles(_act);
        foreach (var group in singles.GroupBy(s => (s.Stage, s.Kind)))
        {
            var heading = new Label
            {
                Text = group.Key.Kind == "boss" ? "Bosses" : $"Stage {group.Key.Stage} · {group.Key.Kind}",
            };
            heading.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
            list.AddChild(heading);
            foreach (var single in group)
            {
                var packages = single.Walk.Map.Nodes.Count - 1;
                var fight = new Button
                {
                    Text = single.Name,
                    Name = $"TrialEncounter_{single.Encounter}",
                    Alignment = HorizontalAlignment.Left,
                    CustomMinimumSize = new Vector2(0, 34),
                    TooltipText = $"{single.Encounter} — {(packages == 0 ? "no rewards first" : $"{packages} reward package(s) first")}",
                };
                fight.Pressed += () => Begin(() => _host.StartTrial(_act, single.Encounter));
                list.AddChild(fight);
            }
        }

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var cancel = new Button { Text = "Back", CustomMinimumSize = new Vector2(120, 40) };
        cancel.Pressed += () => _onCancel();
        actions.AddChild(cancel);
        column.AddChild(actions);
    }

    // Starting and taking up both report through the playback; a failed resume says so by returning false.
    private void Begin(Func<bool> start)
    {
        if (start() && _host.Play?.Error is null)
            _onStarted();
    }

    private void Begin(Action start) => Begin(() =>
    {
        start();
        return true;
    });

    public const string OverlayName = "TrialOverlay";

    public static void Open(Godot.Node screen, GameHost host, int act, Action onStarted)
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
        center.AddChild(new TrialPanel(host, act,
            () =>
            {
                screen.GetNodeOrNull(OverlayName)?.QueueFree();
                onStarted();
            },
            () => screen.GetNodeOrNull(OverlayName)?.QueueFree())
        { Name = nameof(TrialPanel) });
        veil.AddChild(center);
        screen.AddChild(veil);
    }
}
