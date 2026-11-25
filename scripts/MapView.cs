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
    // The act drawn as a plan of its own (scripts/MapArt): the rooms stand on it, so the map grows by the plan's
    // margins and every room moves in by them. Not drawn without a renderer (the headless smoke runs).
    private TextureRect? _art;

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
        var (named, positions, width, height) = Shape(_map);
        _positions = positions;
        CustomMinimumSize = new Vector2(width, height);
        if (_positions.Count > 0 && !DisplayServer.GetName().Contains("headless"))
            StartArt(width, height);

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
            if (isReachable && _onPick is not null)
                AddChild(Beacon(pos));
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

    // Where every room stands and how large the map is, from the graph alone — the same answer for the view
    // and for Prewarm, which has to bake the very picture the view will ask for.
    private static (bool Named, System.Collections.Generic.Dictionary<NodeId, Vector2> Positions, int Width, int Height) Shape(RunMap map)
    {
        var named = map.Nodes.Count(n => Role(n) == MapNodeTags.Boss) > 1;
        // a named room is NameW wide; its token sits in the middle of that
        var side = named ? (NameW - NodeD) / 2 : 0;
        var positions = Layout(map, named ? NameW + 8 : LaneW, named ? RowH + NameH : RowH, Margin + side);
        var width = positions.Count == 0 ? NodeD : (int)positions.Values.Max(p => p.X) + NodeD + side + Margin;
        var height = positions.Count == 0 ? NodeD
            : (int)positions.Values.Max(p => p.Y) + NodeD + (named ? NameH : 0) + Margin;
        return (named, positions, width, height);
    }

    private static string ArtKey(RunMap map, RunState run) =>
        $"{run.RandomSeed}:{run.ActNumber}:{map.Nodes.Count}:{map.Edges.Count}";

    private static MapArt.Plan.Input ArtInput(RunMap map, RunState run,
        System.Collections.Generic.Dictionary<NodeId, Vector2> positions, int width, int height)
    {
        var input = new MapArt.Plan.Input(
            run.ActNumber, run.RandomSeed, width, height, NodeD,
            [.. map.Nodes.Select(n => new MapArt.Plan.Room(n.Id.Value, Role(n), positions[n.Id].X, positions[n.Id].Y))],
            [.. map.Edges.Select(e => (e.From.Value, e.To.Value))]);
        // dev: BNB_MAPART_ACT=5 draws Act V's fragments under three rooms of whatever map is open (top, middle,
        // bottom), since no probe stops on the gauntlet's map
        if (System.Environment.GetEnvironmentVariable("BNB_MAPART_ACT") == "5")
        {
            var byY = input.Rooms.OrderBy(r => r.Y).ToList();
            input = input with { Act = 5, Rooms = [byY[0], byY[byY.Count / 2], byY[^1]] };
        }
        return input;
    }

    // ★ THE PICTURE IS BAKED BEFORE IT IS ASKED FOR (playtest, 2026-11-25: "the map's picture comes a moment after
    // the rooms"). The screen calls this whenever a run's map is known — a fight in the act, the act's title
    // card — so by the time the map opens its picture is usually done. Cheap when it is: one key looked up.
    public static void Prewarm(RunMap? map, RunState run, Godot.Node host)
    {
        if (map is null || map.Nodes.Count == 0 || !host.IsInsideTree() || DisplayServer.GetName().Contains("headless"))
            return;
        var key = ArtKey(map, run);
        if (MapArt.Bake.Known(key))
            return;
        var (_, positions, width, height) = Shape(map);
        var input = ArtInput(map, run, positions, width, height);
        _ = Prewarmed(key, host.GetTree().Root, input);
    }

    private static async System.Threading.Tasks.Task Prewarmed(string key, Godot.Node root, MapArt.Plan.Input input)
    {
        try
        {
            await MapArt.Bake.Get(key, root, () => MapArt.Plan.Make(input));
        }
        catch (Exception e)
        {
            GD.PushWarning($"map art for act {input.Act} failed: {e.Message}");
        }
    }

    // The plan under the rooms: worked out on a worker thread and drawn once, then kept for the run (MapArt/Bake).
    private void StartArt(int width, int height)
    {
        var input = ArtInput(_map, _run, _positions, width, height);
        var origin = new Vector2(MapArt.Plan.Side, MapArt.Plan.Top);
        _positions = _positions.ToDictionary(kv => kv.Key, kv => kv.Value + origin);
        CustomMinimumSize = new Vector2(width + 2 * MapArt.Plan.Side, height + MapArt.Plan.Top + MapArt.Plan.Foot);
        _art = new TextureRect
        {
            Size = CustomMinimumSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            MouseFilter = MouseFilterEnum.Ignore,
            Name = "MapArt",
        };
        AddChild(_art);
        var key = ArtKey(_map, _run);
        if (MapArt.Bake.Cached(key) is { } done)
            _art.Texture = done;
        else
            ShowArtWhenReady(key, input);
    }

    // ⚠ THE ROOMS WAIT FOR THEIR PICTURE. A map whose rooms and roads stood on the bare page for a moment before
    // the picture arrived under them read as two things loading. Until the bake is done the whole view is held
    // invisible, then it comes in at once — picture, roads and rooms together. A bake that takes too long (a
    // slow machine's first map) lets the rooms show anyway after RevealAfter: the map has to stay usable.
    private const double RevealAfter = 1.5;

    private async void ShowArtWhenReady(string key, MapArt.Plan.Input input)
    {
        Modulate = new Color(1, 1, 1, 0);
        GetTree().CreateTimer(RevealAfter).Timeout += Reveal;
        try
        {
            var tex = await MapArt.Bake.Get(key, GetTree().Root, () => MapArt.Plan.Make(input));
            if (IsInstanceValid(this) && _art is not null && IsInstanceValid(_art))
                _art.Texture = tex;
        }
        catch (Exception e)
        {
            // the plan is a picture: the map still works without it, so say so and carry on
            GD.PushWarning($"map art for act {input.Act} failed: {e.Message}");
        }
        Reveal();
    }

    private void Reveal()
    {
        if (IsInstanceValid(this) && Modulate.A < 1)
            Modulate = Colors.White;
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
            var walkedHere = _run.VisitedNodes.Any(n => n.Value == edge.From.Value)
                && _run.VisitedNodes.Any(n => n.Value == edge.To.Value);
            if (_art is not null && !walkedHere)
                continue;   // the plan draws the paths; only the road walked is marked over it
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
    // are greyed; the room the run stands in wears a black ring, and an open room a thin gold one until hovered.
    private void Style(Button button, RunNode node, string role, bool reachable)
    {
        var visited = _run.VisitedNodes.Any(n => n.Value == node.Id.Value);
        var current = _run.CurrentNodeId?.Value == node.Id.Value;
        var open = reachable && _onPick is not null;

        StyleBox rest = current ? Halo(Colors.Black, 4)
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

    // WHERE YOU CAN GO glows (the user's call, 2026-11-22; it used to mark where you are, which now wears a plain
    // black ring). On a full drawn plan a thin halo is one ring among many and the eye loses the choice. Behind
    // each open token: an amber light that slowly brightens and dims, and a thin ring that drifts outward from
    // the rim and fades, once per breath; the movement is what the eye finds. Amber rather than the theme's pale
    // gold, which vanishes on the parchment. It sits under the button, so the token and its halo stay as they are.
    private static readonly Color BeaconColor = new("f0a020");
    private const double BeaconBreath = 2.4; // seconds, one bright-dim cycle and one ring

    private static Control Beacon(Vector2 pos)
    {
        const float Pad = 26f;
        var size = NodeD + 2 * Pad;
        var rim = NodeD / 2f + 3;
        var phase = 0f;
        var glow = new Control
        {
            Position = pos - new Vector2(Pad, Pad),
            Size = new Vector2(size, size),
            MouseFilter = MouseFilterEnum.Ignore,
            Name = "OpenRoomGlow",
        };
        glow.Draw += () =>
        {
            var centre = new Vector2(size / 2, size / 2);
            // brightness breathes 0.45..1 on a sine
            var breath = 0.45f + 0.55f * (0.5f - 0.5f * Mathf.Cos(phase * Mathf.Tau));
            // the light: stacked circles from Pad-wide out to the rim, denser near the token
            const int Steps = 16;
            for (var i = 0; i < Steps; i++)
            {
                var t = i / (Steps - 1f);
                var r = rim + (Pad - 4) * (1 - t);
                glow.DrawCircle(centre, r, new Color(BeaconColor, 0.07f * breath), antialiased: true);
            }
            // the ring drifting outward, fading as it goes
            var ring = rim + (Pad - 2) * phase;
            glow.DrawArc(centre, ring, 0, Mathf.Tau, 64, new Color(BeaconColor, 0.85f * (1 - phase)), 2.5f, antialiased: true);
        };
        glow.Ready += () =>
        {
            var tween = glow.CreateTween().SetLoops();
            tween.TweenMethod(Callable.From<float>(p => { phase = p; glow.QueueRedraw(); }), 0f, 1f, BeaconBreath);
        };
        return glow;
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
    // Remembered, misses too: every map and every legend asks for each kind again, and asking the filesystem
    // whether a file exists is not free.
    private static readonly System.Collections.Generic.Dictionary<string, Texture2D?> Tokens = [];

    public static Texture2D? Token(string role)
    {
        if (Tokens.TryGetValue(role, out var known))
            return known;
        var path = $"res://assets/map/nodes/{role}.png";
        return Tokens[role] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
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
