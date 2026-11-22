using Godot;

namespace BnbGodot;

// ── WHAT A RUN PAID, AS A BAR THAT FILLS (user, 2026-11-22: "eine Art XP-Bar … und nicht nur eine Zahl") ──────
// The end-of-run sheet showed "+43 XP" and nothing else. This shows the character's level bar as it stood when
// the run began, then fills it by what the run paid: where a level is crossed the bar fills to the end, the level
// number steps up with a flash, and the bar starts again from empty with what is left. Under it, where the XP came
// from (rooms, elites, bosses, the win), so the number on the right is a sum the player can read.
public partial class ExperienceBar : VBoxContainer
{
    private readonly ProgressionDefinition _definition;
    private readonly XpGain _gain;
    private readonly int _xpAfter;

    private ProgressBar _bar = null!;
    private Label _level = null!;
    private Label _count = null!;
    private Label _news = null!;

    // Seconds the whole fill takes, however many levels it crosses — a long run must not keep the sheet waiting.
    private const double FillSeconds = 1.6;

    public ExperienceBar(ProgressionDefinition definition, XpGain gain, int xpAfter)
    {
        _definition = definition;
        _gain = gain;
        _xpAfter = xpAfter;
        Name = "ExperienceBar";
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 14);
        AddChild(top);
        _level = new Label { CustomMinimumSize = new Vector2(96, 0), VerticalAlignment = VerticalAlignment.Center };
        _level.AddThemeFontSizeOverride("font_size", 18);
        _level.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        top.AddChild(_level);

        _bar = new ProgressBar
        {
            Name = "XpFill", MinValue = 0, ShowPercentage = false, Step = 0.01,
            CustomMinimumSize = new Vector2(380, 22), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        var fill = new StyleBoxFlat { BgColor = MoonvineTheme.Accent };
        fill.SetCornerRadiusAll(5);
        var track = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f), BorderColor = MoonvineTheme.GoldDim };
        track.SetBorderWidthAll(1);
        track.SetCornerRadiusAll(5);
        _bar.AddThemeStyleboxOverride("fill", fill);
        _bar.AddThemeStyleboxOverride("background", track);
        top.AddChild(_bar);

        _count = new Label { CustomMinimumSize = new Vector2(150, 0), VerticalAlignment = VerticalAlignment.Center };
        _count.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        top.AddChild(_count);

        var sum = new Label { Text = Breakdown(), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        sum.AddThemeFontSizeOverride("font_size", 13);
        sum.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        AddChild(sum);

        _news = new Label { Name = "XpNews", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _news.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        AddChild(_news);

        Animate();
    }

    // "14 rooms × 1 · 1 elite × 3 · …  = +17 XP"
    private string Breakdown()
    {
        var xp = _definition.Xp;
        var parts = new List<string> { $"{_gain.Rooms} room{(_gain.Rooms == 1 ? "" : "s")} × {xp.Room}" };
        if (_gain.Elites > 0)
            parts.Add($"{_gain.Elites} elite{(_gain.Elites == 1 ? "" : "s")} × {xp.Elite}");
        if (_gain.Bosses > 0)
            parts.Add($"{_gain.Bosses} boss{(_gain.Bosses == 1 ? "" : "es")} × {xp.Boss}");
        if (_gain.Won)
            parts.Add($"the win {xp.Win}");
        return $"{string.Join("  ·  ", parts)}   =  +{_gain.Total} XP";
    }

    // The bar as it stands at `xp` total: the level, how far into it, and what the level costs (0 at the top).
    private (int Level, int Into, int Needed) At(int xp)
    {
        var level = CardProgression.LevelOf(_definition, xp);
        var (into, needed) = CardProgression.Progress(_definition, xp);
        return (level, into, needed);
    }

    private void Show(double xp)
    {
        var (level, into, needed) = At((int)Math.Floor(xp));
        var fraction = xp - Math.Floor(xp);
        _level.Text = $"Level {level}";
        if (needed == 0)
        {
            _bar.MaxValue = 1;
            _bar.Value = 1;
            _count.Text = "top level";
            return;
        }
        _bar.MaxValue = needed;
        _bar.Value = Math.Min(needed, into + fraction);
        _count.Text = $"{into} / {needed} XP";
    }

    private void Animate()
    {
        var before = Math.Max(0, _xpAfter - _gain.Total);
        Show(before);
        if (_gain.Total <= 0)
            return;
        var levelShown = At(before).Level;
        var tween = CreateTween();
        tween.TweenInterval(0.35);
        tween.TweenMethod(Callable.From<double>(xp =>
        {
            Show(xp);
            var level = At((int)Math.Floor(xp)).Level;
            if (level > levelShown)
            {
                levelShown = level;
                Flash(level);
            }
        }), (double)before, (double)_xpAfter, FillSeconds).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenCallback(Callable.From(() => Show(_xpAfter)));
    }

    // A level crossed: the number pulses and the line under the bar says what it brought.
    private void Flash(int level)
    {
        _level.PivotOffset = _level.Size / 2;
        var pulse = CreateTween();
        pulse.TweenProperty(_level, "scale", new Vector2(1.35f, 1.35f), 0.12);
        pulse.TweenProperty(_level, "scale", Vector2.One, 0.2);
        var booster = _definition.Boosters.FirstOrDefault(b => b.Level == level);
        var line = booster is null
            ? $"Level {level}!"
            : $"Level {level}! Drawer {level} · {booster.Name} waits, sealed, in the Safe Deposit Box.";
        _news.Text = _news.Text.Length == 0 ? line : _news.Text + "\n" + line;
    }
}
