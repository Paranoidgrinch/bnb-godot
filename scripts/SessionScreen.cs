using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// The run screen: renders whichever state the engine session parked in — the exact dispatch order of
// the Studio's RunSessionView (error → event choice → entity pick → path fork → interlude → combat →
// complete) — and forwards every input to the session/driver. The whole screen rebuilds on
// GameHost.StateChanged; the layouts are simple enough that a full rebuild per answer is fine.
public partial class SessionScreen : Control
{
    private VBoxContainer _main = null!;
    private ScrollContainer _mainScroll = null!;
    private Control _combatRoot = null!;
    private VBoxContainer _sidebar = null!;
    private RichTextLabel _log = null!;

    // Transient pick state owned by the view (mirrors RunSessionView's _selected/_cardPicks/_combatTarget).
    private readonly HashSet<int> _selectedEntities = [];
    private readonly HashSet<string> _selectedCards = [];

    // Options a card offered and the player has taken so far, IN PICK ORDER — a choice resolves in the order
    // it was chosen, so this is a list rather than a set.
    private readonly List<int> _selectedOptions = [];
    private CardInstanceId? _armedCard; // the hand card waiting for a target click
    private int _seenProblems;

    // The act the screen has already announced, so the title card shows once per act rather than every redraw.
    private int _announcedAct;

    // Draw animation: which hand cards were already on screen last render (so newly-drawn cards fly in from
    // the deck), the card nodes queued for that fly-in, and the deck pile's top node (their start point).
    private readonly HashSet<string> _shownHandIds = [];
    private readonly List<Control> _cardsToAnimate = [];
    private Control? _deckTopNode;

    private static RunPlayback? Play => GameHost.Instance.Play;
    private static InteractiveRunSession? Session => Play?.Session;

    public override void _Ready()
    {
        Theme = MoonvineTheme.Build();
        var background = new ColorRect { Color = MoonvineTheme.Bg };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var split = new HBoxContainer();
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        split.AddThemeConstantOverride("separation", 16);
        AddChild(split);

        var mainPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var mainHolder = new Control();
        _mainScroll = new ScrollContainer();
        _mainScroll.SetAnchorsPreset(LayoutPreset.FullRect);
        _main = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _main.AddThemeConstantOverride("separation", 10);
        _mainScroll.AddChild(_main);
        mainHolder.AddChild(_mainScroll);
        // The graphical combat scene lives here (hero left, enemies right, hand bottom) — shown only in combat.
        _combatRoot = new Control { Visible = false };
        _combatRoot.SetAnchorsPreset(LayoutPreset.FullRect);
        mainHolder.AddChild(_combatRoot);
        mainPanel.AddChild(mainHolder);
        split.AddChild(mainPanel);

        var side = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        side.AddThemeConstantOverride("separation", 10);
        var sidePanel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        var sideScroll = new ScrollContainer();
        _sidebar = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sideScroll.AddChild(_sidebar);
        sidePanel.AddChild(sideScroll);
        side.AddChild(sidePanel);
        var logPanel = new PanelContainer { CustomMinimumSize = new Vector2(320, 200) };
        _log = new RichTextLabel { FitContent = false, ScrollFollowing = true, BbcodeEnabled = false };
        logPanel.AddChild(_log);
        side.AddChild(logPanel);
        split.AddChild(side);

        GameHost.Instance.StateChanged += Rebuild;
        Rebuild();

        if (OS.GetCmdlineUserArgs().Contains("--smoke-run"))
            SmokeRun();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-target"))
            SmokeTarget();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-draw"))
            _ = SmokeDraw();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-map"))
            _ = CaptureThenQuit("smoke-map.png"); // a fresh run parks at the entry fork — screenshot the map
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-full"))
            SmokeFull();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-timing"))
            SmokeTiming();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-reward"))
            _ = SmokeReward();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-statuses"))
            SmokeStatuses();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-shop"))
            _ = SmokeRoom(MapNodeTags.Shop, "smoke-shop.png");
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-event"))
            _ = SmokeRoom(MapNodeTags.Event, "smoke-event.png");
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-rest"))
            _ = SmokeRoom(MapNodeTags.Rest, "smoke-rest.png");
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-ambush"))
            _ = SmokeRoom(MapNodeTags.MultiCombat, "smoke-ambush.png");
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-elite"))
            _ = SmokeRoom(MapNodeTags.Elite, "smoke-elite.png");
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-marathon"))
            SmokeMarathon();
    }

    // Play the WHOLE game — both acts, every room the route holds, to the last boss — through the real screens
    // (every answer goes through the same Rebuild the player sees). What it proves is that the frontend holds
    // up all the way: the map redraws for the second act, the act title card fires, no screen throws twenty
    // rooms in. The engine-side coverage lives in bnb-content's own walk; this one is about the UI.
    private void SmokeMarathon()
    {
        var session = Session;
        var play = Play;
        var rooms = new List<string>();
        var acts = 1;
        string? lastRoom = null;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        for (var step = 0; step < 20000 && session is not null && play is not null && !session.IsComplete; step++)
        {
            if (session.Error is not null || play.Error is not null)
                break;
            if (session.Run.CurrentNodeId?.Value is { } here && here != lastRoom)
            {
                lastRoom = here;
                var node = session.Run.Map.Nodes.FirstOrDefault(n => n.Id.Value == here);
                rooms.Add($"{session.Run.ActNumber}:{MapView.Role(node ?? throw new InvalidOperationException(here))}");
                acts = Math.Max(acts, session.Run.ActNumber);
                // The latency curve, room by room: under the replay model every answer re-runs the whole run,
                // so what this prints is how the game FEELS as it gets longer.
                GD.Print($"  [{clock.Elapsed.TotalSeconds,7:0.0}s, {step,5} answers] "
                    + $"act {session.Run.ActNumber} {here} {rooms[^1].Split(':')[1]}");
            }

            if (play.CombatDriver is { Current: not null } driver)
            {
                if (driver.PendingOptionChoice is { } options)
                    driver.SupplyOptionChoice(
                        [.. Enumerable.Range(0, Math.Min(driver.PendingOptionChoiceCount, options.Count))]);
                else if (driver.PendingCardChoice is { } cards)
                    driver.SupplyCardChoice([.. cards.Take(driver.PendingCardChoiceCount).Select(c => c.Id)]);
                else if (driver.Current!.IsHeroTurn)
                {
                    var combat = driver.Current;
                    var hero = combat.State.GetCombatant(combat.HeroId);
                    var card = combat.Hand.FirstOrDefault(c =>
                        !c.DefinitionId.value.Contains("red_tape") && !c.DefinitionId.value.Contains("unsigned_form")
                        && CanPay(hero, c.DefinitionId.value));
                    var target = combat.State.Combatants
                        .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive
                            && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
                    if (card is not null)
                        driver.PlayCard(card.Id, target);
                    else
                        driver.EndTurn();
                }
                else
                    break; // the enemy turn resolves synchronously under replay — it never parks here
            }
            else if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices[^1].Id.Value);
            else if (session.IsAwaitingEntities && session.PendingEntities is { } entities)
                session.PickEntities([.. Enumerable.Range(0, Math.Min(entities.Count, entities.Displays.Count))]);
            else if (session.IsAwaitingChoice)
                session.Pick(session.PendingChoices[0].Id);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else
                break;
        }

