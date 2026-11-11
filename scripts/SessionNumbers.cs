using Godot;

namespace BnbGodot;

// ── THE NUMBERS THAT FLY OFF A BODY (user, 2026-11-11) ────────────────────────────
// A wound is a number in red ink with ink and blood flung round it: it lands with a punch (big, then springing
// back), tilted a little, and flies up and to the side in an arc, slowing as it goes, its red drying to dark ink.
// Its size says how much of the body's health it took; a blow that takes a quarter or more shakes the screen, and
// the one that kills is bigger again and stays a moment longer. The hits of one card on one body are ONE number
// that counts up (−6, −12, −18) rather than three in the air.
//
// Block is a steel seal with its number on it: "+N" when it is raised, and when it catches a blow the seal shows
// what it caught, cracks and falls apart in two. Healing rises in green ink with a few sparks round it.
// The bar under the body keeps its own account: what a blow took stays on it, pale, for a moment before it
// drains away, and the block on the bar is a steel plate whose broken piece falls off when a blow chips it.
public partial class SessionScreen
{
    private static readonly Color SealSteel = new("6f8fa8");
    private static readonly Color SealRim = new("c9dae6");

    // One blow of a step's tally on a body: when it lands, how much it took, and whether it was the last.
    private readonly record struct Blow(double At, int Amount, bool Lethal);

    // Where a body's numbers start: a little above its middle.
    private async System.Threading.Tasks.Task<Vector2?> BodyPoint(string id, double delay)
    {
        if (await BodyAfter(id, delay) is not { } figure)
            return null;
        var rect = figure.GetGlobalRect();
        return EffectLayer().GetGlobalTransform().AffineInverse() * (rect.Position + new Vector2(rect.Size.X / 2, rect.Size.Y * 0.32f));
    }

    private int MaxHealthOf(string id) => _healthBars.TryGetValue(id, out var bar) && bar.Max > 0 ? bar.Max : 50;

    // ── a wound ─────────────────────────────────────────────────────────────────

    private async void DamageTally(string id, List<Blow> blows, int line)
    {
        if (blows.Count == 0 || await BodyPoint(id, blows[0].At) is not { } origin)
            return;
        var max = MaxHealthOf(id);
        var number = new InkNumber(InkNumber.Kind.Wound, line);
        EffectLayer().AddChild(number);
        number.Position = origin;
        var total = 0;
        var started = blows[0].At;
        foreach (var blow in blows)
        {
            if (blow.At > started)
                await ToSignal(GetTree().CreateTimer(blow.At - started), SceneTreeTimer.SignalName.Timeout);
            started = blow.At;
            if (!IsInstanceValid(number))
                return;
            total += blow.Amount;
            var share = total / (float)max;
            number.Hit($"−{total}", share, blow.Lethal);
            if (blow.Amount / (float)max >= 0.25f || blow.Lethal)
                Shake(blow.Lethal ? 9 : 6);
        }
        number.Fly();
    }

    // ── block ───────────────────────────────────────────────────────────────────

    private async void Seal(string id, string text, bool breaks, double delay, int line)
    {
        if (await BodyPoint(id, delay) is not { } origin)
            return;
        var seal = new InkNumber(breaks ? InkNumber.Kind.SealBreaks : InkNumber.Kind.Seal, line);
        EffectLayer().AddChild(seal);
        seal.Position = origin + new Vector2(0, 30);
        seal.Hit(text, 0, false);
        seal.Fly();
    }

    // ── healing ─────────────────────────────────────────────────────────────────

    private async void Mend(string id, int amount, double delay, int line)
    {
        if (await BodyPoint(id, delay) is not { } origin)
            return;
        var number = new InkNumber(InkNumber.Kind.Heal, line);
        EffectLayer().AddChild(number);
        number.Position = origin;
        number.Hit($"+{amount}", amount / (float)MaxHealthOf(id), false);
        number.Fly();
    }

    // ── the bar ─────────────────────────────────────────────────────────────────

