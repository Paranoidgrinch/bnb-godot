using System;
using Godot;

namespace BnbGodot;

// ── THE ENCOUNTER TRIALS, ONE SHEET FOR ALL ACTS (user, 2026-11-05; 2026-11-17) ──────────────────────────────
// The title has ONE "Trials" entry (one button per act made the menu a wall of them); this sheet carries a tab per
// act that has a trial and opens on the act last played. For the act chosen it asks which: the full act, as it always was (and taking a
// full trial up again), or one fight of the act on its own — listed stage by stage, normal, multi, elite, then
// the bosses, each by its name. A single fight first hands out what the full trial would have by then (the
// packages of every stage before its own, GameHost.TrialSingles), then fights that fight alone. Built the way
// NewRunPanel is: a dimmed sheet with a panel on it.
public partial class TrialPanel : PanelContainer
{
    private readonly GameHost _host;
    private int _act;
    private VBoxContainer _body = null!;
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

        var title = new Label { Text = "Encounter trials", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        column.AddChild(title);

        // One tab per act with a trial in the game document; the act shown is pressed in.
        var tabs = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Name = "TrialActTabs" };
        tabs.AddThemeConstantOverride("separation", 6);
        var group = new ButtonGroup();
        foreach (var act in Acts(_host))
        {
            var tab = new Button
            {
                Text = $"Act {Numeral(act)}",
                Name = $"TrialAct{act}",
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = act == _act,
                CustomMinimumSize = new Vector2(84, 34),
                TooltipText = _host.HasTrialSave(act) || _host.SavedTrialSingle(act) is not null
                    ? $"Act {Numeral(act)} — a trial is waiting to be continued" : $"Act {Numeral(act)}",
            };
            var chosen = act;
            tab.Toggled += on => { if (on) Show(chosen); };
            tabs.AddChild(tab);
        }
        column.AddChild(tabs);

        _body = new VBoxContainer { Name = "TrialActBody" };
        _body.AddThemeConstantOverride("separation", 8);
        column.AddChild(_body);
        Fill(_body);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var cancel = new Button { Text = "Back", CustomMinimumSize = new Vector2(120, 40) };
        cancel.Pressed += () => _onCancel();
        actions.AddChild(cancel);
        column.AddChild(actions);
    }

    // The acts whose trial is in the game document, in order.
    public static System.Collections.Generic.List<int> Acts(GameHost host) =>
        [.. System.Linq.Enumerable.Where(System.Linq.Enumerable.Range(1, 5), host.HasTrial)];

    // The act the sheet opens on: the one last started here this session, else one with a trial waiting to be
    // continued, else the first.
    private static int? _lastAct;

    public static int DefaultAct(GameHost host)
    {
        var acts = Acts(host);
        if (_lastAct is { } last && acts.Contains(last))
            return last;
        foreach (var act in acts)
            if (host.HasTrialSave(act) || host.SavedTrialSingle(act) is not null)
                return act;
        return acts.Count > 0 ? acts[0] : 1;
    }

    private void Show(int act)
    {
        if (act == _act)
            return;
        _act = act;
        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
        Fill(_body);
    }

    // Everything the sheet says about the act shown.
    private void Fill(VBoxContainer column)
    {
        var numeral = Numeral(_act);

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

    }

    // Starting and taking up both report through the playback; a failed resume says so by returning false.
    private void Begin(Func<bool> start)
    {
        _lastAct = _act;
        if (start() && _host.Play?.Error is null)
            _onStarted();
    }

    private void Begin(Action start) => Begin(() =>
    {
        start();
        return true;
    });

    public const string OverlayName = "TrialOverlay";

    public static void Open(Godot.Node screen, GameHost host, Action onStarted) =>
        Open(screen, host, DefaultAct(host), onStarted);

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
