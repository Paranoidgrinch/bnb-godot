using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RunNode = RogueDeck.Run.Node;

namespace BnbGodot;

// The act's map as a navigable graph: the entry at the top, the boss at the bottom, every room drawn where its
// depth puts it, edges as lines, each room colored by what it IS and by how the run stands to it (walked /
// standing here / open). During a path fork the reachable rooms are the clickable buttons; otherwise the map is
// a read-only "you are here".
//
// Two things this must get right, because both would lie to the player:
//   • The map it draws is the RUN's — RunState.Map, the map generated for the act being walked. The blueprint's
//     own Map is empty in a generated game, and drawing that showed nothing at all.
//   • A room says what its TAG says (the role it was generated for), not what its id or payload look like: a
//     generated boss room is an ordinary combat node, and only the tag knows it is the boss. The one deliberate
//     lie is the mimic — it is tagged "mimic" and is a fight, and it must read as a treasure until it bites.
public partial class MapView : Control
{
    private readonly RunMap _map;
    private readonly RunState _run;
    private readonly System.Collections.Generic.HashSet<string> _reachable;
    private readonly Action<string>? _onPick;
    private System.Collections.Generic.Dictionary<NodeId, Vector2> _positions = new();

    private const int NodeW = 116;
    private const int NamedNodeW = 240; // a boss room that wears its own name needs the room to write it in
    private const int NodeH = 40;
    private const int LaneW = 132; // horizontal step between two rooms of the same depth
    private const int RowH = 66;   // vertical step between one depth and the next
    private const int Margin = 14;

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

    // The fight a room runs, by the name the document gives it. Falls back to the encounter id, so a missing
    // presentation entry shows up as words rather than as an empty button.
    private static string? NameOf(RunNode node) =>
        node.Payload is EncounterRef fight
            ? GameHost.Instance.Blueprint.Presentation.Encounters
                .GetValueOrDefault(fight.Id.Value)?.FlavorText ?? fight.Id.Value
            : null;

    // The room the run is standing at, for the screen to scroll to.
    public Vector2 CurrentRoomPosition { get; private set; }

    public override void _Ready()
    {
        _positions = Layout(_map);

        // An act with SEVERAL boss rooms is a gauntlet, and its whole shape is which bosses stand in it and in
        // what order — so those rooms wear their names rather than the word "Boss" three times over. An
        // ordinary act's single boss stays unnamed: finding out who ends the act is part of walking it.
        var named = _map.Nodes.Count(n => Role(n) == MapNodeTags.Boss) > 1;

        var roomW = named ? NamedNodeW : NodeW;
        var width = _positions.Count == 0 ? roomW : (int)_positions.Values.Max(p => p.X) + roomW + Margin;
        var height = _positions.Count == 0 ? NodeH : (int)_positions.Values.Max(p => p.Y) + NodeH + Margin;
        CustomMinimumSize = new Vector2(width, height);

        foreach (var node in _map.Nodes)
        {
            var pos = _positions[node.Id];
            var isReachable = _reachable.Contains(node.Id.Value);
            var role = Role(node);
            var name = named && role == MapNodeTags.Boss ? NameOf(node) : null;
            var button = new Button
            {
                Text = name is null ? $"{Icon(role)}\n{Label(role)}" : $"{Icon(role)}  {name}",
                Position = pos,
                Size = new Vector2(name is null ? NodeW : NamedNodeW, NodeH),
                Disabled = !isReachable || _onPick is null,
                TooltipText = name is null ? Tooltip(role) : $"{name} — {Tooltip(role)}",
                AutowrapMode = name is null ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart,
            };
            Style(button, node, role, isReachable);
            if (isReachable && _onPick is { } pick)
            {
                var id = node.Id.Value;
                button.Pressed += () => pick(id);
            }
            AddChild(button);
            if (_run.CurrentNodeId?.Value == node.Id.Value)
                CurrentRoomPosition = pos;
        }
    }

