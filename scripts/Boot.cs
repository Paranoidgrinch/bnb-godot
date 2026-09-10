using Godot;
using RogueDeck.Run;

namespace BnbGodot;

// The title screen: shows the loaded game's identity, a character roster read from Blueprint.Characters
// (unlock-gated by the meta profile), and New Run / Continue / Quit. Generic — everything comes from the
// blueprint + presentation manifest, so any game.roguedeck.json gets a title screen. With
// `--headless -- --smoke` it prints the load line and quits (the CI boot check); the other --smoke-*
// variants boot straight into a seeded run (routed here).
public partial class Boot : Control
{
    private string? _selectedCharacter;

    public override void _Ready()
    {
        Theme = MoonvineTheme.Build();
        var background = new ColorRect { Color = MoonvineTheme.Bg };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);
        MoveChild(background, 0); // behind the scene's Title/Stats labels

        var host = GameHost.Instance;
        GetNode<Label>("Title").Text = host.HostError ?? host.GameTitle;
        if (host.HostError is not null)
            return;

        var blueprint = host.Blueprint;
        GetNode<Label>("Stats").Text =
            $"{blueprint.Cards.Count} cards · {blueprint.Encounters.Count} encounters · "
            + $"{blueprint.Relics.Count} relics · {blueprint.Map.Nodes.Count} map nodes";
        GD.Print($"loaded: {host.GameTitle} ({blueprint.Cards.Count} cards, {blueprint.Map.Nodes.Count} map nodes)");

        var userArgs = OS.GetCmdlineUserArgs();
        if (userArgs.Contains("--smoke"))
        {
            GetTree().Quit();
            return;
        }
        // THE ART CENSUS. Which pictures the document asks for and which of them exist — a question about the
        // document and the folder, not about a screen, so it is answered before one is built. The same list
        // with the design canon's brief beside every relic is bnb-content/ART_SLOTS.md.
        if (userArgs.Contains("--smoke-art"))
        {
            ReportArt(blueprint);
            GetTree().Quit();
            return;
        }
        // Any session smoke boots straight into a seeded run; SessionScreen runs the matching probe + quits.
        // The run simulator: a seeded random walk over the real screens, one run per process.
        if (userArgs.Contains("--sim"))
        {
            var seed = SessionScreen.SimArg("--sim-seed", 1);
            var roster = host.AvailableCharacters;
            var character = roster.Count > 0 ? roster[new Random(seed).Next(roster.Count)].Id : null;
            SessionScreen.SimCharacter = character;
            host.StartNewRun(seed, character,
                health: userArgs.Contains("--sim-immortal") ? 9999
                    : SessionScreen.SimArg("--sim-health", 0) is > 0 and var hp ? hp : null);
            CallDeferred(nameof(GoToSession));
            return;
        }
        if (userArgs.Any(a => a is "--smoke-run" or "--smoke-map" or "--smoke-full" or "--smoke-timing" or "--smoke-reward" or "--smoke-target" or "--smoke-draw" or "--smoke-statuses" or "--smoke-shop" or "--smoke-event" or "--smoke-rest" or "--smoke-marathon" or "--smoke-ambush" or "--smoke-elite" or "--smoke-crowd" or "--smoke-boss" or "--smoke-tooltips" or "--smoke-format"))
        {
            host.StartNewRun(seed: 7,
                health: userArgs.Any(a => a is "--smoke-marathon" or "--smoke-crowd" or "--smoke-boss") ? 9999 : null);
            CallDeferred(nameof(GoToSession));
            return;
        }

        BuildTitle(host);

