using Godot;

namespace BnbGodot;

// THE ONE TOOLTIP. Every hover in this game still says what it says through `Control.TooltipText` — the glossary
// writes it, the tooltip audit (`--smoke-tooltips`) reads it back — but Godot's own tooltip draws that text as a
// single unwrapped line, and a status rule or a relic's text is a paragraph: it ran across the screen and off
// its right edge, where the end of the sentence could not be read (playtest 2026-09-26).
//
// So Godot's tooltip is switched off (`gui/timers/tooltip_delay_sec` in project.godot) and this autoload draws
// the same text instead: a panel of bounded width, word-wrapped, the first line set as a title when the text has
// more than one, and placed so it never leaves the window. It finds the text the way Godot does — the control
// under the pointer, or the nearest ancestor that has any.
public partial class Tooltips : CanvasLayer
{
    private const float Delay = 0.45f;
    private const float MaxWidth = 380f;
    private const float Gap = 16f;

    private PanelContainer _panel = null!;
    private Label _title = null!;
    private RichTextLabel _body = null!;
    private Control? _source;
    private string _text = "";
    private float _hovered;

    public override void _Ready()
    {
        Layer = 128; // over every overlay a screen can open
        _panel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.Theme = MoonvineTheme.Build();
        var box = MoonvineTheme.Panel(MoonvineTheme.BgRaised, MoonvineTheme.Accent);
        box.ContentMarginLeft = box.ContentMarginRight = 12;
        box.ContentMarginTop = box.ContentMarginBottom = 8;
        _panel.AddThemeStyleboxOverride("panel", box);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 4);
        _title = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _title.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
        _title.AddThemeFontSizeOverride("font_size", 15);
        _body = new RichTextLabel
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
        };
        _body.AddThemeColorOverride("default_color", MoonvineTheme.TextSoft);
        _body.AddThemeFontSizeOverride("normal_font_size", 14);
        column.AddChild(_title);
        column.AddChild(_body);
        _panel.AddChild(column);
        AddChild(_panel);
    }

    public override void _Process(double delta)
    {
        var (source, text) = UnderPointer();
        if (source != _source || text != _text)
        {
            _source = source;
            _text = text;
            _hovered = 0;
            _panel.Visible = false;
        }
        if (_source is null || _text.Length == 0)
            return;
        _hovered += (float)delta;
        if (_hovered < Delay)
            return;
        if (!_panel.Visible)
            Show(_text);
        Place();
    }

    private (Control? Source, string Text) UnderPointer()
    {
        var viewport = GetViewport();
        for (Node? node = viewport.GuiGetHoveredControl(); node is Control control; node = control.GetParent())
        {
            if (!control.IsVisibleInTree())
                return (null, "");
            if (!string.IsNullOrWhiteSpace(control.TooltipText))
                return (control, control.TooltipText.Trim());
        }
        return (null, "");
    }

    // A text of several lines has a name on its first: "Seal\nAt 3 Seal, …". One line is all body, and so is a
    // first line that names nothing (a card's "⚡1").
    private void Show(string text)
    {
        var split = text.IndexOf('\n');
        var first = split > 0 ? text[..split].Trim() : "";
        var named = first.Any(char.IsLetter);
        var title = named ? first : "";
        var body = named ? text[(split + 1)..].Trim() : text;
        _title.Text = title;
        _title.Visible = title.Length > 0;
        _body.Text = Marked(body);
        _body.Visible = body.Length > 0;

        // Wrap at the bound, but a short text keeps its own width: ask the font how wide the longest line is.
        var font = _title.GetThemeFont("font");
        var widest = text.Split('\n')
            .Select(l => font.GetStringSize(l, HorizontalAlignment.Left, -1, 15).X)
            .DefaultIfEmpty(0).Max();
        var width = Math.Min(MaxWidth, widest + 8);
        _title.CustomMinimumSize = new Vector2(width, 0);
        _body.CustomMinimumSize = new Vector2(width, 0);
        _panel.ResetSize();
        _panel.Visible = true;
    }

    // The glossary writes a term as "Name — what it does", one per line. The name is what the eye looks for,
    // so it is set in the accent; everything else is plain text (BBCode in the source is escaped first).
    private static string Marked(string body)
    {
        var accent = MoonvineTheme.AccentLight.ToHtml(false);
        return string.Join("\n", body.Split('\n').Select(line =>
        {
            var safe = line.Replace("[", "[lb]");
            var dash = safe.IndexOf(" — ", StringComparison.Ordinal);
            return dash is > 0 and < 40
                ? $"[color=#{accent}]{safe[..dash]}[/color]{safe[dash..]}"
                : safe;
        }));
    }

    // Below and to the right of the pointer, flipped to the other side of it rather than cut by an edge.
    private void Place()
    {
        var screen = GetViewport().GetVisibleRect().Size;
        var mouse = GetViewport().GetMousePosition();
        var size = _panel.GetCombinedMinimumSize();
        _panel.Size = size;
        var x = mouse.X + Gap;
        if (x + size.X > screen.X - 4)
            x = Math.Max(4, mouse.X - Gap - size.X);
        var y = mouse.Y + Gap;
        if (y + size.Y > screen.Y - 4)
            y = Math.Max(4, mouse.Y - Gap - size.Y);
        _panel.Position = new Vector2(x, y);
    }
}
