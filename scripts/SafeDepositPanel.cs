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
    private static string _tab = "drawers";

    public static string Tab { get => _tab; set => _tab = value; }

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

        // Two compartments: the drawers (the card progression) and the shells (the run modifiers).
        var tabs = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Name = "SafeDepositTabs" };
        tabs.AddThemeConstantOverride("separation", 6);
        var group = new ButtonGroup();
        foreach (var (id, label) in new[] { ("drawers", "Drawers"), ("shells", "Shell compartment") })
        {
            var tab = new Button
            {
                Text = label, Name = $"SafeDepositTab_{id}", ToggleMode = true, ButtonGroup = group,
                ButtonPressed = _tab == id, CustomMinimumSize = new Vector2(180, 34),
            };
            var chosen = id;
            tab.Toggled += on =>
            {
                if (!on || _tab == chosen)
                    return;
                _tab = chosen;
                Refill();
            };
            tabs.AddChild(tab);
        }
        column.AddChild(tabs);

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
        if (_tab == "drawers")
            FillDrawers();
        else
            FillShells();
    }

    // ── the drawers: one folder per level, sealed until it is opened here ──
    private void FillDrawers()
    {
        var meta = _host.Meta;
        var definitions = CardProgression.All(_host.Blueprint).ToList();
        if (definitions.Count == 0)
        {
            _body.AddChild(Muted("No drawers yet."));
            return;
        }
        foreach (var definition in definitions)
        {
            var hero = _host.Blueprint.Characters.FirstOrDefault(c => c.Id == definition.Character)?.Start.HeroName ?? definition.Character;
            var xp = CardProgression.Xp(meta, definition.Character);
            var level = CardProgression.LevelOf(definition, xp);
            var (into, needed) = CardProgression.Progress(definition, xp);
            var cards = CardProgression.OwnedCards(meta, definition);
            var relics = CardProgression.OwnedRelics(meta, definition);
            var head = new HBoxContainer();
            head.AddThemeConstantOverride("separation", 16);
            head.AddChild(Heading($"{hero} · level {level} of {definition.MaxLevel}"));
            var bar = new ProgressBar
            {
                Name = $"XpBar_{definition.Character}", MinValue = 0, MaxValue = Math.Max(needed, 1), Value = needed == 0 ? 1 : into,
                ShowPercentage = false, CustomMinimumSize = new Vector2(220, 18), SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            var fill = new StyleBoxFlat { BgColor = MoonvineTheme.Accent };
            fill.SetCornerRadiusAll(4);
            var track = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f) };
            track.SetCornerRadiusAll(4);
            bar.AddThemeStyleboxOverride("fill", fill);
            bar.AddThemeStyleboxOverride("background", track);
            head.AddChild(bar);
            head.AddChild(Soft(needed == 0 ? "every folder earned" : $"{into} / {needed} XP to level {level + 1}"));
            _body.AddChild(head);
            _body.AddChild(Muted($"Register: {cards.Count} of {definition.AllCards.Count()} cards, {relics.Count} of {definition.AllRelics.Count()} relics. "
                + $"XP per run: {definition.Xp.Room} a room, {definition.Xp.Elite} an elite, {definition.Xp.Boss} a boss, {definition.Xp.Win} for the win. "
                + "Opened cards and relics can turn up from the next run on."));

            var scroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0, 430), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                Name = $"Drawers_{definition.Character}",
            };
            var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 8);
            grid.AddThemeConstantOverride("v_separation", 8);
            scroll.AddChild(grid);
            _body.AddChild(scroll);
            foreach (var booster in definition.Boosters)
                grid.AddChild(Drawer(meta, definition, booster));
        }
    }

    private Control Drawer(RogueDeck.Run.MetaState meta, ProgressionDefinition definition, ProgressionBooster booster)
    {
        var earned = CardProgression.IsEarned(meta, definition, booster);
        var opened = CardProgression.IsOpened(meta, definition, booster);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(300, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = new StyleBoxFlat
        {
            BgColor = opened ? new Color(0, 0, 0, 0.25f) : earned ? new Color(0.45f, 0.08f, 0.08f, 0.55f) : new Color(0, 0, 0, 0.45f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 8,
            BorderColor = earned && !opened ? MoonvineTheme.Accent : new Color(0, 0, 0, 0),
        };
        style.SetBorderWidthAll(earned && !opened ? 2 : 0);
        style.SetCornerRadiusAll(6);
        card.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 3);
        card.AddChild(column);
        var title = new Label { Text = $"Drawer {booster.Level} · {booster.Name}" };
        title.AddThemeColorOverride("font_color", earned ? MoonvineTheme.Accent : MoonvineTheme.TextMuted);
        column.AddChild(title);
        if (opened)
        {
            var names = booster.Cards.Select(CardName).Concat(booster.Relics.Select(r => RelicName(r) + " (relic)"));
            var list = new Label { Text = string.Join(" · ", names), AutowrapMode = TextServer.AutowrapMode.WordSmart };
            list.AddThemeFontSizeOverride("font_size", 13);
            list.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
            column.AddChild(list);
        }
        else if (earned)
        {
            column.AddChild(Soft($"A sealed folder: {booster.Cards.Count} cards{(booster.Relics.Count > 0 ? $", {booster.Relics.Count} relic{(booster.Relics.Count == 1 ? "" : "s")}" : "")}."));
            var seal = new Button { Text = "Break the seal", Name = $"OpenBooster_{definition.Character}_{booster.Level}" };
            seal.Pressed += () =>
            {
                if (CardProgression.Open(_host, definition, booster))
                    Refill();
            };
            column.AddChild(seal);
        }
        else
        {
            column.AddChild(Muted($"Locked until level {booster.Level}."));
        }
        return card;
    }

    private string CardName(string id) =>
        _host.Blueprint.Cards.FirstOrDefault(c => c.Id == id)?.NameKey ?? id;

    private string RelicName(string id) =>
        _host.Blueprint.Relics.FirstOrDefault(r => r.Id == id)?.DisplayName ?? id;

    // ── the shell compartment and the modifier register ──
    private void FillShells()
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