    // Depth downward, lanes across — the shape a run map is read in. The engine's layered layout already knows
    // each node's depth (longest path from an entry); this turns its columns into rows and centres each row.
    private static System.Collections.Generic.Dictionary<NodeId, Vector2> Layout(RunMap map)
    {
        var resolved = MapGraphLayout.Resolve(map);
        var depth = new System.Collections.Generic.Dictionary<NodeId, int>();
        var lane = new System.Collections.Generic.Dictionary<NodeId, int>();
        foreach (var (id, position) in resolved)
        {
            depth[id] = position.X / MapGraphLayout.CellWidth;
            lane[id] = position.Y / MapGraphLayout.CellHeight;
        }

        var widest = depth.Count == 0 ? 1 : depth.Values.Distinct().Max(d => lane.Count(l => depth[l.Key] == d));
        var result = new System.Collections.Generic.Dictionary<NodeId, Vector2>();
        foreach (var node in map.Nodes)
        {
            var row = depth[node.Id];
            var inRow = lane.Count(l => depth[l.Key] == row);
            var offset = (widest - inRow) * LaneW / 2f;
            result[node.Id] = new Vector2(Margin + offset + lane[node.Id] * LaneW, Margin + row * RowH);
        }
        return result;
    }

    // Edges drawn beneath the room buttons (children draw on top of _Draw).
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

    private void Style(Button button, RunNode node, string role, bool reachable)
    {
        var visited = _run.VisitedNodes.Any(n => n.Value == node.Id.Value);
        var current = _run.CurrentNodeId?.Value == node.Id.Value;
        var accent = RoleColor(role);

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
        var text = visited && !current ? MoonvineTheme.TextMuted : reachable || current ? MoonvineTheme.Text : accent;
        button.AddThemeColorOverride("font_color", text);
        button.AddThemeColorOverride("font_disabled_color", text);
        button.AddThemeFontSizeOverride("font_size", 11);
    }

    // ── what a room is ───────────────────────────────────────────────────────────

    // The role the room was generated for. A mimic reads as a treasure: the player is meant to find out by
    // opening it. Anything untagged (an authored map) falls back to its node type.
    public static string Role(RunNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var tag = node.Tags.Count > 0 ? node.Tags[0] : node.Type.Value;
        return tag == MapNodeTags.Mimic ? MapNodeTags.Treasure : tag;
    }

    public static string Icon(string role) => role switch
    {
        MapNodeTags.Combat => "⚔",
        MapNodeTags.MultiCombat => "⚔⚔",
        MapNodeTags.Elite => "☣",
        MapNodeTags.Boss => "☠",
        MapNodeTags.Shop => "🛒",
        MapNodeTags.Rest => "🛏",
        MapNodeTags.Treasure => "📦",
        MapNodeTags.Workbench => "🔨",
        MapNodeTags.Event => "❓",
        _ => "•",
    };

    public static string Label(string role) => role switch
    {
        MapNodeTags.Combat => "Fight",
        MapNodeTags.MultiCombat => "Ambush",
        MapNodeTags.Elite => "Elite",
        MapNodeTags.Boss => "Boss",
        MapNodeTags.Shop => "Shop",
        MapNodeTags.Rest => "Rest",
        MapNodeTags.Treasure => "Treasure",
        MapNodeTags.Workbench => "Craft",
        MapNodeTags.Event => "Unknown",
        _ => role,
    };

    private static string Tooltip(string role) => role switch
    {
        MapNodeTags.Combat => "A fight.",
        MapNodeTags.MultiCombat => "Several of them at once.",
        MapNodeTags.Elite => "A hard fight — and a better reward.",
        MapNodeTags.Boss => "The one who ends the act.",
        MapNodeTags.Shop => "Spend gold on cards, relics, and having a card struck from the deck.",
        MapNodeTags.Rest => "Recover, or improve a card.",
        MapNodeTags.Treasure => "Something is filed here. Probably.",
        MapNodeTags.Workbench => "Craft.",
        MapNodeTags.Event => "A door. No telling what is behind it.",
        _ => role,
    };

    public static Color RoleColor(string role) => role switch
    {
        MapNodeTags.Combat => new Color("d9a066"),
        MapNodeTags.MultiCombat => new Color("d98a5c"),
        MapNodeTags.Elite => new Color("e07070"),
        MapNodeTags.Boss => new Color("e05050"),
        MapNodeTags.Shop => new Color("e0c98a"),
        MapNodeTags.Rest => new Color("8ab6e0"),
        MapNodeTags.Treasure => new Color("e0c98a"),
        MapNodeTags.Workbench => MoonvineTheme.Accent,
        MapNodeTags.Event => new Color("c79ae0"),
        _ => MoonvineTheme.TextMuted,
    };
}
