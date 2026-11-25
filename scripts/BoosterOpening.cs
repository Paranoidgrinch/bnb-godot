using Godot;

namespace BnbGodot;

// ── BREAKING THE SEAL, ON A SCREEN OF ITS OWN (user, 2026-11-22) ──────────────────────────────────────────────
// Opening a drawer used to swap its button for a line of names. Now the folder is laid out face down — every
// card of it, then its relics — and the cards are turned over one after another, each with a short flip, so the
// player sees what arrived one thing at a time. "Show all" turns the rest at once; "File it" closes the screen.
// It sits over the Safe Deposit Box and hands back to it when closed (the box then shows the drawer opened).
public partial class BoosterOpening : Control
{
    public const string OverlayName = "BoosterOpeningOverlay";

    private readonly GameHost _host;
    private readonly ProgressionDefinition _definition;
    private readonly ProgressionBooster _booster;
    private readonly Action _onClose;

    private readonly List<Control> _slots = [];
    private readonly List<Func<Control>> _fronts = [];
    private readonly HashSet<int> _turned = [];
    private Button _all = null!;
    private Button _done = null!;
    private Tween? _sequence;

    private const float CardScale = 1.1f;
    private const int RelicSize = 96;
    private const double FlipHalf = 0.14;   // seconds for one half of a flip (edge-on, then open)
    private const double Between = 0.42;    // seconds between two cards turning

    public BoosterOpening(GameHost host, ProgressionDefinition definition, ProgressionBooster booster, Action onClose)
    {
        _host = host;
        _definition = definition;
        _booster = booster;
        _onClose = onClose;
        Name = OverlayName;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.9f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(1100, 0) };
        column.AddThemeConstantOverride("separation", 16);
        centre.AddChild(column);

        var hero = _host.Blueprint.Characters.FirstOrDefault(c => c.Id == _definition.Character)?.Start.HeroName ?? _definition.Character;
        var over = new Label { Text = $"{hero} · Safe Deposit Box", HorizontalAlignment = HorizontalAlignment.Center };
        over.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        column.AddChild(over);
        var title = new Label { Text = $"Drawer {_booster.Level} · {_booster.Name}", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        column.AddChild(title);
        var note = new Label
        {
            Text = "The seal is broken. What the folder holds can turn up in your runs from the next one on.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        note.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        column.AddChild(note);

        var flow = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.Center, Name = "BoosterCards" };
        flow.AddThemeConstantOverride("h_separation", 14);
        flow.AddThemeConstantOverride("v_separation", 14);
        column.AddChild(flow);
        foreach (var card in _booster.Cards)
            AddSlot(flow, new Vector2(Mathf.Round(CardVisuals.CardW * CardScale), Mathf.Round(CardVisuals.CardH * CardScale)),
                () => CardVisuals.Back(CardScale), () => CardFront(card));
        foreach (var relic in _booster.Relics)
            AddSlot(flow, new Vector2(RelicSize + 40, Mathf.Round(CardVisuals.CardH * CardScale)),
                () => RelicBack(), () => RelicFront(relic));

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        _all = new Button { Text = "Show all", Name = "BoosterShowAll", CustomMinimumSize = new Vector2(160, 40) };
        _all.Pressed += ShowAll;
        buttons.AddChild(_all);
        _done = new Button { Text = "File it", Name = "BoosterDone", CustomMinimumSize = new Vector2(160, 40), Disabled = true };
        _done.Pressed += () => _onClose();
        buttons.AddChild(_done);
        column.AddChild(buttons);

        Play();
    }

