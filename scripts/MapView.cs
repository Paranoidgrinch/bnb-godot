using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RunNode = RogueDeck.Run.Node;

namespace BnbGodot;

// The run map as a graph: every node drawn at its authored (or auto-laid-out) position, edges as lines,
// each node colored by kind and by run state (visited / current / reachable). During a path fork the
// reachable nodes (the session's PendingNodeChoices) are the clickable buttons; otherwise the map is a
// read-only "you are here" overview. Self-contained so SessionScreen just drops it in during the
// interlude and fork states.
public partial class MapView : Control
{
    private readonly RunMap _map;
    private readonly RunState _run;
    private readonly System.Collections.Generic.HashSet<string> _reachable;
    private readonly Action<string>? _onPick;
    private System.Collections.Generic.Dictionary<NodeId, (int X, int Y)> _positions = new();

    private const int NodeW = 130;
    private const int NodeH = 46;

    public MapView(
        RunMap map, RunState run,
        System.Collections.Generic.IEnumerable<string>? reachable = null,
        Action<string>? onPick = null)
    {
        _map = map;
        _run = run;
        _reachable = reachable is null ? [] : [.. reachable];
        _onPick = onPick;
    }

    public override void _Ready()
    {
        _positions = new System.Collections.Generic.Dictionary<NodeId, (int X, int Y)>(MapGraphLayout.Resolve(_map));
        var (width, height) = MapGraphLayout.CanvasSize(_positions);
        CustomMinimumSize = new Vector2(width, height);

        foreach (var node in _map.Nodes)
        {
            var pos = _positions[node.Id];
            var isReachable = _reachable.Contains(node.Id.Value);
            var button = new Button
            {
                Text = $"{Icon(node)} {ShortLabel(node)}",
                Position = new Vector2(pos.X, pos.Y),
                Size = new Vector2(NodeW, NodeH),
                Disabled = !isReachable || _onPick is null,
                TooltipText = node.Id.Value,
            };
            Style(button, node, isReachable);
            if (isReachable && _onPick is { } pick)
            {
                var id = node.Id.Value;
                button.Pressed += () => pick(id);
            }
            AddChild(button);
        }
    }

    // Edges drawn beneath the node buttons (children draw on top of _Draw).
    public override void _Draw()
    {
        var traveled = new Color(MoonvineTheme.AccentDark, 0.9f);
        var ahead = new Color(MoonvineTheme.TextMuted, 0.35f);
        foreach (var edge in _map.Edges)
        {
            if (!_positions.TryGetValue(edge.From, out var from) || !_positions.TryGetValue(edge.To, out var to))
                continue;
            var a = new Vector2(from.X + NodeW / 2f, from.Y + NodeH);
            var b = new Vector2(to.X + NodeW / 2f, to.Y);
            var walked = _run.VisitedNodes.Any(n => n.Value == edge.From.Value)
                && _run.VisitedNodes.Any(n => n.Value == edge.To.Value);
            DrawLine(a, b, walked ? traveled : ahead, walked ? 3f : 1.5f);
        }
    }

    private void Style(Button button, RunNode node, bool reachable)
    {
        var visited = _run.VisitedNodes.Any(n => n.Value == node.Id.Value);
        var current = _run.CurrentNodeId?.Value == node.Id.Value;
        var accent = KindColor(node);

        var fill = current ? MoonvineTheme.BgControl
            : visited ? MoonvineTheme.BgPanelStrong
            : reachable ? MoonvineTheme.BgPanel
            : MoonvineTheme.BgPanelStrong;
        var border = current ? MoonvineTheme.AccentLight
            : reachable ? accent
            : new Color(accent, 0.35f);

        var style = MoonvineTheme.Panel(fill, border, 8);
        style.BorderWidthTop = style.BorderWidthBottom = style.BorderWidthLeft = style.BorderWidthRight = current ? 3 : 1;
        button.AddThemeStyleboxOverride("normal", style);
        button.AddThemeStyleboxOverride("hover", MoonvineTheme.Panel(MoonvineTheme.BgControl, MoonvineTheme.Accent, 8));
        button.AddThemeStyleboxOverride("pressed", style);
        button.AddThemeStyleboxOverride("disabled", style);
        button.AddThemeColorOverride("font_color", visited && !current ? MoonvineTheme.TextMuted : MoonvineTheme.TextSoft);
        button.AddThemeColorOverride("font_disabled_color", visited ? MoonvineTheme.TextMuted : MoonvineTheme.TextSoft);
        button.AddThemeFontSizeOverride("font_size", 12);
    }

    // ── node kind → icon / color / short label ───────────────────────────────────

    private static bool IsBoss(RunNode node) => node.Id.Value.Contains("boss");

    private static string EventKind(RunNode node) =>
        node.Payload is EventRef reference
            ? reference.Id.Value.Split(':')[0]
            : "event";

    private static string Icon(RunNode node) => node.Type.Value switch
    {
        "combat" => IsBoss(node) ? "☠" : "⚔",
        "shop" => "🛒",
        "workbench" => "🔨",
        "event" => EventKind(node) switch { "rest" => "🛏", "treasure" => "📦", _ => "❓" },
        _ => "•",
    };

    private static string ShortLabel(RunNode node) => node.Type.Value switch
    {
        "combat" => IsBoss(node) ? "Boss" : "Fight",
        "shop" => "Shop",
        "workbench" => "Craft",
        "event" => EventKind(node) switch { "rest" => "Rest", "treasure" => "Treasure", _ => "Event" },
        _ => node.Type.Value,
    };

    private static Color KindColor(RunNode node) => node.Type.Value switch
    {
        "combat" => IsBoss(node) ? new Color("e07070") : new Color("d9a066"),
        "shop" => new Color("e0c98a"),
        "workbench" => MoonvineTheme.Accent,
        "event" => EventKind(node) switch
        {
            "rest" => new Color("8ab6e0"),
            "treasure" => new Color("e0c98a"),
            _ => new Color("c79ae0"),
        },
        _ => MoonvineTheme.TextMuted,
    };
}
