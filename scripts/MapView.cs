using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RunNode = RogueDeck.Run.Node;

namespace BnbGodot;

// The act's map as a navigable graph: the entry at the top, the boss at the bottom, every room drawn where its
// depth puts it, edges as lines, each room a round token whose symbol and ring say what it IS (from
// assets/map/nodes/), shaded by how the run stands to it (walked / standing here / open). During a path fork the reachable rooms are the clickable buttons; otherwise the map is
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
    // Who at a multiplayer table has voted for which room (node id → names); null alone.
    private readonly System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.List<string>>? _votes;
    private System.Collections.Generic.Dictionary<NodeId, Vector2> _positions = new();

    // A room is a token, not a word (user, 2026-11-10): the symbol says what it is, so the map is half as wide as
    // it was with 116 px name plates.
    private const int NodeD = 44;  // the token's width and height
    private const int NameW = 200; // a boss room that wears its own name writes it under its token, this wide
    private const int NameH = 30;
    private const int LaneW = 76;  // horizontal step between two rooms of the same depth
    private const int RowH = 64;   // vertical step between one depth and the next
    private const int Margin = 14;

    public MapView(
        RunMap map, RunState run,
        System.Collections.Generic.IEnumerable<string>? reachable = null,
        Action<string>? onPick = null,
        System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.List<string>>? votes = null)
    {
        _map = map;
        _run = run;
        _reachable = reachable is null ? [] : [.. reachable];
        _onPick = onPick;
        _votes = votes;
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
        // An act with SEVERAL boss rooms is a gauntlet, and its whole shape is which bosses stand in it and in
        // what order — so those rooms wear their names under their tokens rather than three crowned skulls. An
        // ordinary act's single boss stays unnamed: finding out who ends the act is part of walking it.
        var named = _map.Nodes.Count(n => Role(n) == MapNodeTags.Boss) > 1;

        // a named room is NameW wide; its token sits in the middle of that
        var side = named ? (NameW - NodeD) / 2 : 0;
        _positions = Layout(_map, named ? NameW + 8 : LaneW, named ? RowH + NameH : RowH, Margin + side);
        var width = _positions.Count == 0 ? NodeD : (int)_positions.Values.Max(p => p.X) + NodeD + side + Margin;
        var height = _positions.Count == 0 ? NodeD
            : (int)_positions.Values.Max(p => p.Y) + NodeD + (named ? NameH : 0) + Margin;
        CustomMinimumSize = new Vector2(width, height);

        foreach (var node in _map.Nodes)
        {
            var pos = _positions[node.Id];
            var isReachable = _reachable.Contains(node.Id.Value);
            var role = Role(node);
            // WHO STANDS IN THE ROOM IS ALWAYS ONE HOVER AWAY — for the boss AND for the elite. A player
            // deciding which path to walk is deciding who to meet, and that is not a thing to withhold from
            // them: an elite is the one room on an ordinary path worth planning around, and "Elite" alone
            // says nothing about whether the deck in hand can take it.
            // The name is only WRITTEN ACROSS the room in a gauntlet (several boss rooms, where the order IS
            // the act's shape). An elite never widens its room — there are several per act and a row of
            // 240 px rooms would not be a map any more.
            var who = role is MapNodeTags.Boss or MapNodeTags.Elite ? NameOf(node) : null;
            var name = named && role == MapNodeTags.Boss ? who : null;
            var token = Token(role);
            var button = new Button
            {
                // a kind of room with no token of its own still says what it is, in its word
                Text = token is null ? Label(role) : "",
                Icon = token,
                ExpandIcon = true,
                IconAlignment = HorizontalAlignment.Center,
                Position = pos,
                Size = new Vector2(NodeD, NodeD),
                Disabled = !isReachable || _onPick is null,
                TooltipText = who is null ? $"{Label(role)} — {Tooltip(role)}" : $"{who} — {Tooltip(role)}",
            };
            Style(button, node, role, isReachable);
            if (isReachable && _onPick is { } pick)
            {
                var id = node.Id.Value;
                button.Pressed += () => pick(id);
            }
            AddChild(button);
            if (name is not null)
            {
                var plate = new Label
                {
                    Text = name,
                    Position = pos + new Vector2((NodeD - NameW) / 2f, NodeD + 2),
                    Size = new Vector2(NameW, NameH),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                plate.AddThemeFontSizeOverride("font_size", 11);
                plate.AddThemeColorOverride("font_color", RoleColor(role));
                AddChild(plate);
            }
            // THE TABLE'S VOTES hang under the room they are for: who wants to go there, by name.
            if (_votes is not null && _votes.TryGetValue(node.Id.Value, out var voters) && voters.Count > 0)
            {
                var badge = new Label
                {
                    Text = $"◆ {string.Join(", ", voters)}",
                    Position = pos + new Vector2((NodeD - LaneW) / 2f, NodeD - 2),
                    Size = new Vector2(LaneW, 18),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    ClipText = true,
                    TooltipText = $"Voted for this room: {string.Join(", ", voters)}",
                    MouseFilter = MouseFilterEnum.Pass,
                    Name = $"Votes_{node.Id.Value}",
                };
                badge.AddThemeFontSizeOverride("font_size", 12);
                badge.AddThemeColorOverride("font_color", MoonvineTheme.Signal);
                AddChild(badge);
            }
            if (_run.CurrentNodeId?.Value == node.Id.Value)
                CurrentRoomPosition = pos;
        }
    }

    // Depth downward, lanes across — the shape a run map is read in. The engine's layered layout already knows
    // each node's depth (longest path from an entry); this turns its columns into rows and centres each row.
    // `laneW` and `rowH` are the steps across and down; `inset` is where the first room's token starts.
    private static System.Collections.Generic.Dictionary<NodeId, Vector2> Layout(RunMap map, int laneW, int rowH, int inset)
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
            var offset = (widest - inRow) * laneW / 2f;
            result[node.Id] = new Vector2(inset + offset + lane[node.Id] * laneW, Margin + row * rowH);
        }
        return result;
    }

    // Edges drawn beneath the room buttons (children draw on top of _Draw).
    public override void _Draw()
    {
        var traveled = new Color(MoonvineTheme.Accent, 0.75f); // the road you walked, in gold
        var ahead = new Color(MoonvineTheme.TextMuted, 0.3f);
        foreach (var edge in _map.Edges)
        {
            if (!_positions.TryGetValue(edge.From, out var from) || !_positions.TryGetValue(edge.To, out var to))
                continue;
            // centre to centre, stopped short of both rims
            var middle = new Vector2(NodeD / 2f, NodeD / 2f);
            var dir = (to - from).Normalized();
            var a = from + middle + dir * (NodeD / 2f + 2);
            var b = to + middle - dir * (NodeD / 2f + 2);
            var walked = _run.VisitedNodes.Any(n => n.Value == edge.From.Value)
                && _run.VisitedNodes.Any(n => n.Value == edge.To.Value);
            DrawLine(a, b, walked ? traveled : ahead, walked ? 3f : 1.5f);
        }
    }

    // The token carries the room's colour in its ring; the button around it only says how the run stands to it.
    // Rooms that cannot be walked to now are faded, so the fork's open rooms are the bright ones; walked rooms
    // are greyed; the room the run stands in wears a gold halo, and an open room a thin one until hovered.
    private void Style(Button button, RunNode node, string role, bool reachable)
    {
        var visited = _run.VisitedNodes.Any(n => n.Value == node.Id.Value);
        var current = _run.CurrentNodeId?.Value == node.Id.Value;
        var open = reachable && _onPick is not null;

        StyleBox rest = current ? Halo(MoonvineTheme.AccentLight, 3)
            : open ? Halo(new Color(MoonvineTheme.Accent, 0.8f), 2)
            : new StyleBoxEmpty();
        foreach (var state in new[] { "normal", "pressed", "disabled", "focus" })
            button.AddThemeStyleboxOverride(state, rest);
        button.AddThemeStyleboxOverride("hover", Halo(MoonvineTheme.AccentLight, 3));

        var tint = current || open ? Colors.White
            : visited ? new Color(0.62f, 0.6f, 0.58f, 0.85f)
            : reachable ? Colors.White
            : new Color(1, 1, 1, 0.55f);
        foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_disabled_color", "icon_focus_color" })
            button.AddThemeColorOverride(state, tint);
        // the word a room without a token falls back to
        var text = visited && !current ? MoonvineTheme.TextMuted : RoleColor(role);
        button.AddThemeColorOverride("font_color", text);
        button.AddThemeColorOverride("font_disabled_color", text);
        button.AddThemeFontSizeOverride("font_size", 9);
    }

    // A ring just outside the token's own.
    private static StyleBoxFlat Halo(Color color, int width)
    {
        var halo = new StyleBoxFlat { DrawCenter = false, BorderColor = color, AntiAliasing = true };
        halo.SetBorderWidthAll(width);
        halo.SetCornerRadiusAll(NodeD / 2 + 3);
        halo.SetExpandMarginAll(3);
        return halo;
    }

    // The token for a kind of room (assets/map/nodes/make_nodes.py), or null for a kind that has none yet.
    public static Texture2D? Token(string role)
    {
        var path = $"res://assets/map/nodes/{role}.png";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    // ── what a room is ───────────────────────────────────────────────────────────

    // The role the room was generated for. A mimic reads as a treasure: the player is meant to find out by
    // opening it. Anything untagged (an authored map) falls back to its node type.
    // What KIND of room a node is, in one word. ⚠ One definition, in the engine-side bot (R4): the drawing,
    // three walkers and two reports all say this word, and a mimic that answered "mimic" in one of them would
    // spoil the one room in the game whose point is the surprise.
    public static string Role(RunNode node) => RogueDeck.Bot.MapRole.Of(node);

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

    // THE MAP'S LEGEND IS A SENTENCE, not nine tastes. A room is coloured by what it DOES to you:
    // the warm ramp fights you and gets hotter the harder it is, gold sells to you, violet surprises you,
    // steel mends you, copper builds for you. The four ember shades are the only colours in the game that
    // are not theme tokens — they exist to be told apart from ONE ANOTHER at a glance on a shrunken map,
    // which is a job no single token can do.
    public static Color RoleColor(string role) => role switch
    {
        MapNodeTags.Combat => new Color("b4553f"),      // rust — an ordinary fight
        MapNodeTags.MultiCombat => new Color("c9633a"), // brighter rust — several of them
        MapNodeTags.Elite => new Color("d8443f"),       // hotter still
        MapNodeTags.Boss => new Color("e0313c"),        // the hottest thing on the map; it ends the act
        MapNodeTags.Shop => MoonvineTheme.Accent,
        MapNodeTags.Treasure => MoonvineTheme.AccentLight,
        MapNodeTags.Rest => MoonvineTheme.Steel,
        MapNodeTags.Workbench => MoonvineTheme.Copper,
        MapNodeTags.Event => MoonvineTheme.Arcane,
        _ => MoonvineTheme.TextMuted,
    };
}