        if (userArgs.Contains("--smoke-title") && !DisplayServer.GetName().Contains("headless"))
            _ = CaptureTitleThenQuit();
    }

    // An unfilled slot is NORMAL, so this probe cannot fail on a count — it reports one. What it does assert
    // is the rule that keeps the count honest: an upgraded card must ask for its base card's picture, or 413
    // cards would want 413 pictures instead of the 254 the table lists.
    private static void ReportArt(RunBlueprint blueprint)
    {
        var cards = blueprint.Cards.Select(c => c.Id).Where(id => !id.EndsWith('+'))
            .Distinct(StringComparer.Ordinal).ToList();
        var relics = blueprint.Relics.Select(r => r.Id).Distinct(StringComparer.Ordinal).ToList();
        var filledCards = cards.Count(id => CardVisuals.CardArt(id) is not null);
        var filledRelics = relics.Count(id => CardVisuals.RelicArt(id) is not null);
        var strays = blueprint.Cards.Select(c => c.Id).Where(id => id.EndsWith('+'))
            .Where(id => CardVisuals.SlotPath("cards", id) != CardVisuals.SlotPath("cards", id.TrimEnd('+')))
            .ToList();

        GD.Print($"smoke-art: {cards.Count + relics.Count} slots asked for "
            + $"({cards.Count} cards, {relics.Count} relics), "
            + $"{filledCards + filledRelics} filled, {strays.Count} upgrade(s) asking for a picture of their own");
        foreach (var id in strays.Take(5))
            GD.Print($"  STRAY {id} asks for {CardVisuals.SlotPath("cards", id)}");
        foreach (var id in cards.Where(id => CardVisuals.CardArt(id) is null).Take(2))
            GD.Print($"  waiting: assets/art/{CardVisuals.SlotPath("cards", id)}");
        foreach (var id in relics.Where(id => CardVisuals.RelicArt(id) is null).Take(2))
            GD.Print($"  waiting: assets/art/{CardVisuals.SlotPath("relics", id)}");
    }

    private async System.Threading.Tasks.Task CaptureTitleThenQuit()
    {
        for (var i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng("user://smoke-title.png");
        GD.Print("smoke: screenshot user://smoke-title.png");
        GetTree().Quit();
    }

    private void BuildTitle(GameHost host)
    {
        GetNodeOrNull("TitleBody")?.QueueFree();
        var root = new VBoxContainer { Name = "TitleBody" };
        root.SetAnchorsPreset(LayoutPreset.Center);
        root.CustomMinimumSize = new Vector2(720, 0);
        root.AddThemeConstantOverride("separation", 16);
        root.Position += new Vector2(-360, 90); // below the scene's Title/Stats labels
        AddChild(root);

        // ── character roster (unlock-gated) ──────────────────────────────────────
        var available = host.AvailableCharacters.Select(c => c.Id).ToHashSet();
        _selectedCharacter ??= host.AvailableCharacters.FirstOrDefault()?.Id;

        if (host.Blueprint.Characters.Count > 0)
        {
            root.AddChild(new Label { Text = "Choose your character", HorizontalAlignment = HorizontalAlignment.Center });
            var roster = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            roster.AddThemeConstantOverride("separation", 12);
            foreach (var character in host.Blueprint.Characters)
                roster.AddChild(CharacterCard(host, character, available.Contains(character.Id)));
            root.AddChild(roster);
        }

        // ── actions ──────────────────────────────────────────────────────────────
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        actions.AddThemeConstantOverride("separation", 10);

        var start = new Button { Text = "New run ▸", CustomMinimumSize = new Vector2(160, 44) };
        start.Disabled = host.Blueprint.Characters.Count > 0 && _selectedCharacter is null;
        start.Pressed += () =>
        {
            host.StartNewRun(seed: (int)(Time.GetUnixTimeFromSystem() % int.MaxValue), _selectedCharacter);
            GoToSession();
        };
        actions.AddChild(start);

        if (host.HasSave)
        {
            var resume = new Button { Text = "Continue run", CustomMinimumSize = new Vector2(160, 44) };
            resume.Pressed += () =>
            {
                if (host.ResumeRun())
                    GoToSession();
            };
            actions.AddChild(resume);
        }

        var quit = new Button { Text = "Quit", CustomMinimumSize = new Vector2(120, 44) };
        quit.Pressed += () => GetTree().Quit();
        actions.AddChild(quit);
        root.AddChild(actions);
    }

    private Control CharacterCard(GameHost host, RunCharacter character, bool unlocked)
    {
        var presentation = host.Blueprint.Presentation.Characters.GetValueOrDefault(character.Id);
        var selected = _selectedCharacter == character.Id;

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(220, 130) };
        panel.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(
            selected ? MoonvineTheme.BgControl : MoonvineTheme.BgPanel,
            selected ? MoonvineTheme.AccentLight : unlocked ? new Color(MoonvineTheme.Accent, 0.3f) : new Color(MoonvineTheme.TextMuted, 0.2f)));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        var name = new Label { Text = character.Start.HeroName ?? character.Id };
        name.AddThemeFontSizeOverride("font_size", 18);
        name.AddThemeColorOverride("font_color", unlocked ? MoonvineTheme.Text : MoonvineTheme.TextMuted);
        column.AddChild(name);
        column.AddChild(MutedLabel($"{character.Start.MaxHealth} HP"));
        var flavor = new Label
        {
            Text = unlocked
                ? presentation?.FlavorText ?? ""
                : $"🔒 Locked{(character.UnlockFlag is { } flag ? $" — {flag}" : "")}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        flavor.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        column.AddChild(flavor);
        panel.AddChild(column);

        if (unlocked)
        {
            var button = new Button { Flat = true };
            button.SetAnchorsPreset(LayoutPreset.FullRect);
            button.Pressed += () =>
            {
                _selectedCharacter = character.Id;
                Rebuild();
            };
            panel.AddChild(button);
        }
        return panel;
    }

    private static Label MutedLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        return label;
    }

    // Redraw after a selection change: BuildTitle frees and rebuilds only the TitleBody container.
    private void Rebuild() => BuildTitle(GameHost.Instance);

    private void GoToSession() => GetTree().ChangeSceneToFile("res://scenes/Session.tscn");
}
