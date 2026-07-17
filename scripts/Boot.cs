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
        // Any session smoke boots straight into a seeded run; SessionScreen runs the matching probe + quits.
        if (userArgs.Any(a => a is "--smoke-run" or "--smoke-map" or "--smoke-full" or "--smoke-timing"))
        {
            host.StartNewRun(seed: 7);
            CallDeferred(nameof(GoToSession));
            return;
        }

        BuildTitle(host);

        if (userArgs.Contains("--smoke-title") && !DisplayServer.GetName().Contains("headless"))
            _ = CaptureTitleThenQuit();
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