    private void AddSlot(Container parent, Vector2 size, Func<Control> back, Func<Control> front)
    {
        var slot = new Control { CustomMinimumSize = size, Size = size, PivotOffset = size / 2, MouseFilter = MouseFilterEnum.Pass };
        var face = back();
        face.Position = (size - face.CustomMinimumSize) / 2;
        slot.AddChild(face);
        // A click turns that one at once, out of order.
        var index = _slots.Count;
        slot.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                Turn(index, animate: true);
        };
        parent.AddChild(slot);
        _slots.Add(slot);
        _fronts.Add(front);
    }

    // One after another, each a beat after the last; the first waits a moment so the folder is seen closed.
    private void Play()
    {
        _sequence = CreateTween();
        _sequence.TweenInterval(0.6);
        for (var i = 0; i < _slots.Count; i++)
        {
            var index = i;
            _sequence.TweenCallback(Callable.From(() => Turn(index, animate: true)));
            _sequence.TweenInterval(Between);
        }
        _sequence.TweenCallback(Callable.From(Finished));
    }

    private void ShowAll()
    {
        _sequence?.Kill();
        for (var i = 0; i < _slots.Count; i++)
            Turn(i, animate: false);
        Finished();
    }

    private void Finished()
    {
        _all.Disabled = true;
        _done.Disabled = false;
        _done.GrabFocus();
    }

    // The flip: squeeze to the edge, swap the back for the front, open again — and a small lift as it lands.
    private void Turn(int index, bool animate)
    {
        if (!_turned.Add(index))
            return;
        var slot = _slots[index];
        void Swap()
        {
            foreach (var child in slot.GetChildren())
                child.QueueFree();
            var face = _fronts[index]();
            face.Position = (slot.CustomMinimumSize - face.CustomMinimumSize) / 2;
            slot.AddChild(face);
        }
        if (!animate)
        {
            Swap();
            slot.Scale = Vector2.One;
            return;
        }
        var flip = slot.CreateTween();
        flip.TweenProperty(slot, "scale", new Vector2(0.02f, 1.04f), FlipHalf).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        flip.TweenCallback(Callable.From(Swap));
        flip.TweenProperty(slot, "scale", new Vector2(1.06f, 1.06f), FlipHalf).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        flip.TweenProperty(slot, "scale", Vector2.One, 0.12);
        if (_turned.Count == _slots.Count)
            flip.TweenCallback(Callable.From(Finished));
    }

    private Control CardFront(string id)
    {
        var card = _host.Blueprint.Cards.FirstOrDefault(c => c.Id == id);
        var look = _host.Blueprint.Presentation.Cards.GetValueOrDefault(id);
        var rules = look?.FlavorText ?? "";
        var face = CardVisuals.Face(new CardVisuals.CardFace(
            Id: id,
            Title: card?.NameKey ?? id,
            Cost: card is null || card.Costs.Count == 0 ? "" : string.Join("·", card.Costs.Select(c => c.Amount)),
            Rules: rules,
            Rarity: look?.Rarity,
            CostTip: "",
            Dimmed: false,
            Armed: false), scale: CardScale);
        face.MouseFilter = MouseFilterEnum.Pass;
        return face;
    }

    // A relic is not a card, so it does not pretend to be one: face down it is a blank plate the size of its
    // tile, face up the tile and its name under it.
    private static Control RelicBack()
    {
        var plate = new PanelContainer { CustomMinimumSize = new Vector2(RelicSize, RelicSize), MouseFilter = MouseFilterEnum.Ignore };
        var box = new StyleBoxFlat { BgColor = MoonvineTheme.BgRaised, BorderColor = MoonvineTheme.GoldDim };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(4);
        plate.AddThemeStyleboxOverride("panel", box);
        var mark = new Label { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        mark.AddThemeFontSizeOverride("font_size", 34);
        mark.AddThemeColorOverride("font_color", MoonvineTheme.GoldDim);
        plate.AddChild(mark);
        var holder = new CenterContainer { CustomMinimumSize = new Vector2(RelicSize + 40, Mathf.Round(CardVisuals.CardH * CardScale)) };
        holder.AddChild(plate);
        return holder;
    }

    private Control RelicFront(string id)
    {
        var relic = _host.Blueprint.Relics.FirstOrDefault(r => r.Id == id);
        var look = _host.Blueprint.Presentation.Relics.GetValueOrDefault(id);
        var name = relic?.DisplayName ?? id;
        var column = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(RelicSize + 40, Mathf.Round(CardVisuals.CardH * CardScale)),
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        column.AddThemeConstantOverride("separation", 6);
        var tile = new CenterContainer();
        tile.AddChild(CardVisuals.Tile(new CardVisuals.RelicFace(
            Id: id, Title: name, Pool: look?.Frame, Tooltip: $"{name}\n{Glossary.Explain(look?.FlavorText)}", Off: false), RelicSize));
        column.AddChild(tile);
        var label = new Label
        {
            Text = name, HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(RelicSize + 40, 0),
        };
        label.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        label.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(label);
        var kind = new Label { Text = "relic", HorizontalAlignment = HorizontalAlignment.Center };
        kind.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        kind.AddThemeFontSizeOverride("font_size", 11);
        column.AddChild(kind);
        return column;
    }

    // Over whatever screen the box is open on (a Control that fills the window); closing hands back to it.
    public static void Show(Control screen, GameHost host, ProgressionDefinition definition, ProgressionBooster booster, Action closed)
    {
        screen.GetNodeOrNull(OverlayName)?.QueueFree();
        BoosterOpening? opening = null;
        opening = new BoosterOpening(host, definition, booster, () =>
        {
            opening?.QueueFree();
            closed();
        });
        opening.SetAnchorsPreset(LayoutPreset.FullRect);
        screen.AddChild(opening);
        opening.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }
}
