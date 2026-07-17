using Godot;

namespace BnbGodot;

// The provisional title screen: shows the loaded game's identity and starts/resumes a run. (The real
// title screen with character selection is milestone G5; this is the minimal entry the combat slice
// needs.) With `--headless -- --smoke` it prints the load line and quits — the CI-able boot check.
public partial class Boot : Control
{
    public override void _Ready()
    {
        Theme = MoonvineTheme.Build();
        var background = new ColorRect { Color = MoonvineTheme.Bg };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);
        MoveChild(background, 0);

        var host = GameHost.Instance;
        if (host.HostError is { } error)
        {
            GetNode<Label>("Title").Text = error;
            return;
        }

        var blueprint = host.Blueprint;
        GetNode<Label>("Title").Text = host.GameTitle;
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

        var buttons = new VBoxContainer();
        buttons.SetAnchorsPreset(LayoutPreset.Center);
        buttons.Position += new Vector2(-100, 60);
        buttons.CustomMinimumSize = new Vector2(200, 0);
        buttons.AddThemeConstantOverride("separation", 10);
        AddChild(buttons);

        var start = new Button { Text = "New run ▸" };
        start.Pressed += () =>
        {
            host.StartNewRun(seed: (int)(Time.GetUnixTimeFromSystem() % int.MaxValue));
            GetTree().ChangeSceneToFile("res://scenes/Session.tscn");
        };
        buttons.AddChild(start);

        if (host.HasSave)
        {
            var resume = new Button { Text = "Continue run" };
            resume.Pressed += () =>
            {
                if (host.ResumeRun())
                    GetTree().ChangeSceneToFile("res://scenes/Session.tscn");
            };
            buttons.AddChild(resume);
        }

        var quit = new Button { Text = "Quit" };
        quit.Pressed += () => GetTree().Quit();
        buttons.AddChild(quit);
    }

    private void GoToSession() => GetTree().ChangeSceneToFile("res://scenes/Session.tscn");
}
