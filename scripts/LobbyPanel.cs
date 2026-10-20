using System;
using Godot;

namespace BnbGodot;

// THE TABLE BEFORE THE RUN (multiplayer M5): host one or join one, pick who you play, say you are ready. The host
// starts the run for everyone once every seat is ready. Built in code like every other window here, in the shape of
// NewRunPanel: a dimmed sheet with a panel on it.
//
// What it shows is NetSession's lobby as it stands; every change there redraws it, and the run starting — on any
// machine, the host's press — takes every machine to the run screen.
public partial class LobbyPanel : PanelContainer
{
    public const string OverlayName = "LobbyOverlay";

    private readonly Action _onStarted;
    private readonly Action _onClose;
    private VBoxContainer _column = null!;
    private LineEdit _address = null!;
    private LineEdit _seed = null!;
    private string? _character;
    private bool _ready;

    public LobbyPanel(Action onStarted, Action onClose, string? character)
    {
        _onStarted = onStarted;
        _onClose = onClose;
        _character = character;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(640, 0);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);
        _column = new VBoxContainer();
        _column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(_column);

        var net = NetSession.Instance;
        net.LobbyChanged += Redraw;
        net.StatusChanged += Redraw;
        net.RunStarted += OnRunStarted;
        Redraw();
    }

    public override void _ExitTree()
    {
        var net = NetSession.Instance;
        net.LobbyChanged -= Redraw;
        net.StatusChanged -= Redraw;
        net.RunStarted -= OnRunStarted;
    }

    private void OnRunStarted() => _onStarted();

    private void Redraw()
    {
        foreach (var child in _column.GetChildren())
            child.QueueFree();
        var net = NetSession.Instance;

        var title = new Label { Text = "Play together", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        _column.AddChild(title);

        if (net.Status.Length > 0)
            _column.AddChild(Muted(net.Status));

        if (!net.IsOnline)
            DrawDoor();
        else
            DrawTable(net);
    }

    // Not at a table yet: open one, or name one to join.
    private void DrawDoor()
    {
        _column.AddChild(Muted(
            "Up to four players play one run together, each with their own hero, deck and gold. "
            + "The host's friends join by address; everyone needs the same version of the game."));

        var host = new Button { Text = $"Host a table (port {NetSession.DefaultPort})", CustomMinimumSize = new Vector2(0, 40) };
        host.Pressed += () =>
        {
            NetSession.Instance.Host();
            Redraw();
        };
        _column.AddChild(host);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _address = new LineEdit
        {
            PlaceholderText = "host address, e.g. 192.168.1.20 or 100.64.0.7:24642",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Name = "JoinAddress",
        };
        row.AddChild(_address);
        var join = new Button { Text = "Join", CustomMinimumSize = new Vector2(100, 40) };
        join.Pressed += () =>
        {
            var (address, port) = ParseAddress(_address.Text);
            if (address.Length > 0)
                NetSession.Instance.Join(address, port);
            Redraw();
        };
        row.AddChild(join);
        _column.AddChild(row);

        var close = new Button { Text = "Back", CustomMinimumSize = new Vector2(120, 40) };
        close.Pressed += () => _onClose();
        _column.AddChild(close);
    }

    // At a table: the seats, who you play, ready, and — for the host — the start.
    private void DrawTable(NetSession net)
    {
        var host = GameHost.Instance;
        for (var index = 0; index < net.Seats.Count; index++)
        {
            var seat = net.Seats[index];
            var character = host.Blueprint.Characters.FirstOrDefault(c => c.Id == seat.Character)?.Start.HeroName ?? "—";
            var line = new Label
            {
                Text = $"{index + 1}. {seat.Name}{(index == net.LocalSeat ? " (you)" : "")} — {character}"
                    + $"{(seat.Ready ? "  ✔ ready" : "")}{(seat.Connected ? "" : "  (away)")}",
            };
            line.AddThemeColorOverride("font_color", seat.Ready ? MoonvineTheme.Text : MoonvineTheme.TextSoft);
            _column.AddChild(line);
        }
        if (net.Seats.Count == 0)
            _column.AddChild(Muted("Waiting for the host to let us in …"));

        if (!net.Started && net.LocalSeat >= 0)
        {
            var pick = new HBoxContainer();
            pick.AddThemeConstantOverride("separation", 10);
            pick.AddChild(new Label { Text = "You play" });
            var characters = new OptionButton { Name = "SeatCharacter", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var roster = host.AvailableCharacters;
            for (var i = 0; i < roster.Count; i++)
            {
                characters.AddItem(roster[i].Start.HeroName, i);
                if (roster[i].Id == _character)
                    characters.Select(i);
            }
            _character ??= roster.Count > 0 ? roster[0].Id : null;
            characters.ItemSelected += index =>
            {
                _character = roster[(int)index].Id;
                net.ChooseCharacter(_character, _ready);
            };
            pick.AddChild(characters);
            var ready = new CheckButton { Text = "Ready", ButtonPressed = _ready };
            ready.Toggled += on =>
            {
                _ready = on;
                if (_character is { } chosen)
                    net.ChooseCharacter(chosen, _ready);
            };
            pick.AddChild(ready);
            _column.AddChild(pick);
        }

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        actions.AddThemeConstantOverride("separation", 10);
        var leave = new Button { Text = "Leave the table", CustomMinimumSize = new Vector2(150, 40) };
        leave.Pressed += () =>
        {
            NetSession.Instance.Leave();
            Redraw();
        };
        actions.AddChild(leave);

        if (net.Role == NetSession.NetRole.Host)
        {
            _seed = new LineEdit { PlaceholderText = "seed (random)", CustomMinimumSize = new Vector2(140, 0), Name = "TableSeed" };
            actions.AddChild(_seed);
            var start = new Button { Text = "Start the run ▸", CustomMinimumSize = new Vector2(160, 40), Disabled = !net.CanStart };
            start.TooltipText = net.CanStart ? "Everyone is ready." : "Every seat must pick a hero and be ready.";
            start.Pressed += () => net.StartRun(RunSeeds.Parse(_seed.Text) ?? (int)(Time.GetTicksUsec() % int.MaxValue));
            actions.AddChild(start);
        }
        _column.AddChild(actions);
    }

    private static (string Address, int Port) ParseAddress(string typed)
    {
        var text = typed.Trim();
        var colon = text.LastIndexOf(':');
        return colon > 0 && int.TryParse(text[(colon + 1)..], out var port)
            ? (text[..colon], port)
            : (text, NetSession.DefaultPort);
    }

    private static Label Muted(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }

    public static void Open(Godot.Node screen, Action onStarted, string? character)
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
        center.AddChild(new LobbyPanel(
            () =>
            {
                screen.GetNodeOrNull(OverlayName)?.QueueFree();
                onStarted();
            },
            () => screen.GetNodeOrNull(OverlayName)?.QueueFree(),
            character)
        { Name = nameof(LobbyPanel) });
        veil.AddChild(center);
        screen.AddChild(veil);
    }
}
