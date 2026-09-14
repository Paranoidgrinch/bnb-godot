using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

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
        // THE SECOND HALF OF --smoke-quit: this is the title screen the button led back to. Reported from here
        // rather than from the session, because by the time the scene change has happened the node that pressed
        // the button no longer exists — and what is being checked is exactly what this screen knows.
        if (userArgs.Contains("--smoke-quit") && SessionScreen.SmokeQuitLeftAt is { } leftAt)
        {
            BuildTitle(host);
            var save = host.HasSave
                ? RunSaveJson.FromJson(Godot.FileAccess.GetFileAsString("user://run-save.json"))
                : null;
            var offered = FindButton(this, "Continue run") is not null;
            var sameRoom = save?.CurrentNodeId == leftAt;
            GD.Print($"smoke-quit: back on the title · save on disk={host.HasSave}"
                + $" · \"Continue run\" offered={offered}"
                + $" · saved room={save?.CurrentNodeId ?? "—"}"
                + $" {(sameRoom ? "(the room it was left in)" : $"— LEFT AT {leftAt}")}");
            GetTree().Quit(host.HasSave && offered && sameRoom ? 0 : 1);
            return;
        }
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
        // BOTH GENERATORS, SIDE BY SIDE, on the same seed (map rework S15). A question about the DOCUMENT and
        // the engine rather than about a screen, so it is answered before one is built — and the point of it is
        // that the two columns differ: same acts, same lengths, different maps.
        if (userArgs.Contains("--smoke-generators"))
        {
            ReportGenerators(blueprint);
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
        if (userArgs.Any(a => a is "--smoke-run" or "--smoke-map" or "--smoke-full" or "--smoke-timing" or "--smoke-reward" or "--smoke-target" or "--smoke-draw" or "--smoke-statuses" or "--smoke-shop" or "--smoke-event" or "--smoke-rest" or "--smoke-upgrade" or "--smoke-marathon" or "--smoke-ambush" or "--smoke-elite" or "--smoke-crowd" or "--smoke-boss" or "--smoke-tooltips" or "--smoke-format" or "--smoke-shelf" or "--smoke-deck" or "--smoke-window" or "--smoke-bug-run" or "--smoke-quit"))
        {
            host.StartNewRun(seed: 7,
                health: userArgs.Any(a => a is "--smoke-marathon" or "--smoke-crowd" or "--smoke-boss") ? 9999 : null,
                // Every session probe walks the DESIGN, which since the map rework is v0.0.1 — and `--legacy`
                // walks the same probe over the old maps instead. A probe that cannot name its generator is a
                // probe that cannot say whether what it found is about the map or about the game.
                mapGenerator: userArgs.Contains("--legacy") ? MapGenerators.RuleBased : MapGenerators.Strategic);
            CallDeferred(nameof(GoToSession));
            return;
        }

        BuildTitle(host);

        if (userArgs.Contains("--smoke-title") && !DisplayServer.GetName().Contains("headless"))
            _ = CaptureTitleThenQuit();
        // The one question "New run ▸" asks, opened the way a player opens it. A picture, because what this
        // window has to get right is not arithmetic — it is whether two sentences make the choice clear.
        if (userArgs.Contains("--smoke-newrun") && !DisplayServer.GetName().Contains("headless"))
        {
            NewRunPanel.Open(this, _ => { });
            _ = CaptureThenQuit("user://smoke-newrun.png");
        }
        // The settings dialog, opened the way a player opens it, with a picture of what they get.
        if (userArgs.Contains("--smoke-settings") && !DisplayServer.GetName().Contains("headless"))
        {
            OpenSettings();
            _ = CaptureThenQuit("user://smoke-settings.png");
        }
        // THE REPORT, SENT. The one screen whose whole job is to leave the machine, so the probe does not stop
        // at a picture of the form: it fills the box in, presses Send, and then says what actually landed in the
        // folder — four files or it is not a report. Point BNB_BUGREPORT_WEBHOOK at a listener and the same run
        // exercises the upload for real.
        if (userArgs.Contains("--smoke-bug") && !DisplayServer.GetName().Contains("headless"))
            _ = SmokeBugReport();
    }

    // WHAT THE PLAYER IS ACTUALLY CHOOSING BETWEEN, in numbers, for both answers the dialog offers. Every act
    // of a whole run is laid out twice from one seed and its rooms are counted, because "the maps are different"
    // is the claim the dialog makes on the title screen and this is the only place it is checked from the side
    // the player stands on — through the shipped document, the way Godot loads it.
    //
    // It reports rather than asserts, with one exception: an act that comes out EMPTY is a run nobody can play,
    // and the exit code says so. Everything else is for reading.
    // The first button under `root` whose text contains `text`. Depth-first, because a dialog puts its buttons
    // inside a margin inside a column and a probe should not have to know that.
    private static Button? FindButton(Godot.Node? root, string text)
    {
        if (root is null)
            return null;
        if (root is Button button && button.Text.Contains(text, StringComparison.Ordinal))
            return button;
        foreach (var child in root.GetChildren())
            if (FindButton(child, text) is { } found)
                return found;
        return null;
    }

    private static readonly HashSet<string> RoleTags = new(StringComparer.Ordinal)
    {
        MapNodeTags.Combat, MapNodeTags.MultiCombat, MapNodeTags.Elite, MapNodeTags.Boss, MapNodeTags.Mimic,
        MapNodeTags.Shop, MapNodeTags.Rest, MapNodeTags.Event, MapNodeTags.Treasure, MapNodeTags.Workbench,
    };

    private void ReportGenerators(RunBlueprint blueprint)
    {
        const int seed = 20260914;
        var broken = 0;
        foreach (var generator in new[] { MapGenerators.RuleBased, MapGenerators.Strategic })
        {
            GD.Print($"smoke-generators: {generator} ({RunPreferences.Title(generator)})");
            IReadOnlyList<RunActPlan> plan;
            try
            {
                plan = blueprint.BuildActPlan(seed, startingLoadout: 0, generator);
            }
            catch (Exception ex)
            {
                GD.Print($"    COULD NOT LAY OUT A RUN: {ex.Message.Split('\n')[0]}");
                broken++;
                continue;
            }

            for (var act = 0; act < plan.Count; act++)
            {
                var map = plan[act].Map;
                var rows = map.Nodes.Count == 0
                    ? 0
                    : map.Nodes.Select(node => node.Id.Value.Split('c')[0]).Distinct().Count();
                // A room's role is a tag on it (MapNodeTags), which is what both generators write and what the
                // map screen reads — so counting those counts what the player will actually walk past.
                var rooms = map.Nodes
                    .SelectMany(node => node.Tags.Where(RoleTags.Contains))
                    .GroupBy(tag => tag)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => $"{group.Key} {group.Count()}");
                GD.Print($"    act {act + 1}: {rows} rows, {map.Nodes.Count} rooms — {string.Join(", ", rooms)}");
                if (map.Nodes.Count == 0)
                    broken++;
            }
        }
        broken += ResumeKeepsItsMap(blueprint);

        GD.Print(broken == 0
            ? "smoke-generators: both generators lay out every act, and a resumed run keeps the map it had"
            : $"smoke-generators: FAILED — {broken} problem(s)");
        if (broken > 0)
            GetTree().Quit(1);
    }

    // THE THING THAT WOULD ACTUALLY RUIN A PLAYER'S EVENING. A BnB map is never stored — it is regenerated from
    // the run's seed — so a generator choice that lived in the menu instead of in the run would hand a resumed
    // run a different map: you close the game standing in front of an elite and come back to a shop. The engine
    // carries the choice in the save (`RunSaveData.MapGenerator`) and this is that promise checked from the
    // side the player stands on, through the shipped document, with no file and no menu in the way.
    private static int ResumeKeepsItsMap(RunBlueprint blueprint)
    {
        var broken = 0;
        foreach (var generator in new[] { MapGenerators.RuleBased, MapGenerators.Strategic })
        {
            using var play = new RunPlayback(() => { });
            play.Start(blueprint, seed: 4711, interactive: true, mapGenerator: generator);
            var before = MapSignature(play);
            var json = play.SaveJson();
            if (json is null)
            {
                GD.Print($"    {generator}: the run would not save — {play.Error}");
                broken++;
                continue;
            }

            using var resumed = new RunPlayback(() => { });
            resumed.Resume(blueprint, RunSaveJson.FromJson(json), interactive: true);
            var after = MapSignature(resumed);
            var same = before == after && before.Length > 0;
            GD.Print($"    {generator}: saved and resumed — the map is "
                + (same ? "the same one" : "A DIFFERENT MAP"));
            if (!same)
                broken++;
        }
        return broken;
    }

    // The map as one string: every room, what stands in it, and every edge. Enough that a map which came back
    // with the same shape but a different fight in room four reads as a different map, because it is one.
    private static string MapSignature(RunPlayback play)
    {
        if (play.Session?.Run.Map is not { } map)
            return "";
        return string.Join("|", map.Nodes.Select(node => $"{node.Id.Value}:{node.Payload}:{string.Join(",", node.Tags)}"))
            + "##" + string.Join("|", map.Edges.Select(edge => $"{edge.From.Value}>{edge.To.Value}"));
    }

    // An unfilled slot is NORMAL, so this probe cannot fail on a count — it reports one. What it does assert
    // is the rule that keeps the count honest: an upgraded card must ask for its base card's picture, or 413
    // cards would want 413 pictures instead of the 254 the table lists.
    private static void ReportArt(RunBlueprint blueprint)
    {
        var cards = blueprint.Cards.Select(c => c.Id).Where(id => !id.EndsWith('+'))
            .Distinct(StringComparer.Ordinal).ToList();
        var relics = blueprint.Relics.Select(r => r.Id).Distinct(StringComparer.Ordinal).ToList();
        // The bodies come from the manifest rather than from the encounters: it is the manifest that declares
        // a picture, and an enemy nothing offers is not a slot.
        var bodies = blueprint.Presentation.Enemies.Keys.ToList();
        // ⚠ AND THE PLAYER'S OWN. The document declares a picture for every character and this count had three
        // folders in it while the document has four — so the one slot nothing was ever going to notice was the
        // one on the very first screen of the game.
        var heroes = blueprint.Presentation.Characters.Keys.ToList();
        var filledCards = cards.Count(id => CardVisuals.CardArt(id) is not null);
        var filledRelics = relics.Count(id => CardVisuals.RelicArt(id) is not null);
        var filledBodies = bodies.Count(id => CardVisuals.EnemyArt(id) is not null);
        var filledHeroes = heroes.Count(id => CardVisuals.CharacterArt(id) is not null);
        var strays = blueprint.Cards.Select(c => c.Id).Where(id => id.EndsWith('+'))
            .Where(id => CardVisuals.SlotPath("cards", id) != CardVisuals.SlotPath("cards", id.TrimEnd('+')))
            .ToList();

        GD.Print($"smoke-art: {cards.Count + relics.Count + bodies.Count + heroes.Count} slots asked for "
            + $"({cards.Count} cards, {relics.Count} relics, {bodies.Count} bodies, {heroes.Count} characters), "
            + $"{filledCards + filledRelics + filledBodies + filledHeroes} filled, "
            + $"{strays.Count} upgrade(s) asking for a picture of their own");
        foreach (var id in strays.Take(5))
            GD.Print($"  STRAY {id} asks for {CardVisuals.SlotPath("cards", id)}");
        foreach (var id in cards.Where(id => CardVisuals.CardArt(id) is null).Take(2))
            GD.Print($"  waiting: assets/art/{CardVisuals.SlotPath("cards", id)}");
        foreach (var id in relics.Where(id => CardVisuals.RelicArt(id) is null).Take(2))
            GD.Print($"  waiting: assets/art/{CardVisuals.SlotPath("relics", id)}");
        foreach (var id in bodies.Where(id => CardVisuals.EnemyArt(id) is null).Take(2))
            GD.Print($"  waiting: assets/art/{CardVisuals.SlotPath("enemies", id)}");
        foreach (var id in heroes.Where(id => CardVisuals.CharacterArt(id) is null).Take(2))
            GD.Print($"  waiting: assets/art/{CardVisuals.SlotPath("characters", id)}");
    }

    private async System.Threading.Tasks.Task CaptureThenQuit(string file)
    {
        for (var i = 0; i < 4; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print($"smoke: screenshot {file}");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task SmokeBugReport()
    {
        for (var i = 0; i < 4; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        OpenBugReport();
        var panel = GetNodeOrNull(BugReportPanel.OverlayName)
            ?.FindChild(nameof(BugReportPanel), recursive: true, owned: false) as BugReportPanel;
        if (panel is null)
        {
            GD.Print("smoke-bug: the window did not open");
            GetTree().Quit();
            return;
        }
        panel.Fill("--smoke-bug probe: not a real report. (Sent to prove the window, the four "
            + "attachments and the upload; a real one says what actually went wrong.)");
        for (var i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng("user://smoke-bug.png");

        GD.Print($"smoke-bug: webhook={(BugReport.Webhook() is null ? "none (local only)" : "configured")}");
        await panel.Submit();
        for (var i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng("user://smoke-bug-sent.png");

        ReportFolders();
        GD.Print("smoke: screenshot user://smoke-bug.png + user://smoke-bug-sent.png");
        GetTree().Quit();
    }

    // What a report is made of, on disk: the probe's real assertion. A window that looks right and writes an
    // empty folder has reported nothing.
    private static void ReportFolders()
    {
        if (DirAccess.Open(BugReport.Folder) is not { } dir)
        {
            GD.Print($"smoke-bug: NO FOLDER at {BugReport.Folder}");
            return;
        }
        var folders = dir.GetDirectories();
        GD.Print($"smoke-bug: {folders.Length} report(s) kept (at most {BugReport.KeepFolders})");
        foreach (var folder in folders[^System.Math.Min(1, folders.Length)..])
        {
            var path = $"{BugReport.Folder}/{folder}";
            var files = DirAccess.Open(path)?.GetFiles() ?? [];
            var sizes = files.Select(f =>
            {
                using var file = Godot.FileAccess.Open($"{path}/{f}", Godot.FileAccess.ModeFlags.Read);
                return $"{f} {file?.GetLength() ?? 0}B";
            });
            GD.Print($"  {folder}: {files.Length} files — {string.Join(" · ", sizes)}");
        }
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
        // The one question a new run asks: which map generator lays it out. Both ship and they make different
        // games, so the answer cannot be a build-time constant — and it is asked HERE rather than in Settings
        // because it belongs to the run being started, not to the machine (see NewRunPanel).
        start.Pressed += () => NewRunPanel.Open(this, generator =>
        {
            host.StartNewRun(seed: (int)(Time.GetUnixTimeFromSystem() % int.MaxValue), _selectedCharacter,
                mapGenerator: generator);
            GoToSession();
        });
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

        var settings = new Button { Text = "Settings", CustomMinimumSize = new Vector2(140, 44) };
        settings.Pressed += OpenSettings;
        actions.AddChild(settings);

        var quit = new Button { Text = "Quit", CustomMinimumSize = new Vector2(120, 44) };
        quit.Pressed += () => GetTree().Quit();
        actions.AddChild(quit);
        root.AddChild(actions);

        // A QUIETER SECOND ROW, on purpose. Reporting a bug is not one of the four things anybody came to this
        // screen to do, so it does not get a button the size of "New run" — but it has to be reachable with no
        // run at all, because the bug that stops somebody from starting one can only be reported from here.
        var aside = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        var bug = new Button
        {
            Text = "🐞  Report a bug",
            Flat = true,
            CustomMinimumSize = new Vector2(0, 32),
            TooltipText = "Send what went wrong, with your save and a picture of the screen.",
        };
        bug.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        bug.Pressed += OpenBugReport;
        aside.AddChild(bug);
        root.AddChild(aside);
    }

    private Control CharacterCard(GameHost host, RunCharacter character, bool unlocked)
    {
        var presentation = host.Blueprint.Presentation.Characters.GetValueOrDefault(character.Id);
        var selected = _selectedCharacter == character.Id;

        // 330, not the 220 this was: the picture takes 72 of the width and the flavour line needs the rest, or
        // "Armed with forms, stamps, and a fireproof sense of procedure" comes out six lines tall and pushes the
        // roster off the bottom of the screen.
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(330, 140) };
        panel.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(
            selected ? MoonvineTheme.BgControl : MoonvineTheme.BgPanel,
            selected ? MoonvineTheme.AccentLight : unlocked ? new Color(MoonvineTheme.Accent, 0.3f) : new Color(MoonvineTheme.TextMuted, 0.2f)));

        // WHO YOU ARE ABOUT TO BE, as a body and not only as a name. The roster is the first choice the game
        // asks of anybody, and it was the last one still made entirely out of words.
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        if (CardVisuals.CharacterArt(character.Id) is { } portrait)
        {
            var picture = new TextureRect
            {
                Texture = portrait,
                CustomMinimumSize = new Vector2(72, 110),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            if (!unlocked)
                picture.Modulate = new Color(1, 1, 1, 0.35f);
            row.AddChild(picture);
        }

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
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
        row.AddChild(column);
        panel.AddChild(row);

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

    private void OpenSettings()
    {
        if (GetNodeOrNull("SettingsOverlay") is not null)
            return;
        BugReport.Remember(GetViewport());   // before the veil — see BugReport.Remember
        var overlay = SettingsPanel.Overlay(
            () => GetNodeOrNull("SettingsOverlay")?.QueueFree(),
            OpenBugReport);
        overlay.Name = "SettingsOverlay";
        AddChild(overlay);
    }

    // Either the button on the title screen (nothing covers the game, so capture now) or the one inside the
    // settings window (which captured the screen when IT opened, so do not capture this window).
    private void OpenBugReport()
    {
        if (GetNodeOrNull("SettingsOverlay") is { } settings)
            settings.QueueFree();
        else
            BugReport.Remember(GetViewport());
        BugReportPanel.Open(this);
    }

    private void GoToSession() => GetTree().ChangeSceneToFile("res://scenes/Session.tscn");
}