    // What the blows took stays on the bar, pale, until a moment after the last of them, then drains away.
    private async void BarTrail(string id, int healthBefore, double lastAt)
    {
        if (await BodyAfter(id, 0, bar: true) is not { } bar)
            return;
        var max = MaxHealthOf(id);
        var fill = bar.GetChildren().OfType<ColorRect>().FirstOrDefault(c => c.Name == "Fill");
        if (fill is null)
            return;
        var from = Mathf.Clamp(healthBefore / (float)max, 0, 1);
        if (from <= fill.AnchorRight + 0.001f)
            return;
        var trail = new ColorRect { Name = "Trail", Color = new Color("f2d6b8"), MouseFilter = MouseFilterEnum.Ignore };
        trail.SetAnchorsPreset(LayoutPreset.FullRect);
        trail.AnchorLeft = fill.AnchorRight;
        trail.AnchorRight = from;
        trail.OffsetLeft = trail.OffsetRight = 0;
        bar.AddChild(trail);
        bar.MoveChild(trail, fill.GetIndex());   // under the fill, over the empty track
        var tween = trail.CreateTween();
        tween.TweenInterval(lastAt + 0.45);
        tween.TweenProperty(trail, "anchor_right", fill.AnchorRight, 0.55).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        tween.Parallel().TweenProperty(trail, "color", new Color("a8382c"), 0.55);
        tween.TweenCallback(Callable.From(trail.QueueFree));
    }

    // The broken piece of the bar's steel plate falls off when a blow chips the block.
    private async void BarChip(string id, int blocked, int blockAfter, double delay)
    {
        if (await BodyAfter(id, delay, bar: true) is not { } bar)
            return;
        var max = MaxHealthOf(id);
        var fill = bar.GetChildren().OfType<ColorRect>().FirstOrDefault(c => c.Name == "Fill");
        if (fill is null)
            return;
        var right = fill.AnchorRight;
        var w = bar.Size.X;
        var x1 = right * w - Mathf.Min(blockAfter / (float)max, right) * w;
        var x0 = Mathf.Max(0, x1 - blocked / (float)max * w);
        if (x1 - x0 < 1)
            return;
        var layer = EffectLayer();
        var at = layer.GetGlobalTransform().AffineInverse() * bar.GetGlobalRect().Position;
        var piece = new ColorRect
        {
            Color = new Color(SealSteel, 0.95f),
            Position = at + new Vector2(x0, 0),
            Size = new Vector2(x1 - x0, bar.Size.Y),
            MouseFilter = MouseFilterEnum.Ignore,
            PivotOffset = new Vector2((x1 - x0) / 2, bar.Size.Y / 2),
        };
        layer.AddChild(piece);
        var rng = new RandomNumberGenerator();
        var tween = piece.CreateTween().SetParallel();
        tween.TweenProperty(piece, "position", piece.Position + new Vector2(rng.RandfRange(-30, 30), 46), 0.6)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.TweenProperty(piece, "rotation", rng.RandfRange(-0.8f, 0.8f), 0.6);
        tween.TweenProperty(piece, "modulate:a", 0f, 0.6).SetDelay(0.15);
        tween.Chain().TweenCallback(Callable.From(piece.QueueFree));
        for (var i = 0; i < 7; i++)
        {
            var chip = new ColorRect
            {
                Color = SealRim,
                Size = new Vector2(rng.RandfRange(2, 4), rng.RandfRange(2, 4)),
                Position = at + new Vector2(rng.RandfRange(x0, x1), rng.RandfRange(0, bar.Size.Y)),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            layer.AddChild(chip);
            var t = chip.CreateTween().SetParallel();
            t.TweenProperty(chip, "position", chip.Position + new Vector2(rng.RandfRange(-40, 40), rng.RandfRange(-30, 20)), 0.45)
                .SetEase(Tween.EaseType.Out);
            t.TweenProperty(chip, "modulate:a", 0f, 0.45);
            t.Chain().TweenCallback(Callable.From(chip.QueueFree));
        }
    }

    // ── the screen ──────────────────────────────────────────────────────────────

    private Tween? _shake;

    private void Shake(float strength)
    {
        _shake?.Kill();
        Position = Vector2.Zero;
        _shake = CreateTween();
        var rng = new RandomNumberGenerator();
        for (var i = 0; i < 6; i++)
        {
            var k = strength * (1 - i / 6f);
            _shake.TweenProperty(this, "position", new Vector2(rng.RandfRange(-k, k), rng.RandfRange(-k, k)), 0.035);
        }
        _shake.TweenProperty(this, "position", Vector2.Zero, 0.05);
    }
}

// One number in the air, drawn with what is flung round it.
public partial class InkNumber : Control
{
    public enum Kind { Wound, Heal, Seal, SealBreaks }

    private readonly Kind _kind;
    private readonly int _line;
    private readonly Label _label = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly RandomNumberGenerator _rng = new();
    private readonly List<(Vector2 Dir, float Dist, float Size, float Stretch)> _drops = [];
    // A wound's drops are real ink, cut out of scans (assets/fx/make_fx.py): which one, how big, how turned.
    private readonly List<(int Sprite, Vector2 At, float Size, float Turn, bool Flip)> _blots = [];
    private static List<Texture2D>? _drop;
    private float _burst;        // 0..1, how far the drops have flown out
    private float _dry;          // 0..1, red to dried ink
    private float _size = 30;
    private bool _lethal;
    private Tween? _punch;

