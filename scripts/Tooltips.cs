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
// under the pointer, or the nearest ancestor that has any — after asking any IHoverText on the way (a card,
// which knows which of its drawn words the pointer is on).
//
// ★ THE TERMS IN A TOOLTIP ARE TINTED, AND A TOOLTIP THAT HAS ANY CAN BE REACHED (2026-11-25). A relic's hover
// no longer spells out every term its rules use; it tints them (Keywords.Mark). Such a tooltip stops following
// the pointer once it is up and stays while the pointer travels onto it, and a term inside it is explained in a
// second small panel beside it while the pointer is on that term.
public partial class Tooltips : CanvasLayer
{
    private const float Delay = 0.45f;
    private const float MaxWidth = 380f;
    private const float Gap = 16f;
    // How long a reachable tooltip waits for a pointer that has left its source on the way to it.
    private const float Grace = 0.35f;

    private PanelContainer _panel = null!;
    private Label _title = null!;
    private RichTextLabel _body = null!;
    private PanelContainer _sub = null!;
    private RichTextLabel _subBody = null!;
    private Control? _source;
    private string _text = "";
    private float _hovered;
    private bool _reachable;
    private float _away;
    private Vector2 _anchor;

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
        _body.MetaUnderlined = false;
        _body.MetaHoverStarted += meta => ShowSub(Glossary.Term(Keywords.TermOf(meta)));
        _body.MetaHoverEnded += _ => _sub.Visible = false;
        column.AddChild(_title);
        column.AddChild(_body);
        _panel.AddChild(column);
        AddChild(_panel);

        // The second panel: one term, explained, beside the first. It is never reached itself.
        _sub = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Theme = _panel.Theme };
        var subBox = MoonvineTheme.Panel(MoonvineTheme.BgRaised, MoonvineTheme.AccentDark);
        subBox.ContentMarginLeft = subBox.ContentMarginRight = 10;
        subBox.ContentMarginTop = subBox.ContentMarginBottom = 6;
        _sub.AddThemeStyleboxOverride("panel", subBox);
        _subBody = new RichTextLabel
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
        };
        _subBody.AddThemeColorOverride("default_color", MoonvineTheme.TextSoft);
        _subBody.AddThemeFontSizeOverride("normal_font_size", 13);
        _sub.AddChild(_subBody);
        AddChild(_sub);
    }

    public override void _Process(double delta)
    {
        var hovered = GetViewport().GuiGetHoveredControl();
        // On the reachable tooltip itself: it stays, and its own terms answer the pointer.
        if (_panel.Visible && _reachable && hovered is not null && (hovered == _panel || _panel.IsAncestorOf(hovered)))
        {
            _away = 0;
            return;
        }
        var (source, text) = UnderPointer(hovered);
        if (source != _source || text != _text)
        {
            // A reachable tooltip gives the pointer a moment to get to it before it lets go.
            if (_panel.Visible && _reachable)
            {
                _away += (float)delta;
                if (_away < Grace)
                    return;
            }
            _source = source;
            _text = text;
            _hovered = 0;
            _away = 0;
            HidePanels();
        }
        _away = 0;
        if (_source is null || _text.Length == 0)
            return;
        _hovered += (float)delta;
        if (_hovered < Delay)
            return;
        if (!_panel.Visible)
        {
            _anchor = GetViewport().GetMousePosition();
            Show(_text);
        }
        // A plain tooltip follows the pointer; a reachable one keeps the place it opened at (its size may still
        // settle over the first frames, so it is re-placed around the same point).
        Place(_reachable ? _anchor : GetViewport().GetMousePosition());
    }

    private void HidePanels()
    {
        _panel.Visible = false;
        _sub.Visible = false;
        _reachable = false;
        _panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _body.MouseFilter = Control.MouseFilterEnum.Ignore;
    }

    private (Control? Source, string Text) UnderPointer(Control? hovered)
    {
        var mouse = GetViewport().GetMousePosition();
        for (Node? node = hovered; node is Control control; node = control.GetParent())
        {
            if (!control.IsVisibleInTree())
                return (null, "");
            // A card answers for the whole of itself: the term under the pointer, or nothing.
            if (control is IHoverText speaker)
            {
                var said = speaker.HoverTextAt(mouse).Trim();
                return said.Length > 0 ? (control, said) : (null, "");
            }
            if (!string.IsNullOrWhiteSpace(control.TooltipText))
                return (control, control.TooltipText.Trim());
        }
        return (null, "");
    }

    // One term's explanation, beside the tooltip on whichever side has room, level with the pointer.
    private void ShowSub(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _sub.Visible = false;
            return;
        }
        var head = text.IndexOf(" — ", StringComparison.Ordinal);
        var accent = MoonvineTheme.AccentLight.ToHtml(false);
        _subBody.Text = head is > 0 and < 40
            ? $"[color=#{accent}]{Keywords.Escape(text[..head])}[/color]{Keywords.Mark(text[head..], links: false)}"
            : Keywords.Mark(text, links: false);
        var font = _title.GetThemeFont("font");
        var width = Math.Min(MaxWidth - 40, font.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X + 8);
        _subBody.CustomMinimumSize = new Vector2(width, 0);
        _sub.ResetSize();
        var size = _sub.GetCombinedMinimumSize();
        _sub.Size = size;
        var screen = GetViewport().GetVisibleRect().Size;
        var mouse = GetViewport().GetMousePosition();
        var x = _panel.Position.X + _panel.Size.X + 6;
        if (x + size.X > screen.X - 4)
            x = Math.Max(4, _panel.Position.X - 6 - size.X);
        var y = Math.Clamp(mouse.Y - size.Y / 2, 4, Math.Max(4, screen.Y - 4 - size.Y));
        _sub.Position = new Vector2(x, y);
        _sub.Visible = true;
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
        _body.Text = Marked(body, title);
        _body.Visible = body.Length > 0;
        // A tooltip with terms in it can be reached: it holds still, and catches the pointer to explain them.
        _reachable = _body.Text.Contains("[url=", StringComparison.Ordinal);
        _panel.MouseFilter = _reachable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        _body.MouseFilter = _reachable ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;

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
    // so it is set in the accent; every other term the line uses is tinted and can be hovered (Keywords) — but
    // not the line's own name, nor the tooltip's title, which would only explain themselves.
    private static string Marked(string body, string title)
    {
        var accent = MoonvineTheme.AccentLight.ToHtml(false);
        var own = title.Length > 0 ? title : null;
        return string.Join("\n", body.Split('\n').Select(line =>
        {
            var dash = line.IndexOf(" — ", StringComparison.Ordinal);
            return dash is > 0 and < 40
                ? $"[color=#{accent}]{Keywords.Escape(line[..dash])}[/color]{Keywords.Mark(line[dash..], skip: line[..dash].Trim())}"
                : Keywords.Mark(line, skip: own);
        }));
    }

    // Below and to the right of the pointer, flipped to the other side of it rather than cut by an edge.
    private void Place(Vector2 mouse)
    {
        var screen = GetViewport().GetVisibleRect().Size;
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
