using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BnbGodot;

// THE RUN HISTORY, a main-menu item of its own: the numbers across every run first (how many, how many won,
// how far the best one got, what keeps killing you), then every run, newest on top. A run opens on a click to
// show what it ended holding. Reads RunHistory and nothing else — it is the player's own record, kept on this
// machine.
public partial class HistoryPanel : PanelContainer
{
    public const string OverlayName = "HistoryOverlay";

    private readonly Action _onClose;
    private readonly Action<int>? _onPlaySeed;
    private readonly HashSet<int> _open = [];
    private VBoxContainer _list = null!;
    private List<RunSummary> _runs = [];

    public HistoryPanel(Action onClose, Action<int>? onPlaySeed = null)
    {
        _onClose = onClose;
        _onPlaySeed = onPlaySeed;
    }

    // `onPlaySeed` opens a new run on a past run's seed; null where no run can be started from.
    public static void Open(Control screen, Action<int>? onPlaySeed)
    {
        if (screen.GetNodeOrNull(OverlayName) is not null)
            return;
        var veil = new Control { Name = OverlayName, MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        center.AddChild(new HistoryPanel(() => screen.GetNodeOrNull(OverlayName)?.QueueFree(), onPlaySeed)
            { Name = nameof(HistoryPanel) });
        veil.AddChild(center);
        screen.AddChild(veil);
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(1000, 660);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));
        _runs = RunHistory.Load();

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);

        var head = new HBoxContainer();
        var title = new Label { Text = "Run history", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        head.AddChild(title);
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(120, 36) };
        close.Pressed += () => _onClose();
        head.AddChild(close);
        column.AddChild(head);

        column.AddChild(Stats());

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_list);
        column.AddChild(scroll);
        Fill();
    }

    // ── the numbers ──────────────────────────────────────────────────────────────
    private Control Stats()
    {
        var finished = _runs.Where(r => r.Result is "Victory" or "Defeat").ToList();
        var wins = finished.Count(r => r.Result == "Victory");
        var deadliest = _runs.Where(r => r.Result == "Defeat" && r.EndedAt is not null)
            .GroupBy(r => r.EndedAt!)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        // ⚠ No "time played": a run's start and end are wall-clock times, and a run resumed three days later
        // would count the three days. What can be counted honestly is counted.
        var streak = 0;
        var best = 0;
        foreach (var run in finished)
        {
            streak = run.Result == "Victory" ? streak + 1 : 0;
            best = Math.Max(best, streak);
        }

        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 8);
        grid.AddChild(Tile("Runs", _runs.Count.ToString(),
            _runs.Count(r => r.Result == "Abandoned") is var given and > 0 ? $"{given} given up" : null));
        grid.AddChild(Tile("Victories", wins.ToString(),
            finished.Count > 0 ? $"{100.0 * wins / finished.Count:0}% of finished runs" : null));
        grid.AddChild(Tile("Furthest", _runs.Count > 0 && _runs.Max(r => r.Act) is var act and > 0 ? $"Act {act}" : "—",
            _runs.Where(r => r.Rooms > 0).Select(r => r.Rooms).DefaultIfEmpty(0).Max() is var rooms and > 0
                ? $"{rooms} rooms in one run" : null));
        grid.AddChild(Tile("Deadliest", deadliest?.Key ?? "—",
            deadliest is not null ? $"ended {deadliest.Count()} run{(deadliest.Count() == 1 ? "" : "s")}" : null));
        grid.AddChild(Tile("Best streak", best > 0 ? $"{best} win{(best == 1 ? "" : "s")}" : "—",
            streak > 0 ? $"{streak} in a row right now" : null));
        grid.AddChild(Tile("Characters", PerCharacter(), null));
        return grid;
    }

    private string PerCharacter()
    {
        var lines = _runs.Where(r => r.Character is not null)
            .GroupBy(r => r.Character!)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{g.Key} {g.Count(r => r.Result == "Victory")}/{g.Count()}");
        var text = string.Join(" · ", lines);
        return text.Length == 0 ? "—" : text;
    }

    private static Control Tile(string label, string value, string? under)
    {
        var tile = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tile.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(MoonvineTheme.BgPanel, MoonvineTheme.Hairline));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        var name = new Label { Text = label };
        name.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        name.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(name);
        var number = new Label { Text = value, ClipText = true, TooltipText = value, MouseFilter = MouseFilterEnum.Stop };
        number.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(number);
        if (under is not null)
        {
            var small = new Label { Text = under };
            small.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
            small.AddThemeFontSizeOverride("font_size", 12);
            box.AddChild(small);
        }
        tile.AddChild(box);
        return tile;
    }

    // ── the runs ─────────────────────────────────────────────────────────────────
    private void Fill()
    {
        foreach (var child in _list.GetChildren())
            child.QueueFree();
        if (_runs.Count == 0)
        {
            var none = new Label
            {
                Text = "No runs yet. Every run you finish, win or lose, is written down here.",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            none.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
            _list.AddChild(none);
            return;
        }
        for (var i = _runs.Count - 1; i >= 0; i--)
            _list.AddChild(Row(i, _runs[i]));
    }

    private Control Row(int index, RunSummary run)
    {
        var open = _open.Contains(index);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(
            open ? MoonvineTheme.BgRaised : MoonvineTheme.BgPanel, open ? MoonvineTheme.AccentLight : null));
        var box = new VBoxContainer();
        panel.AddChild(box);

        var line = new Button
        {
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            Text = $"{(open ? "▾" : "▸")}  {When(run)}   {run.Character ?? "?"}   {Where(run)}",
            TooltipText = run.Result == "Abandoned" ? "Given up for a new run." : "Show what this run ended with.",
        };
        line.AddThemeColorOverride("font_color", run.Result switch
        {
            "Victory" => MoonvineTheme.Accent,
            "Defeat" => MoonvineTheme.TextSoft,
            _ => MoonvineTheme.TextMuted,
        });
        line.Pressed += () =>
        {
            if (!_open.Remove(index))
                _open.Add(index);
            Fill();
        };
        box.AddChild(line);

        if (open)
        {
            var details = new List<string>
            {
                $"Seed {run.Seed}",
            };
            if (run.MaxHealth > 0)
                details.Add($"Ended on {run.Health}/{run.MaxHealth} health and {run.Gold} gold");
            if (run.Relics.Count > 0)
                details.Add($"Relics ({run.Relics.Count}): {string.Join(", ", run.Relics)}");
            if (run.Deck.Count > 0)
                details.Add($"Deck ({run.Deck.Count}): {string.Join(", ", run.Deck.GroupBy(c => c).OrderBy(g => g.Key)
                    .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key))}");
            var text = new Label
            {
                Text = string.Join("\n", details),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(900, 0),
            };
            text.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
            text.AddThemeFontSizeOverride("font_size", 13);
            var inset = new MarginContainer();
            inset.AddThemeConstantOverride("margin_left", 28);
            inset.AddThemeConstantOverride("margin_bottom", 6);
            var inner = new VBoxContainer();
            inner.AddThemeConstantOverride("separation", 6);
            inner.AddChild(text);
            if (_onPlaySeed is { } play)
            {
                var again = new Button
                {
                    Text = $"Play seed {run.Seed} again ▸",
                    SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
                    TooltipText = "A new run on the same seed: the same maps, rewards and shops.",
                };
                again.Pressed += () => play(run.Seed);
                inner.AddChild(again);
            }
            inset.AddChild(inner);
            box.AddChild(inset);
        }
        return panel;
    }

    private static string Where(RunSummary run)
    {
        var rooms = run.Rooms > 0 ? $" · {run.Rooms} rooms" : "";
        return run.Result switch
        {
            "Victory" => $"★ Victory{rooms}",
            "Defeat" => $"✝ Fell in Act {run.Act}{rooms}" + (run.EndedAt is { } foe ? $" to {foe}" : ""),
            _ => $"Given up{rooms}",
        };
    }

    private static string When(RunSummary run) =>
        DateTime.TryParse(run.EndedUtc ?? run.StartedUtc, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var at)
            ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : "—";
}