    public InkNumber(Kind kind, int line)
    {
        _kind = kind;
        _line = line;
        MouseFilter = MouseFilterEnum.Ignore;
        _rng.Randomize();
        // a wound is ink on paper: a pale paper edge keeps the dried red readable on the dark room
        _label.AddThemeColorOverride("font_outline_color", kind == Kind.Wound ? new Color("efe2c4") : new Color(0.04f, 0.02f, 0.02f, 0.95f));
        _label.AddThemeConstantOverride("outline_size", kind == Kind.Wound ? 5 : 7);
        AddChild(_label);
        Rotation = Mathf.DegToRad(_rng.RandfRange(-8, 8));
    }

    // A blow (or the seal's number) lands: the text set, the size from the share of health, a punch.
    public void Hit(string text, float share, bool lethal)
    {
        _lethal |= lethal;
        _size = _kind switch
        {
            Kind.Wound => (30 + 40 * Mathf.Clamp(share * 2.5f, 0, 1)) * (lethal ? 1.3f : 1),
            Kind.Heal => 28 + 18 * Mathf.Clamp(share * 2.5f, 0, 1),
            _ => 24,
        };
        _label.Text = text;
        _label.AddThemeFontSizeOverride("font_size", (int)_size);
        var colour = _kind switch { Kind.Heal => SessionScreen_Colours.Heal, Kind.Wound => SessionScreen_Colours.Red, _ => Colors.White };
        _label.AddThemeColorOverride("font_color", colour);
        var min = _label.GetMinimumSize();
        _label.Size = min;
        _label.Position = -min / 2;
        if (_kind == Kind.Wound)
            Blots(_rng.RandiRange(6, 9));
        else if (_kind == Kind.Heal)
            Splash(8);
        else
        {
            // block: the label stands right of its shield
            _label.Position = new Vector2(ShieldW * 0.5f + 3, -min.Y / 2);
        }
        _punch?.Kill();
        Scale = Vector2.One * (_kind == Kind.Heal ? 1.3f : 1.65f);
        _punch = CreateTween();
        _punch.TweenProperty(this, "scale", Vector2.One, 0.24).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _burst = 0;
        _dry = 0;
        var b = CreateTween();
        b.TweenMethod(Callable.From<float>(v => { _burst = v; QueueRedraw(); }), 0f, 1f, 0.14).SetEase(Tween.EaseType.Out);
        QueueRedraw();
    }

    // Drops flung round the number: round ones and streaks pointing outward, more for a bigger blow.
    private void Splash(int n)
    {
        var r0 = _size * 0.5f;
        for (var i = 0; i < n; i++)
        {
            var a = _rng.RandfRange(0, Mathf.Tau);
            _drops.Add((new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.75f),
                r0 + _rng.RandfRange(0, _size * (_kind == Kind.Wound ? 0.35f : 0.9f)),
                _kind == Kind.Wound ? _rng.RandfRange(1.2f, 2.6f) : _rng.RandfRange(1.4f, _size * 0.11f), 1));
        }
    }

    // Several small drops and blots scattered round the number, each a different one from the pool.
    private void Blots(int n)
    {
        if (_drop is null)
        {
            _drop = [];
            for (var i = 0; i < 64 && ResourceLoader.Exists($"res://assets/fx/drops/drop_{i:00}.png"); i++)
                _drop.Add(GD.Load<Texture2D>($"res://assets/fx/drops/drop_{i:00}.png"));
        }
        if (_drop.Count == 0)
            return;
        var half = _label.Size / 2;
        for (var i = 0; i < n; i++)
        {
            var a = _rng.RandfRange(0, Mathf.Tau);
            // just outside the number's box, a little further now and then
            var reach = new Vector2(half.X + _rng.RandfRange(2, _size * 0.45f), half.Y + _rng.RandfRange(0, _size * 0.35f));
            _blots.Add((_rng.RandiRange(0, _drop.Count - 1), new Vector2(Mathf.Cos(a) * reach.X, Mathf.Sin(a) * reach.Y),
                _rng.RandfRange(0.2f, 0.55f) * _size, _rng.RandfRange(0, Mathf.Tau), _rng.Randf() < 0.5f));
        }
    }

