using Godot;

namespace BnbGodot;

// THE OPENING. Three beats over black, each one fading out before the next begins: the studio, the game, the
// title screen. It is the first thing the game says, and until now the first thing it said was Godot's.
//
// ⚠ IT IS AN OVERLAY ON THE TITLE SCREEN, NOT A SCENE OF ITS OWN. Every `--smoke*` probe boots through
// `Boot`, which routes on its command line; a splash scene in front of that would have to learn every one of
// those arguments, and the one thing worse than a probe that fails is a probe that photographs a fade. So the
// title screen builds as it always did and this hangs over it — and `Boot` never even creates it for a probe.
//
// ⚠ AND IT IS SKIPPABLE, WHICH IS NOT A COURTESY. A playtest starts this game dozens of times a day. An
// opening nobody can dismiss is an opening everybody learns to hate; any key or click takes the whole thing
// down at once.
public partial class Splash : ColorRect
{
    // How long each beat stands before it goes. Short: this is a signature, not a film.
    private const double Hold = 1.5;
    private const double Fade = 0.7;

    private Action? _onDone;
    private Tween? _reel;
    private bool _over;

    public static Splash Play(Node host, Action onDone)
    {
        var splash = new Splash
        {
            Color = MoonvineTheme.Bg,
            MouseFilter = MouseFilterEnum.Stop, // the opening swallows clicks meant for the screen beneath it
        };
        splash.SetAnchorsPreset(LayoutPreset.FullRect);
        splash._onDone = onDone;
        host.AddChild(splash);
        splash.MoveToFront();
        return splash;
    }

    public override void _Ready()
    {
        var studio = Beat(Studio());
        var game = Beat(Title());

        _reel = CreateTween();
        _reel.TweenProperty(studio, "modulate:a", 1.0f, Fade);
        _reel.TweenInterval(Hold);
        _reel.TweenProperty(studio, "modulate:a", 0.0f, Fade);
        _reel.TweenProperty(game, "modulate:a", 1.0f, Fade);
        _reel.TweenInterval(Hold);
        _reel.TweenProperty(game, "modulate:a", 0.0f, Fade);
        // The last fade is the ground's own: black over the title screen, lifting away.
        _reel.TweenProperty(this, "modulate:a", 0.0f, Fade);
        _reel.TweenCallback(Callable.From(Done));

        SetProcessInput(true);
    }

    // Anything at all ends it. A key, a button, a click — the player has seen this before.
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true }
            or InputEventJoypadButton { Pressed: true })
        {
            GetViewport().SetInputAsHandled();
            Done();
        }
    }

    private void Done()
    {
        if (_over)
            return; // the tween's callback and a keypress can arrive in the same frame
        _over = true;
        _reel?.Kill();
        var done = _onDone;
        _onDone = null;
        QueueFree();
        done?.Invoke();
    }

    // One beat: a column centred on the page, invisible until its turn comes.
    private Control Beat(Control content)
    {
        content.Modulate = new Color(1, 1, 1, 0);
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(content);
        return content;
    }

    private static Control Studio()
    {
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 22);

        // ⚠ A MISSING LOGO IS A NORMAL STATE. Every picture in this game may be absent — the shelf draws a
        // slot code, a card draws an empty socket — and the studio's mark is the one picture that is not ours
        // to draw at all. Without the file the words carry the beat by themselves.
        if (MoonvineTheme.Brand("moonvine-forge") is { } logo)
        {
            var mark = new TextureRect
            {
                Texture = logo,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
                CustomMinimumSize = new Vector2(0, 220),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            column.AddChild(mark);
        }

        column.AddChild(Line("Moonvine Forge Studios presents", 22, MoonvineTheme.TextSoft));
        return column;
    }

    private static Control Title()
    {
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 10);
        // The game's own name, in the game's own gold. Whatever the document calls itself — a frontend that
        // hardcodes the title is a frontend that belongs to one game.
        column.AddChild(Line(GameHost.Instance.GameTitle, 46, MoonvineTheme.Accent));
        return column;
    }

    private static Label Line(string text, int size, Color colour)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }
}
