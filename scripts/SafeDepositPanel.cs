using Godot;

namespace BnbGodot;

// ── THE SAFE DEPOSIT BOX (user, 2026-11-17: "ein persönlicher Aktenschrank oder ein Bankschließfach vom Vibe her") ─
// The player's own box between runs. For now it holds the SHELL COMPARTMENT — the shells, the streaks, the
// statement of what came in and went out — and the MODIFIER REGISTER, where shells buy run modifiers (Shells).
// The booster drawers and the card register come with the card progression (META_PROGRESSION_PLAN.md §2).
//
// Closed until the first won run: the box says so, and shows what waits behind the shells.
public partial class SafeDepositPanel : PanelContainer
{
    public const string OverlayName = "SafeDepositOverlay";

    private readonly GameHost _host;
    private readonly Action _onClose;
    private VBoxContainer _body = null!;

    public SafeDepositPanel(GameHost host, Action onClose)
    {
        _host = host;
        _onClose = onClose;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(980, 0);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        var title = new Label { Text = "Safe Deposit Box", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        column.AddChild(title);

        _body = new VBoxContainer { Name = "SafeDepositBody" };
        column.AddChild(_body);
        Fill();

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(140, 40) };
        close.Pressed += () => _onClose();
        actions.AddChild(close);
        column.AddChild(actions);
    }

    private void Refill()
    {
        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
        Fill();
    }

    private void Fill()
    {
        var meta = _host.Meta;
        var open = Shells.IsOpen(meta);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        _body.AddChild(row);

        // ── left: the shell compartment ──
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        left.AddThemeConstantOverride("separation", 6);
        row.AddChild(left);
        left.AddChild(Heading("Shell compartment"));
        var balance = new Label
        {
            Name = "ShellBalance",
            Text = open ? $"{Shells.Balance(meta)} shell{(Shells.Balance(meta) == 1 ? "" : "s")}" : "Sealed",
        };
        balance.AddThemeFontSizeOverride("font_size", 30);
        balance.AddThemeColorOverride("font_color", open ? MoonvineTheme.Accent : MoonvineTheme.TextMuted);
        left.AddChild(balance);
        if (!open)
        {
            left.AddChild(Muted("The compartment opens with your first won run, which pays your first shell. "
                + "Shells buy run modifiers; you switch them on before a run."));
        }
        else
        {
            left.AddChild(Muted("A won run pays 1 shell; the modifiers you play with move that by their value (0 to 6). "
                + "Every further win in a row pays 1 more. A loss pays nothing."));
            foreach (var character in CharacterSelectPanel.Roster(_host.Blueprint))
                if (Shells.Streak(meta, character.Id) is var streak and > 0)
                    left.AddChild(Soft($"{character.Start.HeroName}: {streak} win{(streak == 1 ? "" : "s")} in a row"));
        }
        left.AddChild(Heading("Statement"));
        var statement = new VBoxContainer { Name = "ShellStatement" };
        statement.AddThemeConstantOverride("separation", 2);
        var entries = ShellStatement.Read();
        if (entries.Count == 0)
            statement.AddChild(Muted("Nothing yet."));
        foreach (var entry in entries.Take(12))
        {
            var line = new Label
            {
                Text = $"{entry.Date}  {entry.Text}{(entry.Shells == 0 ? "" : $"  {(entry.Shells > 0 ? "+" : "−")}{Math.Abs(entry.Shells)}")}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            line.AddThemeFontSizeOverride("font_size", 13);
            line.AddThemeColorOverride("font_color", entry.Shells > 0 ? MoonvineTheme.TextSoft : MoonvineTheme.TextMuted);
            statement.AddChild(line);
        }
        left.AddChild(statement);

        // ── right: the modifier register ──
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 6);
        row.AddChild(right);
        right.AddChild(Heading("Run modifiers"));
        right.AddChild(Muted("Bought once, then switched on or off before every run, on the sheet that starts it."));
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 470),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            Name = "ModifierRegister",
        };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(list);
        right.AddChild(scroll);
        foreach (var (kind, label) in new[]
                 {
                     (ModifierKind.Bad, "Bad — harder, pays more"),
                     (ModifierKind.Different, "Different — changes the game, pays the same"),
                     (ModifierKind.Good, "Good — easier, pays less"),
                 })
        {
            list.AddChild(Soft(label));
            foreach (var modifier in Shells.Catalog.Where(m => m.Kind == kind))
                list.AddChild(Row(meta, open, modifier));
        }
    }

    private Control Row(RogueDeck.Run.MetaState meta, bool open, RunModifier modifier)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.25f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        style.SetCornerRadiusAll(6);
        card.AddThemeStyleboxOverride("panel", style);
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        card.AddChild(line);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var name = new Label { Text = $"{modifier.Name}   {NewRunPanel.ValueText(modifier.Value)}" };
        name.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        text.AddChild(name);
        var rule = new Label { Text = modifier.Rule, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        rule.AddThemeFontSizeOverride("font_size", 13);
        rule.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        text.AddChild(rule);
        line.AddChild(text);

        var ready = Shells.IsReady(_host.Blueprint, modifier);
        var owned = Shells.Owns(meta, modifier);
        var buy = new Button
        {
            Name = $"Buy_{modifier.Id}",
            CustomMinimumSize = new Vector2(170, 36),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        if (owned)
        {
            buy.Text = "Owned";
            buy.Disabled = true;
        }
        else if (!ready)
        {
            buy.Text = "In preparation";
            buy.Disabled = true;
            buy.TooltipText = "This modifier is not built yet. It can be bought once it is.";
        }
        else
        {
            buy.Text = $"Buy · {modifier.Price} shell{(modifier.Price == 1 ? "" : "s")}";
            buy.Disabled = !Shells.CanBuy(meta, _host.Blueprint, modifier);
            if (!open)
                buy.TooltipText = "The shell compartment opens with your first won run.";
            else if (buy.Disabled)
                buy.TooltipText = $"You have {Shells.Balance(meta)} shell(s).";
            buy.Pressed += () =>
            {
                if (Shells.Buy(_host, modifier))
                    Refill();
            };
        }
        line.AddChild(buy);
        return card;
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        label.AddThemeFontSizeOverride("font_size", 17);
        return label;
    }

    private static Label Soft(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        return label;
    }

    private static Label Muted(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        label.AddThemeFontSizeOverride("font_size", 13);
        return label;
    }

    public static void Open(Godot.Node screen, GameHost host)
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
        center.AddChild(new SafeDepositPanel(host, () => screen.GetNodeOrNull(OverlayName)?.QueueFree()) { Name = nameof(SafeDepositPanel) });
        veil.AddChild(center);
        screen.AddChild(veil);
    }
}