    private float _k;   // how far through its flight, for the drops to fade early
    private const float ShieldW = 22;

    // After the last blow: up and to the side in an arc, slowing, the red drying, then gone.
    public void Fly()
    {
        var side = (_line % 2 == 0 ? -1 : 1) * _rng.RandfRange(30, 70);
        var rise = _kind is Kind.Seal or Kind.SealBreaks ? _rng.RandfRange(30, 45) : _rng.RandfRange(60, 90);
        var life = _lethal ? 2.0 : _kind == Kind.SealBreaks ? 1.2 : 1.45;
        var start = Position;
        var t = CreateTween();
        t.TweenMethod(Callable.From<float>(k =>
        {
            var e = 1 - (1 - k) * (1 - k);                       // slowing
            Position = start + new Vector2(side * k, -rise * e); // sideways steady, up slowing: an arc
            _dry = Mathf.Clamp(k * 1.3f, 0, 1);
            _k = k;
            var c = _kind == Kind.Wound ? SessionScreen_Colours.Red.Lerp(SessionScreen_Colours.Dry, _dry) : _label.GetThemeColor("font_color");
            if (_kind == Kind.Wound)
                _label.AddThemeColorOverride("font_color", c);
            Modulate = new Color(1, 1, 1, k < 0.72f ? 1 : 1 - (k - 0.72f) / 0.28f);
            QueueRedraw();
        }), 0f, 1f, life);
        t.TweenCallback(Callable.From(QueueFree));
    }

    public override void _Draw()
    {
        if (_kind is Kind.Seal or Kind.SealBreaks)
        {
            DrawSeal();
            return;
        }
        if (_kind == Kind.Wound && _drop is { Count: > 0 })
        {
            var ink = SessionScreen_Colours.Red.Lerp(SessionScreen_Colours.Dry, _dry);
            var fade = Mathf.Clamp(1 - (_k - 0.35f) * 1.6f, 0, 1);
            foreach (var (sprite, at, size, turn, flip) in _blots)
            {
                var tex = _drop[sprite];
                var k = size / Mathf.Max(tex.GetWidth(), tex.GetHeight());
                var dim = new Vector2(tex.GetWidth(), tex.GetHeight()) * k;
                DrawSetTransform(at * (0.55f + 0.45f * _burst), turn, new Vector2(flip ? -1 : 1, 1) * (0.6f + 0.4f * _burst));
                DrawTextureRect(tex, new Rect2(-dim / 2, dim), false, new Color(ink, fade));
            }
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }
        var colour = _kind == Kind.Heal
            ? SessionScreen_Colours.Heal
            : SessionScreen_Colours.Red.Lerp(SessionScreen_Colours.Dry, _dry);
        foreach (var (dir, dist, size, stretch) in _drops)
        {
            var p = dir * dist * _burst;
            if (_kind == Kind.Heal)
            {
                // a spark: a small four-pointed star
                var s = size * 0.9f;
                DrawLine(p - new Vector2(s * 1.6f, 0), p + new Vector2(s * 1.6f, 0), new Color(colour, 0.9f), 1.5f, true);
                DrawLine(p - new Vector2(0, s * 1.6f), p + new Vector2(0, s * 1.6f), new Color(colour, 0.9f), 1.5f, true);
                continue;
            }
            if (stretch > 1)
            {
                // a streak, thick at its outer end as a flung drop is
                var tail = p - dir * size * stretch * 2.2f * _burst;
                DrawLine(tail, p, colour, size * 0.7f, true);
                DrawCircle(p, size * 0.8f, colour);
            }
            else
                DrawCircle(p, size, new Color(colour, Mathf.Clamp(1 - _k * 2.2f, 0, 1)));
        }
    }

    // Block: the shield (assets/fx/make_fx.py) left of its number.
    private static Texture2D? _shield;

    private void DrawSeal()
    {
        _shield ??= GD.Load<Texture2D>("res://assets/fx/shield_block.png");
        var w = ShieldW * 1.25f;
        DrawTextureRect(_shield, new Rect2(-w * 0.5f - 2, -w * 0.5f, w, w), false);
        _label.AddThemeColorOverride("font_color", SessionScreen_Colours.Rim);
    }
}

// The colours the numbers use, where a static nested class of the partial screen cannot be reached from InkNumber.
internal static class SessionScreen_Colours
{
    public static readonly Color Red = new("e33a2c");
    public static readonly Color Dry = new("7a1c14");
    public static readonly Color Heal = new("6fd17a");
    public static readonly Color Steel = new("5d7f99");
    public static readonly Color Rim = new("c9dae6");
}
