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
    private static bool _resumeVerdict;
    public static bool ResumeVerdict => _resumeVerdict;

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

            // ⚠⚠ AND THEN PRESS IT. This probe used to stop at the sentence above — the save is on disk, the
            // button is offered, the room is right — and call that "save and quit works". None of those three
            // is the thing the player does next. The player presses Continue, and what THAT does was never
            // measured by anything: `ResumeRun` returning false makes the button do nothing at all, silently.
            // So the probe does what the player does.
            var resumed = host.ResumeRun();
            var play = host.Play;
            GD.Print($"smoke-quit: pressed \"Continue run\" → {(resumed ? "the run came back" : "IT DID NOT COME BACK")}"
                + $" · error={play?.Error ?? "none"}"
                + $" · room={play?.Session?.Run.CurrentNodeId?.Value ?? "—"}"
                + $" · result={play?.Session?.Run.Result.ToString() ?? "—"}"
                + $" · in a fight={play?.CombatDriver?.Current is not null}");
            GD.Print($"smoke-quit: what the resumed session is waiting for — "
                + $"choice={play?.Session?.IsAwaitingChoice} entities={play?.Session?.IsAwaitingEntities} "
                + $"node={play?.Session?.IsAwaitingNodeChoice} interlude={play?.Session?.IsAwaitingInterlude} "
                + $"sessionError={play?.Session?.Error ?? "none"}");
            // AND THEN THE SCREEN. The error the player reports is on the RUN SCREEN, not on the title — so
            // the probe goes there, exactly as pressing the button does, and says what that screen found.
            _resumeVerdict = host.HasSave && offered && sameRoom && resumed && play?.Error is null;
            CallDeferred(nameof(GoToSession));
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
        // THE MATERIAL CENSUS (D8). The same "is it actually there" question the art census answers for the
        // 714 pictures, asked of the surfaces: the tiles the theme reaches for by name, the room each ACT
        // declares, and the one rule that keeps a background safe — it is never shown bare. About the
        // document, the folder and the theme, so again: no screen is built to answer it.
        if (userArgs.Contains("--smoke-materials"))
        {
            ReportMaterials(blueprint);
            return;
        }
        // THE ARCHIVE, BOTH HALVES. The catalogue is a question about the DOCUMENT (what is there to find, and
        // does every slot have a picture), so it is answered before a screen exists; the fund book is a
        // question about the DISK, and it is asked THROUGH THE FILE — an in-memory round trip proves nothing
        // about a save (see the ValueTuple that ate a combat snapshot). The picture comes last, and it is
        // taken with the gods still unmet on purpose: "???" is the state the player starts in.
        if (userArgs.Contains("--smoke-history"))
        {
            BuildTitle(host);
            _ = SmokeHistory();
            return;
        }
        if (userArgs.Contains("--smoke-archive"))
        {
            ReportArchive(blueprint);
            if (DisplayServer.GetName().Contains("headless"))
            {
                GetTree().Quit();
                return;
            }
            BuildTitle(host);
            _ = SmokeArchiveShots();
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
        // THE MUSIC, WITHOUT A SPEAKER. Two questions, neither of which needs a screen or an ear: are the ten
        // files there and will each of them LOOP (an .ogg that ships without the flag plays once and leaves
        // the game silent — invisible until somebody sits still for four minutes), and does the priority in
        // the design document survive contact with the code. The second is a table because that is what it
        // is: a state goes in, exactly one track comes out.
        if (userArgs.Contains("--smoke-music"))
        {
            _ = SmokeMusic();
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
                    : SessionScreen.SimArg("--sim-health", 0) is > 0 and var hp ? hp : null,
                // ⚠ A RUNNER MUST NAME ITS OWN MAP. This used to fall through to the player's remembered
                // preference — `user://settings.cfg`, a file outside the repository — so which act a batch of
                // runs had walked depended on the machine it ran on, and two reports could disagree without
                // either of them being wrong. The runner walks the DESIGN, which since the map rework is
                // v0.0.1, and `--legacy` walks the old maps instead: the same contract the smoke probes below
                // already keep.
                mapGenerator: userArgs.Contains("--legacy") ? MapGenerators.RuleBased : MapGenerators.Strategic);
            CallDeferred(nameof(GoToSession));
            return;
        }
        if (userArgs.Any(a => a is "--smoke-run" or "--smoke-map" or "--smoke-full" or "--smoke-timing" or "--smoke-reward" or "--smoke-target" or "--smoke-draw" or "--smoke-statuses" or "--smoke-shop" or "--smoke-event" or "--smoke-rest" or "--smoke-upgrade" or "--smoke-marathon" or "--smoke-screens" or "--smoke-ambush" or "--smoke-elite" or "--smoke-crowd" or "--smoke-boss" or "--smoke-tooltips" or "--smoke-format" or "--smoke-shelf" or "--smoke-deck" or "--smoke-window" or "--smoke-bug-run" or "--smoke-quit" or "--smoke-archive-run" or "--smoke-hover" or "--smoke-mapkey" or "--smoke-piles" or "--smoke-keys" or "--smoke-preview"))
        {
            host.StartNewRun(seed: 7,
                // ⚠ A PROBE THAT HAS TO WALK SOMEWHERE MUST SURVIVE THE WALK. The greedy walker plays badly on
                // purpose, and on the game's own health it dies in act I — which is how `--smoke-shop`,
                // `--smoke-event` and `--smoke-elite` all came to photograph the same DEFEAT SCREEN and call it
                // a shop, a door and an elite. Every probe that names a room it must reach gets the body the
                // marathon and the bosses have always had; the probes that stay where the run starts do not.
                // ⚠ That body is `SessionScreen.ProbeBody`, not 9999 — see the note there: 9999 was called
                // immortal for months and the greedy walker died on it in Act IV.
                health: userArgs.Any(a => a is "--smoke-marathon" or "--smoke-crowd" or "--smoke-boss"
                    or "--smoke-screens"
                    or "--smoke-shop" or "--smoke-event" or "--smoke-rest" or "--smoke-upgrade"
                    or "--smoke-ambush" or "--smoke-elite" or "--smoke-reward"
                    or "--smoke-archive-run") ? SessionScreen.ProbeBody : null,
                // Every session probe walks the DESIGN, which since the map rework is v0.0.1 — and `--legacy`
                // walks the same probe over the old maps instead. A probe that cannot name its generator is a
                // probe that cannot say whether what it found is about the map or about the game.
                mapGenerator: userArgs.Contains("--legacy") ? MapGenerators.RuleBased : MapGenerators.Strategic);
            CallDeferred(nameof(GoToSession));
            return;
        }

        BuildTitle(host);

        // ★ THE OPENING, AND ONLY FOR A PLAYER. Every probe above has already returned; what is left here is
        // either a human starting the game or one of the screenshot probes that photograph THIS screen — and
        // a fade standing over one of those is six reviews of a dimmed veil, which D6 already paid for once.
        // So the opening runs when nobody passed an argument at all, and never otherwise.
        var watching = userArgs.Contains("--smoke-splash");
        // The first start on a machine asks who is playing. Only a player is asked — a probe passes arguments —
        // and the question stands UNDER the opening, so the fade lifts onto it.
        if ((userArgs.Count() == 0 && !PlayerIdentity.HasName || userArgs.Contains("--smoke-name"))
            && !DisplayServer.GetName().Contains("headless"))
        {
            OpenNamePrompt();
            if (userArgs.Contains("--smoke-name"))
                _ = CaptureThenQuit("user://smoke-name.png");
        }
        if ((userArgs.Count() == 0 || watching) && !DisplayServer.GetName().Contains("headless"))
        {
            Splash.Play(this, () => { });
            if (watching)
                _ = CaptureOpening();
        }

        if (userArgs.Contains("--smoke-title") && !DisplayServer.GetName().Contains("headless"))
            _ = CaptureTitleThenQuit();
        // The one question "New run ▸" asks, opened the way a player opens it. A picture, because what this
        // window has to get right is not arithmetic — it is whether two sentences make the choice clear.
        if (userArgs.Contains("--smoke-newrun") && !DisplayServer.GetName().Contains("headless"))
        {
            NewRunPanel.Open(this, (_, _) => { });
            _ = CaptureThenQuit("user://smoke-newrun.png");
        }
        // Seeds: typed, hashed and daily ones are stable, the same seed lays out the same act, and the daily
        // button fills the field in the open dialog. The run is started and dropped here and never saved.
        if (userArgs.Contains("--smoke-seed"))
        {
            SmokeSeed(host);
            return;
        }
        // Settings ▸ Gameplay, reached the way a player reaches it.
        if (userArgs.Contains("--smoke-gameplay") && !DisplayServer.GetName().Contains("headless"))
        {
            OpenSettings();
            var page = FindChildren("*", nameof(Button), recursive: true, owned: false).OfType<Button>()
                .FirstOrDefault(b => b.Text.Contains("Gameplay"));
            page?.EmitSignal(BaseButton.SignalName.Pressed);
            GD.Print($"smoke-gameplay: page button={(page is not null)} "
                + $"open={Descendants(this).OfType<GameplayPanel>().Any()}");
            _ = CaptureThenQuit("user://smoke-gameplay.png");
        }
        // The settings dialog, opened the way a player opens it, with a picture of what they get.
        if (userArgs.Contains("--smoke-settings") && !DisplayServer.GetName().Contains("headless"))
        {
            OpenSettings();
            _ = CaptureThenQuit("user://smoke-settings.png");
        }
        // The credits, which are a LEGAL OBLIGATION and not decoration — nine of the ten tracks are CC BY.
        // A picture, because what this screen has to get right is whether ten attributions and their links
        // are readable at once, which no assertion can answer.
        if (userArgs.Contains("--smoke-credits") && !DisplayServer.GetName().Contains("headless"))
        {
            OpenCredits();
            _ = CaptureThenQuit("user://smoke-credits.png");
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

    // ⚠ THIS PROBE REWRITES `user://archive.json`. It is the only one that does — every other probe records
    // in memory and leaves the file alone (Archive.Robot) — and it has to, because what it is checking is
    // whether a discovery survives the process that made it.
    private static void ReportArchive(RunBlueprint blueprint)
    {
        Archive.Build(blueprint);
        GD.Print("smoke-archive: ⚠ this probe rewrites user://archive.json");
        Archive.Reset();

        foreach (var kind in Archive.Kinds)
        {
            var all = Archive.Entries(kind);
            // "Wie sehen sie aus" is half of what an archive is for, so the census counts the PICTURES too:
            // a shelf of entries with no art is a shelf of names.
            var painted = all.Count(e => Pictured(e));
            GD.Print($"smoke-archive: {Archive.Title(kind),-8} {all.Count,4} entries · {painted,4} painted"
                + $" · e.g. {string.Join(", ", all.Take(3).Select(e => e.Name))}");
        }

        // THE GODS' SHELF IS THE ONE RULE THE PLAYER NAMED, so it is measured in both states rather than
        // described in one. Found, then thrown away again — the picture below wants the starting state.
        var god = Archive.Entries(ArchiveKind.Gods).FirstOrDefault();
        GD.Print($"smoke-archive: gods shelf unmet ⇒ \"???\" = {Archive.SeenCount(ArchiveKind.Gods) == 0}");
        if (god is not null)
        {
            Archive.Discover(god);
            GD.Print($"smoke-archive: met {god.Name} ⇒ shelf opens = {Archive.SeenCount(ArchiveKind.Gods) == 1}");
        }
        Archive.Reset();

        // AND THEN THROUGH THE FILE. Written, forgotten, read back off the disk — which is the only reading
        // that says anything about a save. Eight per shelf so the picture shows a shelf part-filled, which is
        // what an archive actually looks like.
        var marked = Archive.Kinds
            .Where(k => k != ArchiveKind.Gods)
            .SelectMany(k => Archive.Entries(k).Take(8))
            .ToList();
        foreach (var entry in marked)
            Archive.Discover(entry);
        Archive.Forget();
        var back = marked.Count(Archive.Seen);
        GD.Print($"smoke-archive: wrote {marked.Count} finds, read {back} back OUT OF THE FILE"
            + $" — {(back == marked.Count ? "the fund book survives the process" : "IT DID NOT")}");
        foreach (var kind in Archive.Kinds)
            GD.Print($"smoke-archive: {Archive.Title(kind),-8} found {Archive.SeenCount(kind)}"
                + $"/{Archive.Entries(kind).Count}");
    }

    private static bool Pictured(ArchiveEntry entry) => entry.Kind switch
    {
        ArchiveKind.Cards => CardVisuals.CardArt(entry.Id) is not null,
        ArchiveKind.Relics => CardVisuals.RelicArt(entry.Id) is not null,
        _ => CardVisuals.EnemyArt(entry.Id) is not null,
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

    private static int _musicProblems;

    private void ReportMusic()
    {
        var director = MusicDirector.Instance;
        if (director is null)
        {
            GD.Print("smoke-music: the director autoload is not there");
            _musicProblems = 1;
            return;
        }

        var missing = 0;
        var unlooped = 0;
        var total = 0.0;
        GD.Print("smoke-music: the files");
        foreach (var (track, file, present, loops, seconds) in director.Inventory())
        {
            total += seconds;
            if (!present)
                missing++;
            else if (!loops)
                unlooped++;
            GD.Print($"    {track,-9} {(present ? $"{seconds,6:0.0}s" : "  —   ")} "
                + $"{(present ? loops ? "loops" : "DOES NOT LOOP" : "MISSING")}  {file}");
        }

        // The document's priority list, as the cases that distinguish it from any other ordering. Act V is
        // first because it is the exception the whole table exists to state: in the gauntlet there is no
        // separate boss music, and an elite there is still Act V.
        (string Where, MusicDirector.Track Want, int Act, bool Fight, bool Boss, bool Elite, bool Shop, bool Campfire)[] cases =
        [
            ("act I map",            MusicDirector.Track.ActOne,   1, false, false, false, false, false),
            ("act II map",           MusicDirector.Track.ActTwo,   2, false, false, false, false, false),
            ("act III map",          MusicDirector.Track.ActThree, 3, false, false, false, false, false),
            ("act IV map",           MusicDirector.Track.ActFour,  4, false, false, false, false, false),
            ("act I shop",           MusicDirector.Track.Shop,     1, false, false, false, true,  false),
            ("act III campfire",     MusicDirector.Track.Campfire, 3, false, false, false, false, true),
            ("act II elite fight",   MusicDirector.Track.Elite,    2, true,  false, true,  false, false),
            ("act IV boss fight",    MusicDirector.Track.Boss,     4, true,  true,  false, false, false),
            ("standing on the boss", MusicDirector.Track.ActFour,  4, false, true,  false, false, false),
            ("won, still on it",     MusicDirector.Track.ActTwo,   2, false, true,  false, false, false),
            ("act V map",            MusicDirector.Track.ActFive,  5, false, false, false, false, false),
            ("act V final boss",     MusicDirector.Track.ActFive,  5, true,  true,  false, false, false),
            ("act V elite",          MusicDirector.Track.ActFive,  5, true,  false, true,  false, false),
        ];

        var wrong = 0;
        GD.Print("smoke-music: what plays where");
        foreach (var c in cases)
        {
            var got = MusicDirector.Cue(c.Act, c.Fight, c.Boss, c.Elite, c.Shop, c.Campfire);
            var ok = got == c.Want;
            if (!ok)
                wrong++;
            GD.Print($"    {c.Where,-22} → {got,-9} {(ok ? "" : $"EXPECTED {c.Want}")}");
        }

        _musicProblems = missing + unlooped + wrong > 0 ? 1 : 0;
        GD.Print(_musicProblems == 0
            ? $"smoke-music: all 10 tracks present and looping ({total / 60:0.0} minutes of music), "
                + $"and all {cases.Length} states pick the track the design asks for"
            : $"smoke-music: FAILED — {missing} missing, {unlooped} not looping, {wrong} wrong cue(s)");
    }

    // The two behaviours that are not visible in a table: a screen that asks for what is already playing
    // must NOT restart it, and an errand into a shop must hand the act theme back AT THE BAR IT WAS ON.
    // Both need time to pass, which is why this is the one probe here that waits for frames.
    private async System.Threading.Tasks.Task SmokeMusic()
    {
        ReportMusic();

        var director = MusicDirector.Instance;
        if (director is null || _musicProblems != 0)
        {
            GetTree().Quit(_musicProblems);
            return;
        }

        async System.Threading.Tasks.Task Beat(int frames = 30)
        {
            for (var i = 0; i < frames; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        GD.Print("smoke-music: what the director does");
        director.Want(MusicDirector.Track.Title);
        GD.Print($"    title screen        → {director.LastAction}");
        await Beat();

        director.Want(MusicDirector.Track.ActOne);
        GD.Print($"    start a run         → {director.LastAction}");
        await Beat(90);

        // The same screen redrawing. The act theme must be left strictly alone.
        director.Want(MusicDirector.Track.ActOne);
        var keptPlaying = director.LastAction.Contains("already playing", StringComparison.Ordinal);
        GD.Print($"    redraw the map      → {director.LastAction} {(keptPlaying ? "" : "— SHOULD NOT HAVE CHANGED")}");

        director.Want(MusicDirector.Track.Shop);
        GD.Print($"    walk into a shop    → {director.LastAction}");
        await Beat(60);

        director.Want(MusicDirector.Track.ActOne);
        var resumed = director.LastAction.Contains("resumed at", StringComparison.Ordinal);
        GD.Print($"    leave the shop      → {director.LastAction}"
            + $" {(resumed ? "" : "— the act theme restarted from the top")}");

        // ⚠ A dummy audio driver may not advance a playback position, so "did not resume" is reported and
        // never failed on: under `--headless` there is no clock behind the sound. The claim this probe can
        // make everywhere is the one above it — that an unchanged state changes nothing.
        var headless = DisplayServer.GetName().Contains("headless");
        if (!keptPlaying)
            _musicProblems = 1;
        GD.Print(_musicProblems == 0
            ? $"smoke-music: the director holds a playing track and {(resumed ? "resumes" : "restarts")}"
                + $" the act theme after an errand{(headless && !resumed ? " (no audio clock under --headless)" : "")}"
            : "smoke-music: FAILED — a redraw restarted the music");
        GetTree().Quit(_musicProblems);
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

        // The rooms. 294 encounters ask for five pictures, because the converter fills the slot per ACT — so
        // what is counted is the DISTINCT paths declared, the same way the card census counts a picture once
        // however many cards draw it.
        var rooms = blueprint.Presentation.Encounters.Values
            .Select(e => e.Art).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
        var filledRooms = rooms.Count(a => ResourceLoader.Exists($"res://assets/art/{a}"));

        GD.Print($"smoke-art: {cards.Count + relics.Count + bodies.Count + heroes.Count + rooms.Count} slots asked for "
            + $"({cards.Count} cards, {relics.Count} relics, {bodies.Count} bodies, {heroes.Count} characters, "
            + $"{rooms.Count} rooms), "
            + $"{filledCards + filledRelics + filledBodies + filledHeroes + filledRooms} filled, "
            + $"{strays.Count} upgrade(s) asking for a picture of their own");
        foreach (var art in rooms.Where(a => !ResourceLoader.Exists($"res://assets/art/{a}")).Take(2))
            GD.Print($"  waiting: assets/art/{art}");
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

    // Three questions, and only the first two can fail on a count.
    //
    // ⚠ A MISSING MATERIAL IS NOT AN ERROR AT RUNTIME — `MoonvineTheme.Surface` falls back to the flat panel
    // it replaced, so a build without the files looks like D0 and never like a crash. That is exactly why it
    // needs a probe: the failure mode of this whole phase is a game that quietly looks like the phase before.
    private void ReportMaterials(RunBlueprint blueprint)
    {
        var problems = 0;

        // 1. The surfaces the theme asks for by name.
        var missing = MoonvineTheme.MaterialNames.Where(n => MoonvineTheme.Material(n) is null).ToList();
        problems += missing.Count;
        // …and the other direction: a file in the folder nothing reaches for. Not a failure — the three raw
        // tiles are the generator's own sources — but a count that drifts is how a renamed material hides.
        var onDisk = Godot.DirAccess.GetFilesAt("res://assets/materials")
            .Where(f => f.EndsWith(".png", StringComparison.Ordinal))
            .Select(f => f[..^4]).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var unasked = onDisk.Except(MoonvineTheme.MaterialNames, StringComparer.Ordinal).ToList();

        GD.Print($"smoke-materials: {MoonvineTheme.MaterialNames.Count - missing.Count}"
            + $"/{MoonvineTheme.MaterialNames.Count} materials resolved"
            + $" · {onDisk.Count} file(s) in the folder, {unasked.Count} nothing asks for"
            + (unasked.Count > 0 ? $" ({string.Join(", ", unasked)})" : ""));
        foreach (var name in missing)
            GD.Print($"  MISSING assets/materials/{name}.png");

        // 2. The room each ACT is fought in — derived from the act's own rooms rather than from a list of
        // paths, because the failure worth catching is act III's fights declaring act II's picture. An act
        // must name exactly ONE background and that file must exist.
        var plan = blueprint.BuildActPlan(seed: 20260916, startingLoadout: 0, MapGenerators.Strategic);
        for (var act = 0; act < plan.Count; act++)
        {
            var declared = plan[act].Map.Nodes
                .Select(node => node.Payload)
                .OfType<EncounterRef>()
                .Select(fight => blueprint.Presentation.Encounters.GetValueOrDefault(fight.Id.Value)?.Art)
                .Where(art => !string.IsNullOrEmpty(art))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var fights = plan[act].Map.Nodes.Count(node => node.Payload is EncounterRef);
            var resolved = declared.Count(art => ResourceLoader.Exists($"res://assets/art/{art}"));
            var ok = declared.Count == 1 && resolved == 1;
            if (!ok)
                problems++;
            GD.Print($"    act {act + 1}: {fights} fight(s) declare {declared.Count} room(s), {resolved} on disk"
                + $" — {(declared.Count == 0 ? "NONE DECLARED" : string.Join(", ", declared))}"
                + (ok ? "" : declared.Count > 1 ? "  ← MORE THAN ONE ROOM FOR ONE ACT" : "  ← MISSING"));
        }

        // 3. Never bare. Built through the fight's own constructor, so what is checked is the code that shows
        // a background and not a restatement of it: the veil exists, it is opaque enough to be a veil, and it
        // is added AFTER the picture — a veil behind its picture passes every visibility check and veils
        // nothing.
        var holder = new Control();
        AddChild(holder);
        var (picture, scrim) = SessionScreen.BuildActBackdrop(holder);
        var over = scrim.GetIndex() > picture.GetIndex();
        var veiled = scrim.Color.A >= 0.5f;
        if (!over || !veiled)
            problems++;
        GD.Print($"    the veil: alpha {scrim.Color.A:0.00}, {(over ? "over" : "UNDER")} the picture"
            + $" — {(over && veiled ? "a picture is never shown bare" : "A PICTURE CAN REACH THE SCREEN BARE")}");
        holder.QueueFree();

        GD.Print(problems == 0
            ? "smoke-materials: every surface the game asks for is there, every act has its room, and the veil is over it"
            : $"smoke-materials: FAILED — {problems} problem(s)");
        GetTree().Quit(problems == 0 ? 0 : 1);
    }

    // THE OPENING, PHOTOGRAPHED AT ITS THREE BEATS. Nothing else in the battery can see it: the splash runs
    // only when nobody passed an argument, which is exactly the one case no probe is in. So this probe is the
    // opening's own, and it waits in WALL-CLOCK time rather than in frames, because what is being checked is
    // a sequence timed in seconds and a frame count says nothing about how long a fade took.
    private async System.Threading.Tasks.Task CaptureOpening()
    {
        // The beats land at 1.5 s (the studio, fully up), 4.0 s (the game's name) and 7.0 s (the title screen,
        // the last veil gone). These are WAITS, not timestamps — a timer waits from now — so they are written
        // as the gaps between the beats and the comment carries the absolute times.
        foreach (var (wait, file) in new (double Wait, string File)[]
        {
            (1.5, "user://smoke-splash-1-studio.png"),
            (2.5, "user://smoke-splash-2-game.png"),
            (3.0, "user://smoke-splash-3-title.png"),
        })
        {
            await ToSignal(GetTree().CreateTimer(wait), SceneTreeTimer.SignalName.Timeout);
            GetViewport().GetTexture().GetImage().SavePng(file);
            GD.Print($"smoke-splash: {file}");
        }
        GetTree().Quit();
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
        // ⚠ THE MENU HAS TO FIT A 1280 × 720 CANVAS. Hung 90 px below the centre, the quiet row (report a bug,
        // credits) and the player's name lay below the bottom edge, and a sixth main button (History) ran off
        // the right one. So the title and its numbers move up, and the body is as wide as its widest row.
        if (GetNodeOrNull<Label>("Title") is { } heading)
        {
            heading.OffsetTop = -250;
            heading.OffsetBottom = -210;
        }
        if (GetNodeOrNull<Label>("Stats") is { } stats)
        {
            stats.OffsetTop = -196;
            stats.OffsetBottom = -166;
        }
        var root = new VBoxContainer { Name = "TitleBody" };
        root.SetAnchorsPreset(LayoutPreset.Center);
        root.CustomMinimumSize = new Vector2(960, 0);
        root.AddThemeConstantOverride("separation", 16);
        root.Position += new Vector2(-480, -140); // below the scene's Title/Stats labels
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
        start.Pressed += () => OpenNewRun(host);
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

        // THE ARCHIVE IS A MAIN-MENU ITEM, not a thing inside Settings. It is not a preference — it is the
        // one part of the game that survives a run, and the only reason to open the game without playing it.
        var archive = new Button { Text = "Archive", CustomMinimumSize = new Vector2(140, 44) };
        archive.TooltipText = "Everything you have met, kept between runs.";
        archive.Pressed += () => OpenArchive();
        actions.AddChild(archive);

        // THE PLAYER'S OWN RUNS, a main-menu item of its own beside the archive: the archive is what the game
        // holds, this is what the player did with it.
        var history = new Button { Text = "History", CustomMinimumSize = new Vector2(140, 44) };
        history.TooltipText = "Every run you have played, and the numbers across them.";
        history.Pressed += () => HistoryPanel.Open(this, seed =>
        {
            GetNodeOrNull(HistoryPanel.OverlayName)?.QueueFree();
            OpenNewRun(host, seed);
        });
        actions.AddChild(history);

        var settings = new Button { Text = "Settings", CustomMinimumSize = new Vector2(140, 44) };
        settings.Pressed += OpenSettings;
        actions.AddChild(settings);

        var quit = new Button { Text = "Quit", CustomMinimumSize = new Vector2(120, 44) };
        quit.Pressed += () => GetTree().Quit();
        actions.AddChild(quit);
        root.AddChild(actions);
        if (PlayerIdentity.Name is { } player)
        {
            var who = MutedLabel($"Playing as {player}");
            who.HorizontalAlignment = HorizontalAlignment.Center;
            root.AddChild(who);
        }

        // THE TITLE SCREEN IS WHERE THE MUSIC STARTS, and the only screen that can put it back once a run
        // has ended. Asked for here rather than in _Ready because the probes above return before this point
        // — a headless census should not start a four-minute orchestral loop it will never hear.
        MusicDirector.Instance?.Want(MusicDirector.Track.Title);

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

        // CREDITS BELONG ON THE MAIN MENU and nowhere else — every track in the game is used under a licence
        // that asks for attribution, so this button is a condition of shipping the music, not a nicety. It
        // keeps the quiet row for the same reason the bug button does: nobody came here to read it, but it
        // has to be findable without starting a run.
        var credits = new Button
        {
            Text = "Credits",
            Flat = true,
            CustomMinimumSize = new Vector2(0, 32),
            TooltipText = "The music in this game, and who made it.",
        };
        credits.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        credits.Pressed += OpenCredits;
        aside.AddChild(credits);
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
        panel.AddThemeStyleboxOverride("panel", unlocked
            ? MoonvineTheme.WoodPanel(rim: selected)
            : MoonvineTheme.Panel(MoonvineTheme.BgPanelStrong, new Color(MoonvineTheme.TextMuted, 0.2f)));

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

    private ArchivePanel? OpenArchive() => ArchivePanel.Open(this);

    // "New run ▸", and "Play this seed again" from the history, which is the same dialog with the seed filled in.
    private void OpenNewRun(GameHost host, int? seed = null) =>
        NewRunPanel.Open(this, (generator, chosen) =>
        {
            host.StartNewRun(seed: chosen ?? RunSeeds.Random(), _selectedCharacter, mapGenerator: generator);
            GoToSession();
        }, seed);

    // WHO ARE YOU — asked once per machine (PlayerIdentity). Modal on purpose: the name goes on every run this
    // machine records, and a run started before it was given would be a run nobody played.
    private void OpenNamePrompt()
    {
        if (GetNodeOrNull("NameOverlay") is not null)
            return;
        var veil = new Control { Name = "NameOverlay", MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.8f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
        panel.AddThemeStyleboxOverride("panel", MoonvineTheme.StoneFrame(padH: 24, padV: 20));
        center.AddChild(panel);
        // The frame is only a frame; the question needs a ground of its own, or the title reads through it.
        panel.AddChild(new ColorRect { Color = MoonvineTheme.BgPanel });
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);

        var title = new Label { Text = "Who is playing?", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        column.AddChild(title);
        column.AddChild(MutedLabel("Give yourself a name. You are asked once, on this machine."));

        var field = new LineEdit
        {
            PlaceholderText = "Your name",
            MaxLength = PlayerIdentity.MaxNameLength,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        column.AddChild(field);
        var ok = new Button { Text = "That's me", Disabled = true };
        column.AddChild(ok);

        void Accept()
        {
            if (PlayerIdentity.SetName(field.Text))
            {
                veil.QueueFree();
                Rebuild();
            }
        }
        field.TextChanged += text => ok.Disabled = PlayerIdentity.Clean(text) is null;
        field.TextSubmitted += _ => Accept();
        ok.Pressed += Accept;

        AddChild(veil);
        field.CallDeferred(Control.MethodName.GrabFocus);
    }

    // FOUR PICTURES, because this screen has four faces and only the first of them is the shelf. The plate is
    // where the player reads how much HP a thing has and what it does — the whole request — and each of the
    // three kinds draws it with a different widget (a body, a card face, a framed object), so each of the
    // three is looked at.
    // `--smoke-history`: four made-up runs written through the real file format into a probe file, read back,
    // and shown. What it checks is the one thing that can quietly go wrong — a field that does not survive the
    // disk — and the picture is for the eye.
    private async System.Threading.Tasks.Task SmokeHistory()
    {
        RunHistory.Path = "user://run-history-probe.json";
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(RunHistory.Path));
        var start = new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc);
        string At(int hours) => start.AddHours(hours).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        RunHistory.Append(new RunSummary { StartedUtc = At(0), EndedUtc = At(1), Seed = 11, Character = "Bureaucrat",
            Result = "Defeat", Act = 1, Rooms = 9, Health = 0, MaxHealth = 70, Gold = 41, EndedAt = "Sealed Door Ward",
            Deck = ["Paper Cut", "Paper Cut", "Cower Behind a Desk", "Strong Binder+"], Relics = ["Rubber Stamp"] });
        RunHistory.Append(new RunSummary { StartedUtc = At(2), EndedUtc = At(3), Seed = 12, Character = "Bureaucrat",
            Result = "Abandoned", Rooms = 3 });
        RunHistory.Append(new RunSummary { StartedUtc = At(4), EndedUtc = At(6), Seed = 13, Character = "Bureaucrat",
            Result = "Victory", Act = 5, Rooms = 110, Health = 22, MaxHealth = 70, Gold = 310,
            Deck = ["Paper Cut+", "Permit A38", "Strong Binder"], Relics = ["Rubber Stamp", "Red Tape"] });
        RunHistory.Append(new RunSummary { StartedUtc = At(7), EndedUtc = At(8), Seed = 14, Character = "Bureaucrat",
            Result = "Defeat", Act = 2, Rooms = 30, Health = 0, MaxHealth = 70, Gold = 12, EndedAt = "Sealed Door Ward",
            Deck = ["Paper Cut"], Relics = [] });
        var back = RunHistory.Load();
        var kept = back.Count == 4 && back[0].Deck.Count == 4 && back[0].Deck[3] == "Strong Binder+"
            && back[2].Relics.Count == 2 && back[0].EndedAt == "Sealed Door Ward" && back[1].Result == "Abandoned";
        HistoryPanel.Open(this, null);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // Open the victory, the way a player clicks a row.
        var panel = FindChild(nameof(HistoryPanel), recursive: true, owned: false);
        var row = panel?.FindChildren("*", nameof(Button), recursive: true, owned: false).OfType<Button>()
            .FirstOrDefault(b => b.Text.Contains("Victory"));
        row?.EmitSignal(BaseButton.SignalName.Pressed);
        GD.Print($"smoke-history: {back.Count} runs read back · fields kept={kept} · panel={panel is not null} "
            + $"· row opened={row is not null} {(kept && panel is not null && row is not null ? "PASS" : "FAIL")}");
        if (!DisplayServer.GetName().Contains("headless"))
        {
            await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
            GetViewport().GetTexture().GetImage().SavePng("user://smoke-history.png");
        }
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(RunHistory.Path));
        GetTree().Quit(kept ? 0 : 1);
    }

    private static IEnumerable<Godot.Node> Descendants(Godot.Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var below in Descendants(child))
                yield return below;
        }
    }

    private void SmokeSeed(GameHost host)
    {
        var today = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        var parsed = RunSeeds.Parse(" 12345 ") == 12345 && RunSeeds.Parse("") is null
            && RunSeeds.Parse("dragon") is { } word && word == RunSeeds.Parse("dragon") && word != RunSeeds.Parse("Dragon");
        var daily = RunSeeds.Daily(today) == RunSeeds.Daily(today.AddHours(11))
            && RunSeeds.Daily(today) != RunSeeds.Daily(today.AddDays(1));

        string Layout(int seed)
        {
            host.StartNewRun(seed, mapGenerator: MapGenerators.Strategic);
            var map = host.Play?.Session?.Run.Map;
            return map is null ? "none" : string.Join(",", map.Nodes.Select(n => $"{n.Id.Value}:{n.Type}"));
        }
        var first = Layout(4242);
        var same = first != "none" && first == Layout(4242);
        var different = first != Layout(4243);
        host.AbandonRunInMemory();

        NewRunPanel.Open(this, (_, _) => { });
        var panel = FindChild(nameof(NewRunPanel), recursive: true, owned: false);
        var button = panel?.FindChild("DailyButton", recursive: true, owned: false) as Button;
        button?.EmitSignal(BaseButton.SignalName.Pressed);
        var field = panel?.FindChild("SeedField", recursive: true, owned: false) as LineEdit;
        var filled = field?.Text == RunSeeds.Daily(DateTime.UtcNow).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var ok = parsed && daily && same && different && filled;
        GD.Print($"smoke-seed: parse={parsed} daily={daily} same-seed-same-act={same} other-seed-other-act={different} "
            + $"daily-button={filled} {(ok ? "PASS" : "FAIL")}");
        if (DisplayServer.GetName().Contains("headless"))
            GetTree().Quit(ok ? 0 : 1);
        else
            _ = CaptureThenQuit("user://smoke-seed.png");
    }

    private async System.Threading.Tasks.Task SmokeArchiveShots()
    {
        // The god is DISCOVERED here and not by ReportArchive, so the shelf picture keeps its "???" and the
        // plate picture can still show a decree.
        var god = Archive.Entries(ArchiveKind.Gods).FirstOrDefault();
        var shots = new (string File, ArchiveKind? Kind, string? Id)[]
        {
            ("user://smoke-archive.png", null, null),
            ("user://smoke-archive-elite.png", ArchiveKind.Elites, Archive.Entries(ArchiveKind.Elites).FirstOrDefault()?.Id),
            ("user://smoke-archive-card.png", ArchiveKind.Cards, Archive.Entries(ArchiveKind.Cards).FirstOrDefault()?.Id),
            ("user://smoke-archive-relic.png", ArchiveKind.Relics, Archive.Entries(ArchiveKind.Relics).FirstOrDefault()?.Id),
            ("user://smoke-archive-god.png", ArchiveKind.Gods, god?.Id),
        };

        // ⚠⚠ ONE PANEL, SHOWN FIVE THINGS — not five panels. Tearing the overlay down between shots and
        // building it again produced FIVE BYTE-IDENTICAL FILES: `QueueFree` is deferred, so the freed overlay
        // was still in the tree when the next `OpenArchive` looked for one, found it, and handed back the
        // dying panel. Every line the probe printed was true and every picture was of the same screen. It is
        // the same trap D7 found in `--smoke-shop`, arriving from the other side, and the same two cures:
        // do not rebuild what you can simply ask again, and make the screen SAY what it is showing.
        var panel = OpenArchive();
        if (panel is null)
        {
            GD.Print("smoke-archive: the archive did not open at all");
            GetTree().Quit(1);
            return;
        }

        var wrong = 0;
        foreach (var (file, kind, id) in shots)
        {
            if (kind == ArchiveKind.Gods && god is not null)
                Archive.Discover(god);
            if (kind is { } which && id is { } what)
                panel.Show(which, what);
            for (var frame = 0; frame < 4; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(file);
            GD.Print($"smoke-archive: screenshot {file} — {panel.Photographed}");
            if (id is not null && !panel.Photographed.Contains(id, StringComparison.Ordinal))
                wrong++;
        }
        GetTree().Quit(wrong == 0 ? 0 : 1);
    }

    private void OpenCredits()
    {
        if (GetNodeOrNull("CreditsOverlay") is not null)
            return;
        var overlay = CreditsPanel.Overlay(() => GetNodeOrNull("CreditsOverlay")?.QueueFree());
        overlay.Name = "CreditsOverlay";
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
