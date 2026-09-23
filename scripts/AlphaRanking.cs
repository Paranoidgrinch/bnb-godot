using System.Text.Json;
using Godot;

namespace BnbGodot;

// THE CLOSED-ALPHA RANKING on the title screen: every alpha player's totals, from bnb-runs' leaderboard.json
// (rebuilt hourly by its ingest workflow from every run the game has sent home). Five boards — runs, elites,
// bosses, enemies, wins — the top ten on each, and the player's own line always shown, highlighted, even when
// they are not in it: the point is to make somebody want to play one more.
//
// Fetched once per title screen; the last good copy is kept in `user://leaderboard-cache.json`, so an offline
// player still sees the board (marked as such) instead of an empty box.
public partial class AlphaRanking : PanelContainer
{
    private const string Url = "https://raw.githubusercontent.com/Paranoidgrinch/bnb-runs/main/leaderboard.json";
    private const string CachePath = "user://leaderboard-cache.json";
    private const int Shown = 10;

    private static readonly (string Key, string Tab, string Title)[] Boards =
    [
        ("runs", "Runs", "Runs started"),
        ("elites", "Elites", "Elites killed"),
        ("bosses", "Bosses", "Bosses killed"),
        ("enemies", "Enemies", "Enemies killed"),
        ("wins", "Wins", "Wins"),
    ];

    private static int _board;
    private List<Entry> _players = [];
    private string _status = "loading…";
    private VBoxContainer _rows = null!;
    private Label _title = null!;
    private Label _footer = null!;

    internal string Status => _status;
    internal int Players => _players.Count;

    private sealed class Entry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Dictionary<string, int> Values { get; } = new(StringComparer.Ordinal);
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(262, 0);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 12);
        AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        margin.AddChild(column);

        var head = new Label { Text = "CLOSED ALPHA RANKING", HorizontalAlignment = HorizontalAlignment.Center };
        head.AddThemeFontSizeOverride("font_size", 13);
        head.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        column.AddChild(head);

        var tabs = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.Center };
        tabs.AddThemeConstantOverride("h_separation", 3);
        tabs.AddThemeConstantOverride("v_separation", 3);
        var group = new ButtonGroup();
        for (var i = 0; i < Boards.Length; i++)
        {
            var index = i;
            var tab = new Button
            {
                Text = Boards[i].Tab,
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = i == _board,
                Name = $"Tab{Boards[i].Tab}",
            };
            tab.AddThemeFontSizeOverride("font_size", 12);
            tab.Pressed += () =>
            {
                _board = index;
                Fill();
            };
            tabs.AddChild(tab);
        }
        column.AddChild(tabs);

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 12);
        _title.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        column.AddChild(_title);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 1);
        column.AddChild(_rows);
        _footer = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _footer.AddThemeFontSizeOverride("font_size", 11);
        _footer.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        column.AddChild(_footer);

        if (Godot.FileAccess.FileExists(CachePath) && Parse(Godot.FileAccess.GetFileAsString(CachePath)) is { } cached)
        {
            _players = cached.Players;
            _status = $"offline copy · {cached.Stamp}";
        }
        Fill();
        _ = Fetch();
    }

    private async System.Threading.Tasks.Task Fetch()
    {
        var request = new HttpRequest { Timeout = 15 };
        AddChild(request);
        try
        {
            if (request.Request(Url) != Error.Ok)
                return;
            var done = await ToSignal(request, HttpRequest.SignalName.RequestCompleted);
            if ((HttpRequest.Result)(int)done[0] != HttpRequest.Result.Success || (int)done[1] != 200)
                return;
            var text = System.Text.Encoding.UTF8.GetString((byte[])done[3]);
            if (Parse(text) is not { } fresh)
                return;
            _players = fresh.Players;
            _status = $"updated hourly · {fresh.Stamp}";
            using var file = Godot.FileAccess.Open(CachePath, Godot.FileAccess.ModeFlags.Write);
            file?.StoreString(text);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"alpha ranking: {ex.Message}");
        }
        finally
        {
            request.QueueFree();
            if (IsInstanceValid(this))
                Fill();
        }
    }

    private static (List<Entry> Players, string Stamp)? Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var players = new List<Entry>();
            foreach (var row in document.RootElement.GetProperty("players").EnumerateArray())
            {
                var entry = new Entry
                {
                    Id = row.GetProperty("id").GetString() ?? "",
                    Name = row.GetProperty("name").GetString() ?? "?",
                };
                foreach (var (key, _, _) in Boards)
                    entry.Values[key] = row.TryGetProperty(key, out var value) && value.TryGetInt32(out var n) ? n : 0;
                players.Add(entry);
            }
            var stamp = document.RootElement.TryGetProperty("generated_utc", out var at)
                && DateTime.TryParse(at.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var when)
                ? when.ToLocalTime().ToString("dd.MM. HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                : "—";
            return (players, stamp);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Fill()
    {
        foreach (var child in _rows.GetChildren())
            child.QueueFree();
        var (key, _, title) = Boards[_board];
        _title.Text = title;
        _footer.Text = _status;
        var ranked = _players
            .OrderByDescending(p => p.Values.GetValueOrDefault(key))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ranked.Count == 0)
        {
            _rows.AddChild(Line("", _status == "loading…" ? "…" : "No runs yet — be the first.", "", false));
            return;
        }
        var me = PlayerIdentity.Id;
        for (var i = 0; i < ranked.Count && i < Shown; i++)
            _rows.AddChild(Line($"{i + 1}.", ranked[i].Name, ranked[i].Values.GetValueOrDefault(key).ToString(),
                ranked[i].Id == me));
        var mine = ranked.FindIndex(p => p.Id == me);
        if (mine >= Shown)
        {
            _rows.AddChild(Line("", "…", "", false));
            _rows.AddChild(Line($"{mine + 1}.", ranked[mine].Name, ranked[mine].Values.GetValueOrDefault(key).ToString(), true));
        }
    }

    private static Control Line(string place, string name, string value, bool mine)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var colour = mine ? MoonvineTheme.Signal : MoonvineTheme.TextSoft;
        Label Cell(string text, float width, bool grow, HorizontalAlignment align)
        {
            var label = new Label
            {
                Text = text,
                CustomMinimumSize = new Vector2(width, 0),
                SizeFlagsHorizontal = grow ? SizeFlags.ExpandFill : SizeFlags.Fill,
                HorizontalAlignment = align,
                ClipText = true,
            };
            label.AddThemeFontSizeOverride("font_size", 13);
            label.AddThemeColorOverride("font_color", colour);
            return label;
        }
        row.AddChild(Cell(place, 26, false, HorizontalAlignment.Right));
        row.AddChild(Cell(name, 0, true, HorizontalAlignment.Left));
        row.AddChild(Cell(value, 44, false, HorizontalAlignment.Right));
        return row;
    }
}