        var byAct = rooms.GroupBy(r => r.Split(':')[0])
            .Select(g => $"act {g.Key}: {g.Count()} rooms ({string.Join(" ", g.GroupBy(x => x.Split(':')[1]).Select(k => $"{k.Key}×{k.Count()}"))})");
        GD.Print($"smoke-marathon: result={session?.Run.Result} acts={acts} rooms={rooms.Count} "
            + $"error={session?.Error ?? Play?.Error ?? "none"}");
        foreach (var line in byAct)
            GD.Print($"  {line}");
        GetTree().Quit();
    }

    // Walk toward the nearest room of one KIND and screenshot it as the player would meet it. The rooms that
    // are not fights — shop, campfire, a door — are the ones nothing else in the smoke suite ever looks at.
    private async System.Threading.Tasks.Task SmokeRoom(string role, string file)
    {
        var session = Session;
        var play = Play;
        for (var step = 0; step < 600 && session is not null && play is not null; step++)
        {
            var here = session.Run.CurrentNodeId is { } id
                ? session.Run.Map.Nodes.FirstOrDefault(n => n.Id.Value == id.Value)
                : null;
            // Parked at a room of the wanted kind, with something on screen to look at.
            if (here is not null && here.HasTag(role)
                && (session.IsAwaitingChoice || session.IsAwaitingEntities || play.CombatDriver?.Current is not null))
                break;

            if (play.CombatDriver?.Current is { } combat)
            {
                if (!combat.IsHeroTurn)
                    break;
                var hero = combat.State.GetCombatant(combat.HeroId);
                var card = combat.Hand.FirstOrDefault(c =>
                    !c.DefinitionId.value.Contains("red_tape") && CanPay(hero, c.DefinitionId.value));
                var target = combat.State.Combatants
                    .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
                if (card is not null)
                    play.CombatDriver.PlayCard(card.Id, target);
                else
                    play.CombatDriver.EndTurn();
            }
            else if (session.IsAwaitingNodeChoice)
            {
                // Steer toward the wanted kind; otherwise take the shortest way on.
                var wanted = session.PendingNodeChoices.FirstOrDefault(n => n.HasTag(role))
                    ?? session.PendingNodeChoices[0];
                session.PickNode(wanted.Id.Value);
            }
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else if (session.IsAwaitingEntities)
                session.PickEntities([0]);
            else if (session.IsAwaitingChoice)
                session.Pick(session.PendingChoices[^1].Id);
            else
                break;
        }
        Rebuild();
        GD.Print($"smoke-room {role}: choice={session?.IsAwaitingChoice} entities={session?.IsAwaitingEntities} "
            + $"error={session?.Error ?? Play?.Error ?? "none"}");
        await CaptureThenQuit(file);
    }

    // Auto-play greedily until the first reward/entity pick, then screenshot it (verifies reward
    // readability). Windowed only.
    private async System.Threading.Tasks.Task SmokeReward()
    {
        var session = Session;
        var play = Play;
        // Advance to a MEANINGFUL entity pick — one with real ability descriptions (the card reward),
        // auto-taking the bundled 1-option "spoils" pick along the way.
        bool AtCardPick() => session!.IsAwaitingEntities
            && session.PendingEntities!.Descriptions.Any(d => !string.IsNullOrWhiteSpace(d));
        for (var step = 0; step < 300 && session is not null && play is not null && !AtCardPick(); step++)
        {
            if (session.IsAwaitingEntities)
            {
                session.PickEntities([0]); // the bundled spoils (no descriptions) — take it and move on
                continue;
            }
            if (play.CombatDriver?.Current is { } combat)
            {
                if (combat.IsHeroTurn)
                {
                    var hero = combat.State.GetCombatant(combat.HeroId);
                    var card = combat.Hand.FirstOrDefault(c =>
                        !c.DefinitionId.value.Contains("red_tape") && CanPay(hero, c.DefinitionId.value));
                    var target = combat.State.Combatants
                        .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
                    if (card is not null)
                        play.CombatDriver.PlayCard(card.Id, target);
                    else
                        play.CombatDriver.EndTurn();
                }
                else
                    break;
            }
            else if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else if (session.IsAwaitingChoice)
                session.Pick(session.PendingChoices[^1].Id);
            else
                break;
        }
        Rebuild();
        GD.Print($"smoke-reward: awaiting={session?.IsAwaitingEntities} "
            + $"displays={(session?.PendingEntities is { } e ? string.Join(" | ", e.Displays) : "-")}");
        await CaptureThenQuit("smoke-reward.png");
    }

    // Measure per-action latency (a card play under the replay model re-executes the whole run — is that
    // fast enough for a human clicking cards?). Reach the first fight, then time up to 12 actions.
    private void SmokeTiming()
    {
        var session = Session;
        var play = Play;
        var reachWatch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 8 && play?.CombatDriver?.Current is null && session is not null; i++)
        {
            if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else
                break;
        }
        var combat = play?.CombatDriver?.Current;
        if (combat is null || play is null)
        {
            GD.Print("smoke-timing: no fight reached");
            GetTree().Quit();
            return;
        }
        GD.Print($"smoke-timing: reached fight in {reachWatch.ElapsedMilliseconds} ms");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var actions = 0;
        for (; actions < 2 && play.CombatDriver!.Current is { } live && !live.IsOver; actions++)
        {
            var before = watch.ElapsedMilliseconds;
            var hero = live.State.GetCombatant(live.HeroId);
            var card = live.Hand.FirstOrDefault(c =>
                !c.DefinitionId.value.Contains("red_tape") && CanPay(hero, c.DefinitionId.value));
            if (card is not null && live.IsHeroTurn)
            {
                var target = live.State.Combatants
                    .FirstOrDefault(c => c.Id != live.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
                play.CombatDriver.PlayCard(card.Id, target);
            }
            else
            {
                play.CombatDriver.EndTurn();
            }
            GD.Print($"smoke-timing: action {actions} took {watch.ElapsedMilliseconds - before} ms");
        }
        GD.Print($"smoke-timing: {actions} actions in {watch.ElapsedMilliseconds} ms "
            + $"({(actions > 0 ? watch.ElapsedMilliseconds / actions : 0)} ms/action avg)");
        GetTree().Quit();
    }

    // Auto-play the FIRST FEW nodes through the same input methods the UI calls — proves the multi-node
    // loop holds up (combat → interlude → fork → event/shop → next fight) without the full-act cost (the
    // replay model re-executes the whole run per input, so a whole act is far too slow headless; the
    // fast full-act check is bnb-content's direct-driver C3 test). Greedy in combat, forward-biased at
    // choices; stops after NodeBudget rooms.
    private const int NodeBudget = 2;

    private void SmokeFull()
    {
        var session = Session;
        var play = Play;
        var fights = 0;
        for (var step = 0; step < 4000 && session is not null && play is not null && !session.IsComplete; step++)
        {
            if (session.Error is not null || play.Error is not null)
                break;
            if (session.Run.VisitedNodes.Count >= NodeBudget && play.CombatDriver?.Current is null
                && !session.IsAwaitingChoice && !session.IsAwaitingEntities)
                break; // budget reached at a clean boundary

            if (play.CombatDriver?.Current is { } combat)
            {
                if (play.CombatDriver.PendingOptionChoice is { } options)
                {
                    play.CombatDriver.SupplyOptionChoice(
                        Enumerable.Range(0, Math.Min(play.CombatDriver.PendingOptionChoiceCount, options.Count)).ToList());
                }
                else if (play.CombatDriver.PendingCardChoice is { } candidates)
                {
                    play.CombatDriver.SupplyCardChoice(
                        candidates.Take(play.CombatDriver.PendingCardChoiceCount).Select(c => c.Id).ToList());
                }
                else if (combat.IsHeroTurn)
                {
                    var hero = combat.State.GetCombatant(combat.HeroId);
                    var playable = combat.Hand.FirstOrDefault(c =>
                        !c.DefinitionId.value.Contains("red_tape") && !c.DefinitionId.value.Contains("unsigned_form")
                        && CanPay(hero, c.DefinitionId.value));
                    if (playable is not null)
                    {
                        var target = combat.State.Combatants
                            .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
                        play.CombatDriver.PlayCard(playable.Id, target);
                    }
                    else
                    {
                        fights++;
                        play.CombatDriver.EndTurn();
                    }
                }
                else
                {
                    break; // enemy turn resolves synchronously under replay — never parks here
                }
            }
            else if (session.IsAwaitingChoice)
            {
                // Forward-biased: prefer a leave/continue/decline choice so shops and events terminate.
                var choices = session.PendingChoices;
                var choice = choices.FirstOrDefault(c =>
                    c.Id is "leave" or "continue" or "skip" or "decline") ?? choices[^1];
                session.Pick(choice.Id);
            }
            else if (session.IsAwaitingEntities && session.PendingEntities is { } entities)
            {
                session.PickEntities(Enumerable.Range(0, entities.Count).ToList());
            }
            else if (session.IsAwaitingNodeChoice)
            {
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
            }
            else if (session.IsAwaitingInterlude)
            {
                session.Continue();
            }
            else
            {
                break;
            }
        }

        GD.Print("smoke-full: "
            + $"result={session?.Run.Result} "
            + $"visited={session?.Run.VisitedNodes.Count}/{session?.Run.Map.Nodes.Count} "
            + $"hp={session?.Run.Health.Current}/{session?.Run.Health.Max} "
            + $"gold={session?.Run.GetResource(StandardRunIds.Gold)} "
            + $"deck={session?.Run.Deck.Count} relics={session?.Run.Relics.Count} "
            + $"turns={fights} error={session?.Error ?? play?.Error ?? "none"}");
        GetTree().Quit();
    }

    // Headless proof that carried state READS: every status the game defines must show the name it was
    // authored with, not the id it is filed under. Walks into the first fight (which supplies a live
    // definition registry), then renders a sample of statuses from across the game — the plain ones, the
    // Act-II boss state that whole fights are built on — exactly as a chip in the fight would.
    private void SmokeStatuses()
    {
        var combat = WalkToFirstFight();
        if (combat is null)
        {
            GD.Print("smoke-statuses: no fight reached");
            GetTree().Quit();
            return;
        }

        var registry = combat.State.DefinitionRegistry;
        if (registry is null)
        {
            GD.Print("smoke-statuses: FAIL the fight carries no definition registry");
            GetTree().Quit();
            return;
        }

        // Let the fight put something ON the table first: two turns of the Notary's wax is Paperwork stacking
        // on the hero, which is what a chip with a magnitude has to look like.
        for (var turn = 0; turn < 2 && Play?.CombatDriver?.Current is not null; turn++)
            Play.CombatDriver.EndTurn();
        combat = Play?.CombatDriver?.Current ?? combat;

        // Statuses the Act-II bosses put on the table, plus two ordinary ones for contrast.
        string[] sample =
        [
            "paperwork", "strength",
            "scheduled_the_collapse", "final_entry", "catalogue_authority",
            "warden_custody", "supporting_documentation", "office_hours",
        ];

        var unnamed = 0;
        var lines = new List<string>();
        foreach (var id in sample)
        {
            if (!registry.TryGetStatus(new StatusDefinitionId(id), out var definition) || definition is null)
                continue;
            var named = !string.IsNullOrWhiteSpace(definition.DisplayNameKey)
                && !string.Equals(definition.DisplayNameKey, id, StringComparison.Ordinal);
            if (!named)
                unnamed++;
            lines.Add($"{id} -> \"{definition.DisplayNameKey}\""
                + (string.IsNullOrWhiteSpace(definition.DescriptionKey) ? " (no rules text)" : ""));
        }

        GD.Print($"smoke-statuses: resolved={lines.Count}/{sample.Length} unnamed={unnamed}");
        foreach (var line in lines)
            GD.Print($"  {line}");
        // …and what the fight itself is currently carrying, rendered as the chips render it.
        foreach (var combatant in combat.State.Combatants)
            GD.Print($"  [{Name(combatant, combat)}] {StatusLine(combat, combatant)}");

        // Windowed: the chips are a layout as well as a lookup — capture the fight so they can be eyeballed.
        if (!DisplayServer.GetName().Contains("headless"))
            _ = CaptureThenQuit("smoke-statuses.png");
        else
            GetTree().Quit();
    }

    // The first fight of a fresh run, reached through the same session calls the buttons make.
    private InteractiveCombat? WalkToFirstFight()
    {
        var session = Session;
        for (var guard = 0; guard < 20 && session is not null && Play?.CombatDriver?.Current is null; guard++)
        {
            if (session.IsAwaitingNodeChoice)
            {
                var combatNode = session.PendingNodeChoices
                    .FirstOrDefault(n => n.Type == StandardRunIds.CombatNode) ?? session.PendingNodeChoices[0];
                session.PickNode(combatNode.Id.Value);
            }
            else if (session.IsAwaitingChoice)
                session.Pick(session.PendingSituation!.Choices[0].Id);
            else if (session.IsAwaitingEntities)
                session.PickEntities([0]);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else
                break;
        }
        return Play?.CombatDriver?.Current;
    }

    // Headless proof of the whole Godot-side loop: walk to the first fight THROUGH the same methods the
    // buttons call, play one affordable card at the default target, and report the resulting state.
    private void SmokeRun()
    {
        var session = Session;
        if (session is null)
        {
            GD.Print("smoke-run: NO SESSION");
            GetTree().Quit();
            return;
        }

        for (var guard = 0; guard < 10 && Play?.CombatDriver?.Current is null; guard++)
        {
            if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else
                break;
        }

        var combat = Play?.CombatDriver?.Current;
        if (combat is null)
        {
            GD.Print($"smoke-run: no fight reached (error={session.Error ?? Play?.Error ?? "none"})");
            GetTree().Quit();
            return;
        }

        var hero = combat.State.GetCombatant(combat.HeroId);
        var enemy = combat.State.Combatants.First(c => c.Id != combat.HeroId);
        var hpBefore = enemy.Health.Current;
        _armedCard = combat.Hand.FirstOrDefault(c => CanPay(hero, c.DefinitionId.value))?.Id;
        if (_armedCard is not null)
            PlayArmedCardAt(null);

        var after = Play?.CombatDriver?.Current;
        var enemyAfter = after?.State.Combatants.First(c => c.Id != after.HeroId);
        GD.Print("smoke-run: "
            + $"fight={combat.State.Combatants.Count(c => c.Id != combat.HeroId)}v1 "
            + $"hand={combat.Hand.Count}→{after?.Hand.Count ?? -1} "
            + $"enemyHp={hpBefore}→{enemyAfter?.Health.Current ?? -1} "
            + $"intent={combat.UpcomingIntentFor(enemy.Id)?.Label ?? "-"} "
            + $"error={session.Error ?? Play?.Error ?? "none"}");

        // Windowed run only: let the freshly-built UI render a few frames, capture the combat screen so
        // the look can be eyeballed, then quit. Headless has no framebuffer to read, so just quit.
        if (DisplayServer.GetName().Contains("headless"))
        {
            GetTree().Quit();
            return;
        }
        _ = CaptureThenQuit("smoke-combat.png");
    }

    private async System.Threading.Tasks.Task CaptureThenQuit(string file)
    {
        if (DisplayServer.GetName().Contains("headless"))
        {
            GetTree().Quit();
            return;
        }
        // Let animations (draw fly-in, the deck's video) settle before the still capture.
        await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng($"user://{file}");
        GD.Print($"smoke: screenshot user://{file} ({image.GetWidth()}x{image.GetHeight()})");
        GetTree().Quit();
    }

    public override void _ExitTree() => GameHost.Instance.StateChanged -= Rebuild;

    // ── the dispatcher ───────────────────────────────────────────────────────────

    private void Rebuild()
    {
        foreach (var child in _main.GetChildren())
            child.QueueFree();
        foreach (var child in _combatRoot.GetChildren())
            child.QueueFree();
        foreach (var child in _sidebar.GetChildren())
            child.QueueFree();

        var session = Session;
        // Combat gets the graphical scene (_combatRoot); everything else the ordinary list (_mainScroll).
        var inCombat = session is not null && Play?.Error is null && session.Error is null
            && !session.IsAwaitingChoice && !session.IsAwaitingEntities && !session.IsAwaitingNodeChoice
            && !session.IsAwaitingInterlude && Play?.CombatDriver?.Current is not null;
        _combatRoot.Visible = inCombat;
        _mainScroll.Visible = !inCombat;
        _deckTopNode = null;
        if (!inCombat)
            _shownHandIds.Clear(); // a fresh fight re-deals; its opening hand animates in

        if (Play is null || session is null)
        {
            Title("No run active.");
            return;
        }

        if (Play.Error is { } hostError)
            Title($"Error: {hostError}", MoonvineTheme.Danger);
        else if (session.Error is { } runError)
            Title($"Run error: {runError}", MoonvineTheme.Danger);
        else if (session.IsAwaitingChoice && session.PendingSituation is { } situation)
            RenderChoices(session, situation);
        else if (session.IsAwaitingEntities && session.PendingEntities is { } entities)
            RenderEntityPick(session, entities);
        else if (session.IsAwaitingNodeChoice)
            RenderNodeFork(session);
        else if (session.IsAwaitingInterlude)
            RenderInterlude(session);
        else if (Play.CombatDriver?.Current is { } combat)
            RenderCombatGraphical(session, combat);
        else if (session.IsComplete)
            RenderComplete(session);
        else
            Title("…");

        RenderSidebar(session);
        _log.Text = string.Join("\n", session.Run.Log.TakeLast(60).Select(entry => entry.Message));
        AnnounceAct(session);
    }

    // Crossing into the next act is the biggest thing that happens outside a fight, and the engine does it by
    // itself: the map simply becomes a different map. Say it out loud, once, the first time the run renders
    // inside an act it was not in before.
    private void AnnounceAct(InteractiveRunSession session)
    {
        var act = session.Run.ActNumber;
        if (act == _announcedAct)
            return;
        _announcedAct = act;
        if (act <= 1)
            return; // the first act needs no announcement — the run just started in it

        var acts = GameHost.Instance.Blueprint.Acts;
        var name = acts is not null && session.Run.ActIndex < acts.Count
            ? acts[session.Run.ActIndex].NameKey ?? acts[session.Run.ActIndex].Id
            : $"Act {act}";
        Banner(name);
    }

    // A title card that fades away by itself: the whole screen dimmed, the act's name across it.
    private void Banner(string text)
    {
        if (!IsInsideTree())
            return; // a headless probe can redraw on its way out of the tree; there is nobody to show it to

        var veil = new ColorRect { Color = new Color(MoonvineTheme.Bg, 0.82f), MouseFilter = MouseFilterEnum.Ignore };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.SetAnchorsPreset(LayoutPreset.FullRect);
        label.AddThemeFontSizeOverride("font_size", 34);
        label.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
        veil.AddChild(label);
        AddChild(veil);

        var fade = CreateTween();
        fade.TweenInterval(2.2);
        fade.TweenProperty(veil, "modulate:a", 0.0f, 0.8);
        fade.TweenCallback(Callable.From(veil.QueueFree));
    }

    // ── run-level states ─────────────────────────────────────────────────────────

    private void RenderChoices(InteractiveRunSession session, EventSituation situation)
    {
        // A shop is an event situation like any other, but its choices are only the AFFORDABLE ones — so a
        // player with no gold saw an empty room. It gets its own screen, drawn off the live shelf.
        if (session.PendingShopShelf is { } shelf)
        {
            RenderShop(session, shelf);
            return;
        }

        Title(Say(situation.TextKey ?? situation.Id));
        foreach (var choice in session.PendingChoices)
        {
            var id = choice.Id;
            AddButton(Say(choice.TextKey ?? id), () => session.Pick(id));
        }
    }

    // The shop, as a shopkeeper would lay it out: everything standing on the shelf with its price, whether or
    // not the purse can reach it. What is affordable is a button; what is not is greyed and still readable —
    // knowing what you cannot yet buy is most of what a shop is for.
    private void RenderShop(InteractiveRunSession session, ShopShelf shelf)
    {
        var run = session.Run;
        var gold = run.GetResource(StandardRunIds.Gold);
        var affordable = session.PendingChoices.ToDictionary(c => c.Id, c => c, StringComparer.Ordinal);

        Title("The shop");
        Muted($"Gold: {gold}");

        foreach (var group in shelf.Slots.GroupBy(slot => slot.GroupId))
        {
            _main.AddChild(MutedLabel(Say(group.Key)));
            foreach (var slot in group)
                AddShopRow(session, affordable, slot.Entry.Id, slot.Entry.TextKey ?? slot.Entry.Id, slot.Price,
                    WhatItDoes(slot.Entry.Payload));
        }

        foreach (var service in shelf.Services.Where(s => !shelf.IsServiceUsed(s)))
            AddShopRow(session, affordable, service.Id, Say(service.TextKey ?? service.Id), shelf.PriceOf(service));

        if (ShopHere() is { Reroll: { } reroll })
            AddShopRow(session, affordable, ShopNodeResolver.RerollChoiceId, "Restock the shelves", reroll.Price);

        AddButton("Leave", () => session.Pick(ShopNodeResolver.LeaveChoiceId));
    }

    // The shop definition the run is standing in, read off the map node it entered (for the prices of things
    // the player cannot afford — those never reach the choice list).
    private static ShopDefinition? ShopHere()
    {
        if (Session is not { } session || session.Run.CurrentNodeId is not { } id)
            return null;
        var node = session.Run.Map.Nodes.FirstOrDefault(n => n.Id.Value == id.Value);
        return node?.Payload is ShopRef shop
            ? GameHost.Instance.Blueprint.Shops.GetValueOrDefault(shop.Id.Value)
            : null;
    }

    private void AddShopRow(
        InteractiveRunSession session, IReadOnlyDictionary<string, EventChoice> affordable,
        string choiceId, string name, int price, string description = "")
    {
        var canBuy = affordable.ContainsKey(choiceId);
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        var button = new Button { Text = $"{name}   —   {price} gold", Disabled = !canBuy };
        button.Pressed += () => session.Pick(choiceId);
        if (!canBuy)
        {
            button.TooltipText = "Not enough gold.";
            button.AddThemeColorOverride("font_disabled_color", MoonvineTheme.TextMuted);
        }
        row.AddChild(button);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var text = MutedLabel(description);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(text);
        }
        _main.AddChild(row);
    }

    // What a purchase actually gives you, read off the effects behind it rather than off its id — the shelf
    // labels a slot with a name and a price, and the rules text lives with the card or relic it grants.
    private static string WhatItDoes(IReadOnlyList<IRunEffectRequest> payload)
    {
        var presentation = GameHost.Instance.Blueprint.Presentation;
        foreach (var effect in payload)
            switch (effect)
            {
                case AddCardToDeckRunEffect card
                    when presentation.Cards.GetValueOrDefault(card.Card.value)?.FlavorText is { } cardText:
                    return cardText;
                case AddRelicByIdRunEffect relic
                    when presentation.Relics.GetValueOrDefault(relic.Relic.Value)?.FlavorText is { } relicText:
                    return relicText;
            }
        return "";
    }

    // The engine's own situation/choice keys are ids, not sentences ("event.shop.leave"). Give the handful the
    // player can meet a voice; anything else falls back to a readable form of the key itself, so a content gap
    // shows up as words rather than as a dotted id.
    private static string Say(string key) => key switch
    {
        "event.shop" => "The shop",
        "event.shop.leave" => "Leave",
        "event.shop.reroll" => "Restock the shelves",
        "event.shop.remove-card" => "Have a card struck from your deck",
        "cards" => "Cards",
        "relics" => "Relics",
        "stock" => "For sale",
        "reward" => "Your reward",
        "spoils" => "The spoils",
        _ => key.Contains('.') || key.Contains('-') || key.Contains('_') ? Humanized(key) : key,
    };

    private void RenderEntityPick(InteractiveRunSession session, EntitySelectionRequest entities)
    {
        Title(Say(entities.Purpose));
        Muted(entities.Displays.Count <= entities.Count ? "Yours:" : $"Pick {entities.Count}");
        for (var i = 0; i < entities.Displays.Count; i++)
        {
            var index = i;
            var description = index < entities.Descriptions.Count ? entities.Descriptions[index] : "";
            _main.AddChild(EntityOption(entities.Displays[index], description, _selectedEntities.Contains(index), () =>
            {
                if (!_selectedEntities.Remove(index))
                {
                    if (entities.Count == 1)
                        _selectedEntities.Clear();
                    if (_selectedEntities.Count < entities.Count)
                        _selectedEntities.Add(index);
                }
                Rebuild();
            }));
        }
        var confirm = AddButton("Confirm", () =>
        {
            var picks = _selectedEntities.ToList();
            _selectedEntities.Clear();
            session.PickEntities(picks);
        });
        confirm.Disabled = _selectedEntities.Count != entities.Count;

        // A declinable reward (e.g. a card reward): let the player take nothing.
        if (entities.AllowSkip)
            AddButton("Skip — take none", () =>
            {
                _selectedEntities.Clear();
                session.PickEntities([]);
            });
    }

    // A pickable option showing the name on top and its ability/rules text beneath — so a card reward
    // pick shows WHAT each card does. The whole panel is clickable via a transparent overlay button.
    private static Control EntityOption(string name, string description, bool selected, Action onPressed)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(0, 0) };
        panel.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(
            selected ? MoonvineTheme.BgControl : MoonvineTheme.BgPanel,
            selected ? MoonvineTheme.AccentLight : new Color(MoonvineTheme.Accent, 0.3f)));

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        var title = new Label { Text = (selected ? "✓ " : "") + name };
        title.AddThemeColorOverride("font_color", MoonvineTheme.Text);
        column.AddChild(title);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var desc = new Label { Text = description, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            desc.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
            desc.AddThemeFontSizeOverride("font_size", 13);
            column.AddChild(desc);
        }
        panel.AddChild(column);

        var overlay = new Button { Flat = true };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.Pressed += () => onPressed();
        panel.AddChild(overlay);
        return panel;
    }

    private void RenderNodeFork(InteractiveRunSession session)
    {
        ActHeading(session);
        Title("Choose your path");
        Muted("Pick a highlighted room to travel to.");
        AddMap(session.PendingNodeChoices.Select(n => n.Id.Value), session.PickNode);
    }

    private void RenderInterlude(InteractiveRunSession session)
    {
        ActHeading(session);
        Title("Between rooms");
        foreach (var consumable in session.Run.Consumables.Where(c => c.UseEffects.Count > 0))
        {
            var id = consumable.Id;
            AddButton($"Use {consumable.DefinitionId.Value}", () => session.UseConsumable(id));
        }
        AddButton("Continue ▸", session.Continue);
        AddButton("Save run", () => Toast(GameHost.Instance.SaveRun() ?? "Saved."));
        AddMap(null, null);
    }

    // Which act this is, and how far through it the run stands. Without this the player crosses from the city
    // into the archives and is never told — the map simply becomes a different map.
    private void ActHeading(InteractiveRunSession session)
    {
        var run = session.Run;
        var acts = GameHost.Instance.Blueprint.Acts;
        var name = acts is not null && run.ActIndex < acts.Count
            ? acts[run.ActIndex].NameKey ?? acts[run.ActIndex].Id
            : null;
        if (name is not null)
        {
            var label = new Label { Text = name, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            label.AddThemeFontSizeOverride("font_size", 15);
            label.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
            _main.AddChild(label);
        }
        if (run.Map.Nodes.Count > 0)
            Muted($"Room {run.VisitedNodes.Count} of about {LongestRoute(run.Map)}");
    }

    // The most rooms any route through this act asks for — "about", because the routes differ.
    private static int LongestRoute(RunMap map)
    {
        var depth = map.Nodes.ToDictionary(n => n.Id.Value, _ => 1);
        for (var pass = 0; pass < map.Nodes.Count; pass++)
            foreach (var edge in map.Edges)
                if (depth.TryGetValue(edge.From.Value, out var from) && depth.TryGetValue(edge.To.Value, out var to)
                    && to < from + 1)
                    depth[edge.To.Value] = from + 1;
        return depth.Count == 0 ? 0 : depth.Values.Max();
    }

    // Drop the map graph into the main column: reachable ids are the clickable rooms (a fork), or null
    // for a read-only "you are here" overview (an interlude).
    //
    // The map is the RUN's (RunState.Map = the act being walked), never the blueprint's: in a generated game
    // the blueprint carries map RULES and its own Map is empty, so drawing that drew nothing at all.
    private void AddMap(IEnumerable<string>? reachable, Action<string>? onPick)
    {
        if (Session is not { } session)
            return;
        var map = new MapView(session.Run.Map, session.Run, reachable, onPick)
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        _main.AddChild(map);
        _main.AddChild(MapLegend());
        // Keep the room the run stands in on screen — an act's map is taller than the window.
        CallDeferred(nameof(ScrollToCurrentRoom), map);
    }

    private void ScrollToCurrentRoom(MapView map)
    {
        // Nothing walked yet (the run is at its entry fork) ⇒ the top of the map IS where to look. Scrolling to
        // a room that does not exist used to drop the player into the middle of the act.
        if (!IsInstanceValid(map) || map.CurrentRoomPosition == Vector2.Zero || _mainScroll.Size.Y <= 0)
            return;
        var target = (int)(map.Position.Y + map.CurrentRoomPosition.Y - _mainScroll.Size.Y / 2);
        _mainScroll.ScrollVertical = Math.Max(0, target);
    }

    private static Control MapLegend()
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        row.AddThemeConstantOverride("separation", 12);
        foreach (var role in new[]
                 {
                     MapNodeTags.Combat, MapNodeTags.MultiCombat, MapNodeTags.Elite, MapNodeTags.Event,
                     MapNodeTags.Rest, MapNodeTags.Treasure, MapNodeTags.Shop, MapNodeTags.Boss,
                 })
        {
            var label = new Label { Text = $"{MapView.Icon(role)} {MapView.Label(role)}" };
            label.AddThemeFontSizeOverride("font_size", 11);
            label.AddThemeColorOverride("font_color", MapView.RoleColor(role));
            row.AddChild(label);
        }
        return row;
    }

    private void RenderComplete(InteractiveRunSession session)
    {
        var victory = session.Run.Result == RunResult.Victory;
        Title(victory ? "Victory!" : $"Run over — {session.Run.Result}",
            victory ? MoonvineTheme.Accent : MoonvineTheme.Danger);
        AddButton("Back to title", () => GetTree().ChangeSceneToFile("res://scenes/Boot.tscn"));
    }

    // ── combat (graphical: hero left, enemies right, hand bottom-center) ──────────

    private void RenderCombatGraphical(InteractiveRunSession session, InteractiveCombat combat)
    {
        var play = Play!;
        var hero = combat.State.GetCombatant(combat.HeroId);
        var enemies = combat.State.Combatants
            .Where(c => c.Id != combat.HeroId && c.TeamId == StandardCombatIds.EnemyTeam).ToList();

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        _combatRoot.AddChild(margin);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        margin.AddChild(col);

        var round = new Label { Text = $"Round {combat.Round}", HorizontalAlignment = HorizontalAlignment.Center };
        round.AddThemeFontSizeOverride("font_size", 18);
        col.AddChild(round);

        // Arena: hero far left, enemies far right, a stretchy gap between.
        var arena = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        var heroBox = CombatantBox(combat, hero, isHero: true);
        heroBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        arena.AddChild(heroBox);
        arena.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var enemyRow = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        enemyRow.AddThemeConstantOverride("separation", 24);
        foreach (var enemy in enemies)
            enemyRow.AddChild(CombatantBox(combat, enemy, isHero: false));
        arena.AddChild(enemyRow);
        col.AddChild(arena);

        // Bottom: a prompt the played card raised, the "resolving" note, or the hand + controls.
        //
        // An OPTION prompt comes first: a card parked on one is mid-resolution, so nothing else is playable
        // until it is answered. Picking is by position — one option supplies at once, several toggle into an
        // ordered set, and the order they are picked is the order they resolve.
        if (play.CombatDriver!.PendingOptionChoice is { } optionChoice)
        {
            var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            box.AddChild(new Label
            {
                Text = $"{play.CombatDriver.PendingOptionChoicePurpose}  (pick {play.CombatDriver.PendingOptionChoiceCount})",
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            row.AddThemeConstantOverride("separation", 10);
            for (var i = 0; i < optionChoice.Count; i++)
            {
                var index = i;
                var order = _selectedOptions.IndexOf(index);
                var button = new Button
                {
                    Text = order >= 0 && play.CombatDriver.PendingOptionChoiceCount > 1
                        ? $"{order + 1}. {optionChoice[index]}"
                        : optionChoice[index],
                    CustomMinimumSize = new Vector2(180, 48),
                };
                button.AddThemeColorOverride("font_color",
                    order >= 0 ? MoonvineTheme.Accent : MoonvineTheme.Text);
                button.Pressed += () => OnOptionChoiceClicked(play, index);
                row.AddChild(button);
            }
            box.AddChild(row);

            if (play.CombatDriver.PendingOptionChoiceCount > 1)
            {
                var confirm = new Button { Text = "Confirm" };
                confirm.Disabled = _selectedOptions.Count != play.CombatDriver.PendingOptionChoiceCount;
                confirm.Pressed += () =>
                {
                    var picks = _selectedOptions.ToList();
                    _selectedOptions.Clear();
                    play.CombatDriver.SupplyOptionChoice(picks);
                };
                box.AddChild(confirm);
            }

            col.AddChild(box);
            return;
        }

        if (play.CombatDriver!.PendingCardChoice is { } cardChoice)
        {
            var choiceBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            var title = new Label
            {
                Text = $"{play.CombatDriver.PendingCardChoicePurpose}  (pick {play.CombatDriver.PendingCardChoiceCount})",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            choiceBox.AddChild(title);
            var choiceRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            choiceRow.AddThemeConstantOverride("separation", 10);
            foreach (var candidate in cardChoice)
            {
                var id = candidate.Id;
                var selected = _selectedCards.Contains(id.value);
                var block = CardBlockButton(combat, hero, candidate, selected, () => OnCardChoiceClicked(play, cardChoice, id));
                choiceRow.AddChild(block);
            }
            choiceBox.AddChild(choiceRow);
            if (play.CombatDriver.PendingCardChoiceCount > 1)
            {
                var confirm = new Button { Text = "Confirm" };
                confirm.Disabled = _selectedCards.Count != play.CombatDriver.PendingCardChoiceCount;
                confirm.Pressed += () =>
                {
                    var picks = cardChoice.Where(c => _selectedCards.Contains(c.Id.value)).Select(c => c.Id).ToList();
                    _selectedCards.Clear();
                    play.CombatDriver.SupplyCardChoice(picks);
                };
                choiceBox.AddChild(confirm);
            }
            col.AddChild(choiceBox);
            return;
        }

        if (!combat.IsHeroTurn)
        {
            col.AddChild(new Label { Text = "Resolving enemy actions…", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        var hint = new Label
        {
            Text = _armedCard is not null ? "Click an enemy to play it — or the card again to cancel." : " ",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        col.AddChild(hint);

        // The deck pile sits in the bottom-left corner; build it first so its top card is the fly-in origin.
        BuildDeckPile(combat);
        BuildHand(col, combat, hero);

        var controls = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        controls.AddThemeConstantOverride("separation", 10);
        var endTurn = new Button { Text = "End turn ▸" };
        endTurn.Pressed += () => { _armedCard = null; play.CombatDriver.EndTurn(); SurfaceNewProblems(); };
        controls.AddChild(endTurn);
        foreach (var consumable in session.Run.Consumables.Where(c => c.CombatUse is not null))
        {
            var id = consumable.Id;
            var use = new Button { Text = $"Use {consumable.DefinitionId.Value}" };
            use.Pressed += () => play.UseConsumableInCombat(id);
            controls.AddChild(use);
        }
        col.AddChild(controls);
    }

    // The draw pile in the bottom-left corner: a few offset card backs (the top one animated), plus a count.
    private void BuildDeckPile(InteractiveCombat combat)
    {
        var drawCount = combat.State.GetCardZones(combat.HeroId).GetCardsInZone(CardZone.DrawPile).Count;

        var holder = new Control { CustomMinimumSize = new Vector2(CardVisuals.CardW + 24, CardVisuals.CardH + 30) };
        holder.SetAnchorsPreset(LayoutPreset.BottomLeft);
        holder.Position = new Vector2(24, -(CardVisuals.CardH + 30) - 12);
        _combatRoot.AddChild(holder);

        // Static backs fanned slightly for depth; the top one animates.
        var backing = Math.Min(drawCount, 3);
        for (var i = 0; i < backing; i++)
        {
            var still = CardVisuals.Back(animated: false);
            still.Position = new Vector2(i * 4, -i * 4);
            holder.AddChild(still);
        }
        if (drawCount > 0)
        {
            var top = CardVisuals.Back(animated: true);
            top.Position = new Vector2(backing * 4, -backing * 4);
            holder.AddChild(top);
            _deckTopNode = top;
        }

        var count = new Label { Text = $"Draw {drawCount}", HorizontalAlignment = HorizontalAlignment.Center };
        count.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        count.SetAnchorsPreset(LayoutPreset.BottomWide);
        holder.AddChild(count);
    }

    // The hand as manually-placed card faces (a centered row), so newly-drawn cards can fly in from the deck.
    private void BuildHand(VBoxContainer col, InteractiveCombat combat, CombatantState hero)
    {
        var cards = combat.Hand.ToList();
        const int spacing = 12;
        var totalWidth = cards.Count * CardVisuals.CardW + Math.Max(0, cards.Count - 1) * spacing;

        var center = new CenterContainer();
        var inner = new Control { CustomMinimumSize = new Vector2(Math.Max(totalWidth, 1), CardVisuals.CardH + 8) };
        center.AddChild(inner);
        col.AddChild(center);

        _cardsToAnimate.Clear();
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var cardId = card.Id;
            var armed = _armedCard is { } a && a.value == cardId.value;
            var face = CardBlockButton(combat, hero, card, armed, () => OnCardClicked(cardId));
            face.Position = new Vector2(i * (CardVisuals.CardW + spacing), 0);
            face.Size = new Vector2(CardVisuals.CardW, CardVisuals.CardH);
            inner.AddChild(face);
            if (!_shownHandIds.Contains(cardId.value))
                _cardsToAnimate.Add(face); // newly drawn → fly it in
        }
        if (cards.Count == 0)
            inner.AddChild(MutedLabel("(empty hand)"));

        _shownHandIds.Clear();
        foreach (var card in cards)
            _shownHandIds.Add(card.Id.value);

        if (_cardsToAnimate.Count > 0)
            CallDeferred(nameof(AnimateDraws));
    }

    // Fly each freshly-drawn card from the deck to its hand slot with a mid-flight flip (back → face).
    // Deferred so the layout has settled and the slots' real positions are known.
    private void AnimateDraws()
    {
        var deckGlobal = _deckTopNode is { } deck && IsInstanceValid(deck)
            ? deck.GlobalPosition
            : new Vector2(40, GetViewportRect().Size.Y - 120);

        for (var i = 0; i < _cardsToAnimate.Count; i++)
        {
            var card = _cardsToAnimate[i];
            if (!IsInstanceValid(card) || card.GetParent() is not Control parent)
                continue;

            var target = card.Position;
            var startLocal = parent.GetGlobalTransform().AffineInverse() * deckGlobal;
            var mid = startLocal.Lerp(target, 0.5f);
            card.PivotOffset = new Vector2(CardVisuals.CardW / 2f, CardVisuals.CardH / 2f);
            card.Position = startLocal;

            // A still back covers the face until the flip's midpoint.
            var back = CardVisuals.Back(animated: false);
            back.SetAnchorsPreset(LayoutPreset.FullRect);
            card.AddChild(back);

            var tween = CreateTween();
            tween.TweenInterval(i * 0.12);
            tween.TweenProperty(card, "scale:x", 0.0f, 0.15f).SetTrans(Tween.TransitionType.Sine);
            tween.Parallel().TweenProperty(card, "position", mid, 0.15f).SetTrans(Tween.TransitionType.Cubic);
            tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(back)) back.QueueFree(); }));
            tween.TweenProperty(card, "scale:x", 1.0f, 0.15f).SetTrans(Tween.TransitionType.Sine);
            tween.Parallel().TweenProperty(card, "position", target, 0.15f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        _cardsToAnimate.Clear();
    }

    // A combatant's column: name, a stick-figure placeholder, an HP bar, energy (hero) or intent (enemy),
    // and its status chips. When a card is armed, an enemy box becomes a clickable target.
    private Control CombatantBox(InteractiveCombat combat, CombatantState combatant, bool isHero)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) };
        box.AddThemeConstantOverride("separation", 4);

        var name = new Label { Text = Name(combatant, combat), HorizontalAlignment = HorizontalAlignment.Center };
        name.AddThemeFontSizeOverride("font_size", 16);
        box.AddChild(name);

        var figure = new StickFigure(isHero ? MoonvineTheme.Accent : MoonvineTheme.Danger,
            facing: isHero ? 1 : -1, dead: !combatant.IsAlive)
        { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        box.AddChild(figure);

        box.AddChild(HealthBar(combatant));

        if (isHero)
        {
            var energy = new Label
            {
                Text = ResourcePoolsLine(combatant),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            energy.AddThemeColorOverride("font_color", MoonvineTheme.Warning);
            box.AddChild(energy);
        }
        else if (combatant.IsAlive && combat.UpcomingIntentFor(combatant.Id) is { } intent)
        {
            var intentLabel = new Label
            {
                Text = $"{RogueDeck.Scenario.Authoring.IntentDisplay.Glyph(intent.Kind)} "
                    + $"{RogueDeck.Scenario.Authoring.IntentDisplay.KindWord(intent.Kind)}\n{intent.Label}",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            intentLabel.AddThemeColorOverride("font_color", MoonvineTheme.IntentColor(intent.Kind));
            box.AddChild(intentLabel);
        }

        if (StatusChips(combat, combatant) is { } chips)
            box.AddChild(chips);

        // A framed panel around the column; enemies highlight + become clickable when a card is armed.
        var panel = new PanelContainer();
        var targetable = !isHero && combatant.IsAlive && _armedCard is not null;
        panel.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(
            isHero ? MoonvineTheme.BgControl : MoonvineTheme.BgPanel,
            targetable ? MoonvineTheme.AccentLight : null));
        panel.AddChild(box);

        if (targetable)
        {
            var overlay = new Button { Flat = true };
            overlay.SetAnchorsPreset(LayoutPreset.FullRect);
            var targetId = combatant.Id;
            overlay.Pressed += () => PlayArmedCardAt(targetId);
            panel.AddChild(overlay);
        }
        return panel;
    }

    private static Control HealthBar(CombatantState combatant)
    {
        var holder = new Control { CustomMinimumSize = new Vector2(170, 22) };
        var bg = new ColorRect { Color = new Color("2a1414") };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        holder.AddChild(bg);
        var ratio = combatant.Health.Max > 0 ? Mathf.Clamp((float)combatant.Health.Current / combatant.Health.Max, 0, 1) : 0;
        var fill = new ColorRect { Color = new Color("6a9a5a") };
        fill.SetAnchorsPreset(LayoutPreset.FullRect);
        fill.AnchorRight = ratio;
        fill.OffsetRight = 0;
        holder.AddChild(fill);
        var block = Block(combatant);
        var label = new Label
        {
            Text = $"{combatant.Health.Current}/{combatant.Health.Max}" + (block > 0 ? $"   🛡{block}" : ""),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetAnchorsPreset(LayoutPreset.FullRect);
        holder.AddChild(label);
        return holder;
    }

    // A hand/choice card as a black block: name, cost, and its ability text. `onClick` arms/plays it.
    private Control CardBlockButton(InteractiveCombat combat, CombatantState hero, CardInstance card, bool highlighted, Action onClick)
    {
        var definition = card.DefinitionId.value;
        var affordable = CanPay(hero, definition);
        var presentation = GameHost.Instance.Blueprint.Presentation.Cards.GetValueOrDefault(definition);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(CardVisuals.CardW, CardVisuals.CardH) };
        panel.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(
            new Color("0a0a0c"),
            highlighted ? MoonvineTheme.AccentLight : affordable ? new Color(MoonvineTheme.Accent, 0.5f) : new Color(MoonvineTheme.TextMuted, 0.25f), 6));

        var margin = new MarginContainer();
        foreach (var s in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(s, 6);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 3);

        var header = new HBoxContainer();
        var cost = new Label { Text = CostLabel(definition) };
        cost.AddThemeColorOverride("font_color", MoonvineTheme.Warning);
        cost.AddThemeFontSizeOverride("font_size", 13);
        header.AddChild(cost);
        column.AddChild(header);

        var name = new Label { Text = CardName(definition), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        name.AddThemeColorOverride("font_color", affordable ? MoonvineTheme.RarityColor(presentation?.Rarity) : MoonvineTheme.TextMuted);
        name.AddThemeFontSizeOverride("font_size", 14);
        column.AddChild(name);

        var rule = new HSeparator();
        column.AddChild(rule);

        // The rules text lives in a window of its OWN, fixed height. A Label sizes to whatever it wraps to, and
        // a card container sizes to its label — so the wordiest card in the hand used to grow past the bottom
        // of the screen and take the whole row's alignment with it. A plain Control reports only its minimum
        // size, whatever it holds, so every card in the hand is the same card-shaped block. What does not fit
        // is on the tooltip.
        var window = new Control
        {
            CustomMinimumSize = new Vector2(CardVisuals.CardW - 12, CardVisuals.CardH - 66),
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var effect = new Label
        {
            Text = presentation?.FlavorText ?? "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        effect.SetAnchorsPreset(LayoutPreset.TopWide);
        effect.AddThemeColorOverride("font_color", affordable ? MoonvineTheme.TextSoft : MoonvineTheme.TextMuted);
        effect.AddThemeFontSizeOverride("font_size", 11);
        window.AddChild(effect);
        column.AddChild(window);

        margin.AddChild(column);
        panel.AddChild(margin);

        var overlay = new Button
        {
            Flat = true,
            Disabled = !affordable,
            TooltipText = presentation?.FlavorText ?? "",
        };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.Pressed += () => onClick();
        panel.AddChild(overlay);
        return panel;
    }

    private void OnCardChoiceClicked(RunPlayback play, IReadOnlyList<CardInstance> candidates, CardInstanceId id)
    {
        var driver = play.CombatDriver!;
        if (driver.PendingCardChoiceCount == 1)
        {
            _selectedCards.Clear();
            driver.SupplyCardChoice([id]);
            return;
        }
        if (!_selectedCards.Remove(id.value) && _selectedCards.Count < driver.PendingCardChoiceCount)
            _selectedCards.Add(id.value);
        Rebuild();
    }

    private void OnOptionChoiceClicked(RunPlayback play, int index)
    {
        var driver = play.CombatDriver!;
        if (driver.PendingOptionChoiceCount == 1)
        {
            _selectedOptions.Clear();
            driver.SupplyOptionChoice([index]);
            return;
        }
        if (!_selectedOptions.Remove(index) && _selectedOptions.Count < driver.PendingOptionChoiceCount)
            _selectedOptions.Add(index);
        Rebuild();
    }

    // Capture the opening draw MID-FLIGHT (cards fanning out of the deck), to eyeball the animation.
    private async System.Threading.Tasks.Task SmokeDraw()
    {
        var session = Session;
        for (var i = 0; i < 8 && Play?.CombatDriver?.Current is null && session is not null; i++)
        {
            if (session.IsAwaitingNodeChoice) session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude) session.Continue();
            else break;
        }
        if (Play?.CombatDriver?.Current is null || DisplayServer.GetName().Contains("headless"))
        {
            GetTree().Quit();
            return;
        }
        await ToSignal(GetTree().CreateTimer(0.34), SceneTreeTimer.SignalName.Timeout);
        GetViewport().GetTexture().GetImage().SavePng("user://smoke-draw.png");
        GD.Print("smoke: screenshot user://smoke-draw.png (mid-draw)");
        GetTree().Quit();
    }

    // Verify the targeting rule through the real click handler: a block card plays on click (no arm),
    // a damage card arms (waits for an enemy click).
    private void SmokeTarget()
    {
        var session = Session;
        for (var i = 0; i < 8 && Play?.CombatDriver?.Current is null && session is not null; i++)
        {
            if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else
                break;
        }
        var combat = Play?.CombatDriver?.Current;
        if (combat is null)
        {
            GD.Print("smoke-target: no fight reached");
            GetTree().Quit();
            return;
        }

        var block = combat.Hand.FirstOrDefault(c => c.DefinitionId.value.Contains("cower"));   // gain block → self
        var attack = combat.Hand.FirstOrDefault(c => c.DefinitionId.value.Contains("paper_cut")); // deal damage → target
        var handBefore = combat.Hand.Count;

        if (block is not null)
            OnCardClicked(block.Id); // should PLAY immediately (no arm)
        var afterBlock = Play?.CombatDriver?.Current;
        GD.Print($"smoke-target: block played={afterBlock?.Hand.Count < handBefore} armed={_armedCard is not null}");

        if (attack is not null)
            OnCardClicked(attack.Id); // should ARM (wait for enemy)
        GD.Print($"smoke-target: attack armed={_armedCard is not null} played={Play?.CombatDriver?.Current?.Hand.Count < (afterBlock?.Hand.Count ?? 0)}");
        GetTree().Quit();
    }

    // Click a card. A self-only card (gain block, draw, self-buff) plays immediately — no enemy target
    // needed. A card that aims at an enemy arms for a target click (click an enemy to play, or the card
    // again to cancel).
    private void OnCardClicked(CardInstanceId cardId)
    {
        var combat = Play?.CombatDriver?.Current;
        var definition = combat?.Hand.FirstOrDefault(c => c.Id.value == cardId.value)?.DefinitionId.value;

        if (definition is not null && !NeedsTarget(definition))
        {
            _armedCard = cardId;
            PlayArmedCardAt(null); // source/self card: the engine ignores the (default) target
            return;
        }
        if (_armedCard is { } armed && armed.value == cardId.value)
        {
            _armedCard = null; // clicking the armed card again cancels
            Rebuild();
            return;
        }
        _armedCard = cardId;
        Rebuild();
    }

    // Does the card require the player to choose an enemy? (Only cards that aim at "eventTarget".) Unknown
    // (e.g. a composed card) defaults to needing one, so a damage card is never silently misfired.
    private bool NeedsTarget(string definitionId) =>
        Play is { } play && play.CardNeedsTarget.TryGetValue(definitionId, out var needs) ? needs : true;

    private void PlayArmedCardAt(CombatantId? target)
    {
        var driver = Play?.CombatDriver;
        var combat = driver?.Current;
        if (driver is null || combat is null || _armedCard is not { } armed)
            return;
        _armedCard = null;
        target ??= combat.State.Combatants
            .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
        driver.PlayCard(armed, target);
        SurfaceNewProblems();
    }

    // Rejected plays are recorded as step problems, not thrown — surface newly-appeared ones as a toast.
    private void SurfaceNewProblems()
    {
        var steps = Play?.CombatDriver?.Current?.Steps;
        if (steps is null)
        {
            _seenProblems = 0;
            return;
        }
        var problems = steps.Where(s => s.HasProblems).SelectMany(s => s.Problems).ToList();
        if (problems.Count > _seenProblems)
            Toast(problems[^1]);
        _seenProblems = problems.Count;
    }

    // ── sidebar + widgets ────────────────────────────────────────────────────────

    private void RenderSidebar(InteractiveRunSession session)
    {
        var run = session.Run;
        _sidebar.AddChild(new Label { Text = Play?.HeroName ?? "You" });
        _sidebar.AddChild(MutedLabel($"HP {run.Health.Current}/{run.Health.Max}"));
        foreach (var (resource, amount) in run.Resources.OrderBy(r => r.Key.Value, StringComparer.Ordinal))
            _sidebar.AddChild(MutedLabel($"{resource.Value}: {amount}"));

        if (run.Relics.Count > 0)
        {
            _sidebar.AddChild(new Label { Text = "Relics" });
            foreach (var relic in run.Relics)
            {
                var label = MutedLabel($"• {relic.Definition.DisplayName}{(relic.Enabled ? "" : " (off)")}");
                label.MouseFilter = MouseFilterEnum.Stop; // tooltips need a hit-testable control
                label.TooltipText = GameHost.Instance.Blueprint.Presentation.Relics
                    .GetValueOrDefault(relic.Id.Value)?.FlavorText ?? "";
                _sidebar.AddChild(label);
            }
        }
        if (run.Consumables.Count > 0)
        {
            _sidebar.AddChild(new Label { Text = "Consumables" });
            foreach (var consumable in run.Consumables)
                _sidebar.AddChild(MutedLabel($"• {consumable.DefinitionId.Value}"));
        }

        _sidebar.AddChild(new Label { Text = $"Deck ({run.Deck.Count})" });
        foreach (var group in run.Deck
            .GroupBy(card => CardName(card.DefinitionId.value) + new string('+', card.UpgradeLevel))
            .OrderBy(g => g.Key, StringComparer.Ordinal))
            _sidebar.AddChild(MutedLabel(group.Count() > 1 ? $"{group.Key} ×{group.Count()}" : group.Key));
    }

    private void Title(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", 22);
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        _main.AddChild(label);
    }

    private void Muted(string text) => _main.AddChild(MutedLabel(text));

    private static Label MutedLabel(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        return label;
    }

    private Button AddButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.Pressed += () => onPressed();
        _main.AddChild(button);
        return button;
    }

    private void Toast(string message)
    {
        var label = new Label { Text = message };
        label.AddThemeColorOverride("font_color", MoonvineTheme.Warning);
        label.SetAnchorsPreset(LayoutPreset.CenterBottom);
        label.Position -= new Vector2(0, 48);
        AddChild(label);
        GetTree().CreateTimer(2.5).Timeout += () => label.QueueFree();
    }

    // ── ported display helpers (RunSessionView.razor) ────────────────────────────

    private string Name(CombatantState combatant, InteractiveCombat combat) =>
        combatant.Id == combat.HeroId
            ? Play?.HeroName ?? "You"
            : Play!.EnemyNames.TryGetValue(combatant.Id.value, out var name) ? name : combatant.Id.value;


    private string CardName(string definitionId)
    {
        var play = Play!;
        if (play.CardNames.TryGetValue(definitionId, out var name))
            return name;
        if (definitionId.StartsWith("shred:", StringComparison.Ordinal))
            return string.Join(" + ", definitionId["shred:".Length..].Split('+')
                .Select(part => play.ShredNames.TryGetValue(part, out var partName) ? partName : part));
        return definitionId;
    }

    private IReadOnlyList<ResourceCost> FullCosts(string definitionId)
    {
        var play = Play!;
        if (play.CardFullCosts.TryGetValue(definitionId, out var costs))
            return costs;
        return play.ComposedCostsFor(definitionId)
            ?? [new ResourceCost(StandardCombatIds.EnergyResource, play.CardCosts.GetValueOrDefault(definitionId))];
    }

    private string ResourceLabel(ResourceId id) =>
        id == StandardCombatIds.EnergyResource ? "⚡"
        : Play!.ResourceNames.TryGetValue(id.value, out var name) ? name + " "
        : id.value + " ";

    private string CostLabel(string definitionId)
    {
        var costs = FullCosts(definitionId);
        return costs.Count == 0
            ? "⚡0"
            : string.Join(" · ", costs.Select(c => $"{ResourceLabel(c.ResourceId)}{c.Amount}"));
    }

    private bool CanPay(CombatantState payer, string definitionId) =>
        FullCosts(definitionId).All(cost =>
            payer.Resources.TryGetValue(cost.ResourceId, out var pool) && pool.Current >= cost.Amount);

    private string ResourcePoolsLine(CombatantState combatant)
    {
        var pools = combatant.Resources
            .OrderBy(p => p.Key == StandardCombatIds.EnergyResource ? 0 : 1)
            .ThenBy(p => p.Key.value, StringComparer.Ordinal)
            .Select(p => $"{ResourceLabel(p.Key).TrimEnd()} {p.Value.Current}{(p.Value.Max is { } max ? $"/{max}" : "")}");
        var line = string.Join(" · ", pools);
        return line.Length == 0 ? "—" : line;
    }

    private static int Block(CombatantState combatant) =>
        combatant.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0;

    // What a combatant is CARRYING, one chip per status. Everything a fight in this game turns on lives here —
    // an Act-II boss is built out of visible state (Authority, Custody, the seals, the filed hours), so a chip
    // says the status's authored NAME, not the id it is stored under, and its rules text on hover.
    //
    // The definitions come from the live fight's registry, which knows the engine's own statuses as well as the
    // game's. A status the registry cannot resolve falls back to a readable form of its id rather than to
    // nothing: an unnamed status is a content gap, not a reason to hide state from the player.
    private static Control? StatusChips(InteractiveCombat combat, CombatantState combatant)
    {
        var registry = combat.State.DefinitionRegistry;
        var shown = combatant.Statuses.Where(s => s.Visibility == StatusVisibility.Visible).ToList();
        if (shown.Count == 0)
            return null;

        var flow = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.Center };
        flow.AddThemeConstantOverride("h_separation", 8);

        foreach (var status in shown)
        {
            StatusDefinition? definition = null;
            registry?.TryGetStatus(status.DefinitionId, out definition);

            var chip = new Label
            {
                Text = StatusText(status, definition),
                MouseFilter = Control.MouseFilterEnum.Pass, // let the targeting overlay keep the click
                TooltipText = StatusTooltip(status, definition),
            };
            chip.AddThemeColorOverride("font_color", status.Polarity switch
            {
                StatusPolarity.Buff => MoonvineTheme.Accent,
                StatusPolarity.Debuff => MoonvineTheme.Danger,
                _ => MoonvineTheme.TextMuted,
            });
            flow.AddChild(chip);
        }
        return flow;
    }

    // The chips as one line of text — what the headless checks read, and what a log line would say.
    private static string StatusLine(InteractiveCombat combat, CombatantState combatant)
    {
        var registry = combat.State.DefinitionRegistry;
        return combatant.Statuses.Count == 0
            ? "-"
            : string.Join("  ", combatant.Statuses.Select(status =>
            {
                StatusDefinition? definition = null;
                registry?.TryGetStatus(status.DefinitionId, out definition);
                return StatusText(status, definition);
            }));
    }

    // "Scheduled: The Collapse 2t" — the name, then whatever the status is counting.
    //
    // The magnitude comes from the INSTANCE, not from the definition's ShowStacksInUi/…InUi flags: a blueprint
    // does not carry those, so the engine leaves them false for every authored status, and honouring them here
    // would hide every number in the game. What the instance holds is what the player is owed.
    private static string StatusText(StatusInstance status, StatusDefinition? definition)
    {
        var name = definition is not null && !string.IsNullOrWhiteSpace(definition.DisplayNameKey)
            ? definition.DisplayNameKey
            : Humanized(status.DefinitionId.value);

        // …filtered by what the status is DECLARED to count (the blueprint does carry that), so a plain marker
        // does not read "Paper Seals Wax ×1" while a stacking debuff still counts up.
        var magnitude = status.Stacks > 0 && (definition?.UsesStacks ?? true) ? $" ×{status.Stacks}"
            : status.DurationTurns > 0 && (definition?.UsesDuration ?? true) ? $" {status.DurationTurns}t"
            : status.Charges > 0 && (definition?.UsesCharges ?? true) ? $" {status.Charges}c" : "";
        // A status that has not taken effect yet is state the player can still answer — say so.
        var pending = status.PendingTurns > 0 ? $" (in {status.PendingTurns})" : "";
        return $"{name}{magnitude}{pending}";
    }

    private static string StatusTooltip(StatusInstance status, StatusDefinition? definition)
    {
        var description = definition?.DescriptionKey;
        return string.IsNullOrWhiteSpace(description)
            ? StatusText(status, definition)
            : $"{StatusText(status, definition)}\n{description}";
    }

    // "scheduled_the_collapse" → "Scheduled the collapse". Only ever seen when content forgot a name.
    private static string Humanized(string id)
    {
        var text = id.Replace("standard.", "", StringComparison.Ordinal)
            .Replace("event.", "", StringComparison.Ordinal)
            .Replace('.', ' ').Replace('-', ' ').Replace('_', ' ');
        return text.Length == 0 ? id : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
