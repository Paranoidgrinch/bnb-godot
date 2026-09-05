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
    private static bool _fastForward;   // a smoke probe is walking: draw the screen, do not animate it
    private Control? _enemyRow;   // the live enemy row, so a probe can measure what the layout did with it

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

        _fastForward = OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--smoke", StringComparison.Ordinal))
            || IsSimulating;

        if (IsSimulating)
            _ = SimulateRun();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-run"))
            SmokeRun();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-target"))
            SmokeTarget();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-draw"))
            _ = SmokeDraw();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-map"))
            _ = MapShot(); // a fresh run parks at the entry fork — screenshot the map
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
            _ = SmokeMarathon();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-crowd"))
            _ = SmokeCrowd();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-boss"))
            _ = SmokeBoss(BossActArgument());
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-tooltips"))
            _ = SmokeTooltips();
    }

    // Walk the screen the way a mouse would and report what is EXPLAINED and what is not: every piece of text
    // on it, whether hovering it says anything, and — the part that matters — every label that uses a word the
    // glossary knows while offering no hover at all. A name with no explanation is the thing this checks for.
    private async System.Threading.Tasks.Task SmokeTooltips()
    {
        // Get into a fight first: combat is where the most named things are on screen at once.
        var session = Session;
        var play = Play;
        for (var step = 0; step < 400 && session is not null && play is not null; step++)
        {
            if (play.CombatDriver?.Current is not null && session.Run.VisitedNodes.Count > 1)
                break;
            if (play.CombatDriver?.Current is { } fight)
            {
                if (!fight.IsHeroTurn)
                    break;
                var hero = fight.State.GetCombatant(fight.HeroId);
                var card = fight.Hand.FirstOrDefault(c =>
                    !c.DefinitionId.value.Contains("red_tape") && CanPay(hero, c.DefinitionId.value));
                var target = fight.State.Combatants
                    .FirstOrDefault(c => c.Id != fight.HeroId && c.IsAlive
                        && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
                if (card is not null)
                    play.CombatDriver.PlayCard(card.Id, target);
                else
                    play.CombatDriver.EndTurn();
            }
            else if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices
                    .FirstOrDefault(n => n.HasTag(MapNodeTags.Combat))?.Id.Value
                    ?? session.PendingNodeChoices[0].Id.Value);
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
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ReportTooltips("combat");
        GetTree().Quit();
    }

    // What on this screen is explained, and what names something without offering a hover.
    private void ReportTooltips(string screen)
    {
        var explained = 0;
        var mute = new List<string>();
        var samples = new List<string>();
        var all = 0;
        Walk(this, "");

        // `inherited` is the tooltip the mouse would actually find: a label that lets the pointer through
        // (MouseFilter.Ignore, the default for a Label) is hovered as whatever sits beneath it, so a card's
        // rules text is explained by the card's own hover and must not be counted as mute.
        void Walk(Godot.Node node, string inherited)
        {
            foreach (var child in node.GetChildren())
            {
                var passed = inherited;
                if (child is Control control)
                {
                    if (!string.IsNullOrWhiteSpace(control.TooltipText))
                        passed = control.TooltipText;
                    if (TextOf(control) is { Length: > 0 } text)
                    {
                        all++;
                        if (!string.IsNullOrWhiteSpace(passed))
                        {
                            explained++;
                            if (passed.Contains(" — ", StringComparison.Ordinal))
                                samples.Add(passed);
                        }
                        else if (Glossary.In(text, limit: 1).Count > 0)
                            mute.Add(text.Replace("\n", " · "));
                    }
                }
                Walk(child, passed);
            }
        }

        GD.Print($"smoke-tooltips [{screen}]: {all} labelled controls, {explained} with a hover, "
            + $"{mute.Count} naming something the glossary knows with no hover at all");
        foreach (var line in mute.Distinct().Take(15))
            GD.Print($"  UNEXPLAINED: {line}");
        // A couple of the actual hovers, so the check reports what a player would READ, not just that a
        // string is non-empty.
        foreach (var sample in samples.Distinct().Take(3))
            GD.Print($"  EXAMPLE ⟨{sample.Replace("\n", " ⏎ ")}⟩");
    }

    private static string? TextOf(Control control) => control switch
    {
        Button button => button.Text,
        Label label => label.Text,
        _ => null,
    };

    // Play the WHOLE game — every act, every room the route holds, to the last god — through the real screens
    // (every answer goes through the same Rebuild the player sees). What it proves is that the frontend holds
    // up all the way: the map redraws for each new act, the act title card fires, the gauntlet's roll call and
    // divine rule area appear where they should, no screen throws a hundred rooms in. The engine-side coverage
    // lives in bnb-content's own walk; this one is about the UI.
    private async System.Threading.Tasks.Task SmokeMarathon()
    {
        var session = Session;
        var play = Play;
        var rooms = new List<string>();
        // What each room COST to answer, in wall-clock seconds and in answers. Every answer replays the run
        // from its baseline, so this is the one number that says whether a fourth act is affordable — a per-act
        // mean the report can state rather than a hundred room lines somebody has to read.
        var roomCost = new List<(int Act, double Seconds, int Answers)>();
        var acts = 1;
        string? lastRoom = null;
        var roomOpenedAt = 0.0;
        var roomOpenedAtStep = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var reason = "the run finished";

        // The greedy player's three guards, learned the hard way by bnb-content's RunWalker: a play the engine
        // REFUSED must not be tried again, a play that moved NOTHING on the table must not be repeated (a card
        // is allowed to put a fresh copy of itself back in your hand — Act III's Make Amends does it on purpose
        // — and a greedy player will then play it for ever), and both a turn and a fight need a ceiling. A
        // probe without them does not fail: it spends its whole step budget in one room and reports "Ongoing".
        // Note what a fight is NOT identified by: the InteractiveCombat object. Under the replay model the
        // fight is rebuilt from the blueprint on every single answer, so comparing instances says "a new fight"
        // every step and silently resets every counter below. A fight begins when the driver has one and ends
        // when it no longer does; turns are counted where this probe itself ends them.
        var inFight = false;
        var turn = 0;
        var playsThisTurn = 0;
        var refused = new HashSet<CardInstanceId>();
        var barren = new HashSet<string>(StringComparer.Ordinal);
        string? lastPlayed = null;
        var tableBeforeThePlay = "";
        void NewTurn()
        {
            playsThisTurn = 0;
            lastPlayed = null;
            refused.Clear();
            barren.Clear();
        }

        for (var step = 0; step < 20000 && session is not null && play is not null && !session.IsComplete; step++)
        {
            if (session.Error is not null || play.Error is not null)
            {
                reason = "an error was raised";
                break;
            }
            // Let the engine breathe. Every answer rebuilds this screen out of fresh Control nodes and frees
            // the old ones with QueueFree, which is DEFERRED: a walk that never yields never lets the tree
            // collect anything, and the whole game's worth of discarded screens is 14 GB of resident memory —
            // the marathon printed its Victory line and was then killed by the OOM killer on the way out.
            // One frame every twenty answers costs nothing and keeps it under a normal footprint.
            if (step % 20 == 19)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (session.Run.CurrentNodeId?.Value is { } here && here != lastRoom)
            {
                lastRoom = here;
                var node = session.Run.Map.Nodes.FirstOrDefault(n => n.Id.Value == here);
                rooms.Add($"{session.Run.ActNumber}:{MapView.Role(node ?? throw new InvalidOperationException(here))}");
                if (rooms.Count > 1)
                    roomCost.Add((acts, clock.Elapsed.TotalSeconds - roomOpenedAt, step - roomOpenedAtStep));
                roomOpenedAt = clock.Elapsed.TotalSeconds;
                roomOpenedAtStep = step;
                acts = Math.Max(acts, session.Run.ActNumber);
                // The latency curve, room by room: under the replay model every answer re-runs the whole run,
                // so what this prints is how the game FEELS as it gets longer.
                GD.Print($"  [{clock.Elapsed.TotalSeconds,7:0.0}s, {step,5} answers] "
                    + $"act {session.Run.ActNumber} {here} {rooms[^1].Split(':')[1]}");
            }

            if (play.CombatDriver?.Current is null && inFight)
            {
                inFight = false;
                turn = 0;
                NewTurn();
            }

            if (play.CombatDriver is { Current: not null } driver)
            {
                inFight = true;
                if (driver.PendingOptionChoice is { } options)
                    driver.SupplyOptionChoice(
                        [.. Enumerable.Range(0, Math.Min(driver.PendingOptionChoiceCount, options.Count))]);
                else if (driver.PendingCardChoice is { } cards)
                    driver.SupplyCardChoice([.. cards.Take(driver.PendingCardChoiceCount).Select(c => c.Id)]);
                else if (driver.Current!.IsHeroTurn)
                {
                    var combat = driver.Current;

                    // A play only FINISHES here: a card that asks a question parks halfway through its own
                    // resolution, so a reading taken the moment PlayCard returns straddles an open question.
                    if (lastPlayed is { } finished)
                    {
                        if (TableState(combat) == tableBeforeThePlay)
                            barren.Add(finished);
                        lastPlayed = null;
                    }

                    var hero = combat.State.GetCombatant(combat.HeroId);
                    var card = combat.Hand.FirstOrDefault(c =>
                        !c.DefinitionId.value.Contains("red_tape") && !c.DefinitionId.value.Contains("unsigned_form")
                        && !refused.Contains(c.Id) && !barren.Contains(c.DefinitionId.value)
                        && CanPay(hero, c.DefinitionId.value));
                    var target = combat.State.Combatants
                        .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive
                            && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
                    if (card is not null)
                    {
                        var stepsBefore = combat.Steps.Count;
                        tableBeforeThePlay = TableState(combat);
                        lastPlayed = card.DefinitionId.value;
                        driver.PlayCard(card.Id, target);
                        if (Refused(driver.Current, stepsBefore))
                            refused.Add(card.Id);
                        if (++playsThisTurn >= PlaysInATurnNobodyMakes)
                        {
                            reason = $"a turn at {Where(session)} played {playsThisTurn} cards without ending "
                                + $"— last '{card.DefinitionId.value}'";
                            break;
                        }
                    }
                    else
                    {
                        driver.EndTurn();
                        NewTurn();
                        if (++turn >= TurnsAFightShouldNotNeed)
                        {
                            reason = $"the fight at {Where(session)} did not end in {turn} turns";
                            break;
                        }
                    }
                }
                else
                {
                    // The enemy turn resolves synchronously under replay, so parking here means the fight is
                    // waiting for something this probe does not know how to answer. Say which fight, and say so.
                    reason = $"the fight at {Where(session)} parked on the enemy's turn";
                    break;
                }
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
            {
                reason = $"nothing at {Where(session)} was awaiting an answer";
                break;
            }

            if (step == 19999)
                reason = $"the step limit ran out at {Where(session)}";
        }

        var costByAct = roomCost.GroupBy(c => c.Act).ToDictionary(
            g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
            g => (Seconds: g.Sum(c => c.Seconds), Rooms: g.Count(), Answers: g.Sum(c => c.Answers)));
        var byAct = rooms.GroupBy(r => r.Split(':')[0])
            .Select(g =>
            {
                var cost = costByAct.GetValueOrDefault(g.Key);
                var perRoom = cost.Rooms == 0 ? 0 : cost.Seconds / cost.Rooms;
                return $"act {g.Key}: {g.Count()} rooms ({string.Join(" ", g.GroupBy(x => x.Split(':')[1]).Select(k => $"{k.Key}×{k.Count()}"))}) "
                    + $"— {cost.Seconds:0.0}s, {perRoom:0.0}s/room over {cost.Answers} answers";
            });
        GD.Print($"smoke-marathon: result={session?.Run.Result} acts={acts} rooms={rooms.Count} "
            + $"seconds={clock.Elapsed.TotalSeconds:0.0} "
            + $"error={session?.Error ?? Play?.Error ?? "none"} stopped because {reason}");
        foreach (var line in byAct)
            GD.Print($"  {line}");
        GetTree().Quit();
    }

    private const int PlaysInATurnNobodyMakes = 50;
    private const int TurnsAFightShouldNotNeed = 100;

    // Everything about the table a play could visibly move. The EXHAUST PILE is deliberately not in it: a card
    // that burns itself and puts a fresh copy back in hand grows that pile on every play, which would make
    // exactly the loop this reading exists to find look busy for ever. Statuses count their STACKS as well as
    // their number, because paying a debt down usually moves the stack and not the count.
    private static string TableState(InteractiveCombat combat)
    {
        var hero = combat.State.GetCombatant(combat.HeroId);
        var energy = hero.Resources.TryGetValue(StandardCombatIds.EnergyResource, out var pool) ? pool.Current : 0;
        var enemies = combat.State.Combatants.Where(c => c.Id != combat.HeroId).ToList();
        var zones = combat.State.GetCardZones(combat.HeroId);
        int Count(CardZone zone) => zones.GetCardsInZone(zone).Count;
        static int Stacks(IEnumerable<StatusInstance> statuses) => statuses.Sum(status => status.Stacks);
        return $"{energy}/{hero.Health.Current}/{hero.Statuses.Count}/{Stacks(hero.Statuses)}/"
            + $"{Count(CardZone.Hand)}/{Count(CardZone.DiscardPile)}/{Count(CardZone.DrawPile)}/"
            + $"{enemies.Sum(e => e.Health.Current)}/{enemies.Sum(e => e.Statuses.Count)}/"
            + $"{enemies.Sum(e => Stacks(e.Statuses))}";
    }

    // Did the play go through? The fight records every attempt as a step, and a refused one carries the reason;
    // nothing new at all means the driver dropped it (a prompt opened, say).
    private static bool Refused(InteractiveCombat? combat, int stepsBefore)
    {
        if (combat is null)
            return false;
        var steps = combat.Steps;
        return steps.Count <= stepsBefore || steps.Skip(stepsBefore).Any(step => step.HasProblems);
    }

    // Where the run stands, in the two names that identify a room: its map id and what is being fought there.
    private static string Where(InteractiveRunSession session)
    {
        var here = session.Run.CurrentNodeId?.Value ?? "nowhere";
        var node = session.Run.Map.Nodes.FirstOrDefault(n => n.Id.Value == here);
        var content = node?.Payload switch
        {
            EncounterRef fight => fight.Id.Value,
            EventRef door => door.Id.Value,
            ShopRef shop => shop.Id.Value,
            { } payload => payload.GetType().Name,
            _ => "—",
        };
        return $"act {session.Run.ActNumber} {here} ({content})";
    }

    // THE WIDEST FIGHT THE GAME CAN PUT ON ONE SCREEN, which nothing had ever looked at. A boss with three
    // volumes standing beside it is four enemy bodies plus the hero, and this is the one thing a fight cannot
    // report about itself: every rule resolves correctly while the fifth column sits past the right edge with
    // its health bar and its intent on it.
    //
    // So the probe walks to a crowd, measures what the layout actually did — the row's own width against the
    // room it was given — and says whether anything is off the screen. Then it captures the frame, because
    // "it fits" and "it reads" are two different questions and only one of them is arithmetic.
    private async System.Threading.Tasks.Task SmokeCrowd()
    {
        const int Wanted = 3;   // three enemies + the hero = the four bodies G-5 asks about; four is the most
        var crowded = new[] { MapNodeTags.MultiCombat, MapNodeTags.Elite, MapNodeTags.Boss };

        // Which fights an act fields is drawn per run, so ONE seed is not a search: seed 7 walks both acts
        // without ever meeting a third body. Try a handful and stop at the first crowd — this probe exists to
        // look at a wide fight, and a run that has none has nothing to say about how a wide fight is drawn.
        var best = 0;
        foreach (var seed in new[] { 5, 7, 1, 2, 3, 4, 6, 8 })   // 5 is the one this search first found
        {
            GameHost.Instance.StartNewRun(seed, health: 9999);
            best = Math.Max(best, await WalkUntil(
                stop: () => Enemies().Count >= Wanted,
                prefer: node => crowded.Any(node.HasTag),
                budget: 700));
            if (Enemies().Count >= Wanted)
            {
                GD.Print($"  crowd found on seed {seed}");
                break;
            }
        }

        Rebuild();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var bodies = Enemies().Count;
        var row = _enemyRow;
        var rowWidth = row is not null && IsInstanceValid(row) ? row.Size.X : -1;
        var rowRight = row is not null && IsInstanceValid(row) ? row.GlobalPosition.X + rowWidth : -1;
        var screen = GetViewportRect().Size.X;

        GD.Print($"smoke-crowd: enemies={bodies} (widest reached {best}) row={rowWidth:0} "
            + $"right={rowRight:0}/{screen:0} offscreen={(rowRight > screen + 1 ? "YES" : "no")} "
            + $"error={Session?.Error ?? Play?.Error ?? "none"}");
        GD.Print($"  facing: {Facing()}");
        if (bodies < Wanted)
            GD.Print($"  NOTE no fight of {Wanted}+ bodies was reached inside the budget");

        ReportTooltips("crowd");
        await CaptureThenQuit("smoke-crowd.png");
    }

    // The eyes-on pass the presentation work is judged by: stand in a named act's BOSS fight and capture it.
    // A boss is the only place the phase banner, the dial and a five-body row can all be wrong at once, and
    // walking there is the only way to see them — the fight cannot report its own layout.
    private async System.Threading.Tasks.Task SmokeBoss(int act)
    {
        await WalkUntil(
            stop: () => Session is { } s && s.Run.ActNumber >= act
                && Play?.CombatDriver?.Current is not null && AtABoss(),
            prefer: node => node.HasTag(MapNodeTags.Boss),
            budget: 5000);

        Rebuild();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var combat = Play?.CombatDriver?.Current;
        GD.Print($"smoke-boss {act}: act={Session?.Run.ActNumber} boss={(AtABoss() ? "yes" : "NO")} "
            + $"round={combat?.Round} ended={_walkEnded} error={Session?.Error ?? Play?.Error ?? "none"}");
        GD.Print($"  facing: {Facing()}");
        // The Divine Rule Area, read back out of the tree it was built into: a headless probe cannot take a
        // screenshot, so this is the only way to say that the one UI surface Act V's design REQUIRES is
        // actually on the screen and not merely a method that returned without throwing.
        GD.Print($"  divine rule area: {DivineRuleOnScreen() ?? "none"}");
        if (combat is not null)
            foreach (var body in combat.State.Combatants)
                GD.Print($"  [{Name(body, combat)}] {StatusLine(combat, body)}");

        ReportTooltips($"boss{act}");
        await CaptureThenQuit($"smoke-boss{act}.png");
    }

    // What the Divine Rule Area currently says, read out of the LIVE labels rather than out of the document,
    // so a panel that was never added to the tree reads as "none".
    private string? DivineRuleOnScreen()
    {
        var lines = new List<string>();
        void Collect(Godot.Node node)
        {
            if (node is Label label && !string.IsNullOrWhiteSpace(label.Text))
                lines.Add(label.Text);
            foreach (var child in node.GetChildren())
                Collect(child);
        }
        Collect(_combatRoot);

        var titles = GameHost.Instance.Blueprint.Presentation.Encounters.Values
            .Select(e => e.Extra.GetValueOrDefault("divineRuleTitle"))
            .Where(title => !string.IsNullOrEmpty(title))
            .ToHashSet();
        var at = lines.FindIndex(titles.Contains!);
        if (at < 0)
            return null;
        return at + 1 < lines.Count ? $"{lines[at]} — {lines[at + 1]}" : lines[at];
    }

    // `--smoke-boss 3` — the act to walk to, defaulting to the first.
    private static int BossActArgument()
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, "--smoke-boss");
        return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var act) ? act : 1;
    }

    private bool AtABoss() =>
        Session?.Run.CurrentNodeId?.Value is { } id
        && Session.Run.Map.Nodes.FirstOrDefault(n => n.Id.Value == id) is { } node
        && node.HasTag(MapNodeTags.Boss);

    private IReadOnlyList<CombatantState> Enemies() =>
        Play?.CombatDriver?.Current is { } fight
            ? [.. fight.State.Combatants.Where(c => c.Id != fight.HeroId && c.TeamId == StandardCombatIds.EnemyTeam)]
            : [];

    private string Facing() =>
        Play?.CombatDriver?.Current is { } fight
            ? string.Join(", ", Enemies().Select(c => Name(c, fight)))
            : "-";

    // ONE greedy walker for both probes: `stop` is what this probe came to look at, `prefer` steers the map.
    // Returns the widest fight it met on the way.
    //
    // Under the replay model every answer re-runs the whole run, so a walk is quadratic in its own length and
    // the budget is a real cost, not a formality. The three guards below are the marathon's, learned the hard
    // way: a play the engine REFUSED must not be retried, a play that moved nothing on the table must not be
    // repeated (a card may put a fresh copy of itself in your hand, and a greedy player will then play it for
    // ever), and a turn needs a ceiling.
    private string _walkEnded = "-";

    private async System.Threading.Tasks.Task<int> WalkUntil(
        Func<bool> stop, Func<RogueDeck.Run.Node, bool> prefer, int budget)
    {
        _walkEnded = "the budget ran out";
        var session = Session;
        var play = Play;
        var best = 0;
        var playsThisTurn = 0;
        var refused = new HashSet<CardInstanceId>();
        var barren = new HashSet<string>(StringComparer.Ordinal);
        var timesPlayed = new Dictionary<string, int>(StringComparer.Ordinal);
        string? lastPlayed = null;
        string? lastRoom = null;
        var tableBeforeThePlay = "";
        void NewTurn()
        {
            playsThisTurn = 0;
            lastPlayed = null;
            refused.Clear();
            barren.Clear();
            timesPlayed.Clear();
        }

        for (var step = 0; step < budget && session is not null && play is not null && !session.IsComplete; step++)
        {
            if (session.Error is not null || play.Error is not null)
                break;
            // Let the engine breathe. Every answer redraws this screen, and a redraw queues deferred layout
            // work; a walk that never yields fills Godot's message queue and takes the process down with
            // "Message queue out of memory" somewhere in the second act. One frame every twenty answers is
            // cheap and is the difference between a probe that reaches Act III and one that dies on the way.
            if (step % 20 == 19)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            best = Math.Max(best, Enemies().Count);
            if (session.Run.CurrentNodeId?.Value is { } here && here != lastRoom)
            {
                lastRoom = here;
                var node = session.Run.Map.Nodes.FirstOrDefault(n => n.Id.Value == here);
                GD.Print($"  [{step,5}] act {session.Run.ActNumber} {here} "
                    + $"{(node is null ? "?" : MapView.Role(node))}");
            }

            if (play.CombatDriver is { Current: not null } driver)
            {
                // Stop only with the turn in the player's hands and nothing half-answered, or the screen the
                // probe captures is a screen no player ever sees.
                if (driver.PendingOptionChoice is null && driver.PendingCardChoice is null
                    && driver.Current!.IsHeroTurn && stop())
                {
                    _walkEnded = "it found what it came for";
                    break;
                }

                if (driver.PendingOptionChoice is { } options)
                    driver.SupplyOptionChoice(
                        [.. Enumerable.Range(0, Math.Min(driver.PendingOptionChoiceCount, options.Count))]);
                else if (driver.PendingCardChoice is { } cards)
                    driver.SupplyCardChoice([.. cards.Take(driver.PendingCardChoiceCount).Select(c => c.Id)]);
                else if (driver.Current!.IsHeroTurn)
                {
                    var combat = driver.Current;
                    if (lastPlayed is { } finished)
                    {
                        if (TableState(combat) == tableBeforeThePlay)
                            barren.Add(finished);
                        lastPlayed = null;
                    }

                    var hero = combat.State.GetCombatant(combat.HeroId);
                    var card = combat.Hand.FirstOrDefault(c =>
                        !c.DefinitionId.value.Contains("red_tape") && !c.DefinitionId.value.Contains("unsigned_form")
                        && !refused.Contains(c.Id) && !barren.Contains(c.DefinitionId.value)
                        && CanPay(hero, c.DefinitionId.value));
                    var target = combat.State.Combatants
                        .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive
                            && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
                    if (card is not null)
                    {
                        var stepsBefore = combat.Steps.Count;
                        tableBeforeThePlay = TableState(combat);
                        lastPlayed = card.DefinitionId.value;
                        driver.PlayCard(card.Id, target);
                        if (Refused(driver.Current, stepsBefore))
                            refused.Add(card.Id);
                        // A fourth guard the marathon's three do not cover: Act III's Make Amends puts a fresh
                        // COPY of itself in your hand, so every play moves the table and none of them is
                        // barren — a greedy player plays it fifty times and the walk dies in the elite before
                        // the boss. Nobody plays one card six times in a turn.
                        timesPlayed[card.DefinitionId.value] = timesPlayed.GetValueOrDefault(card.DefinitionId.value) + 1;
                        if (timesPlayed[card.DefinitionId.value] >= 6)
                            barren.Add(card.DefinitionId.value);
                        // A turn that long is a greedy player, not a stuck one: by Act III a deck that makes
                        // its own cards really can play fifty times. The marathon STOPS there because a walk
                        // that never ends a turn is what it is looking for; this probe is trying to get
                        // somewhere, so it ends the turn and carries on. The budget is still the ceiling.
                        if (++playsThisTurn >= PlaysInATurnNobodyMakes)
                        {
                            driver.EndTurn();
                            NewTurn();
                        }
                    }
                    else
                    {
                        driver.EndTurn();
                        NewTurn();
                    }
                }
                else
                {
                    _walkEnded = "the fight parked on the enemy's turn";
                    break;   // nothing this probe can answer
                }
            }
            else if (session.IsAwaitingNodeChoice)
            {
                var wanted = session.PendingNodeChoices.FirstOrDefault(prefer)
                    ?? session.PendingNodeChoices[^1];
                session.PickNode(wanted.Id.Value);
                NewTurn();
            }
            else if (session.IsAwaitingEntities && session.PendingEntities is { } entities)
                session.PickEntities([.. Enumerable.Range(0, Math.Min(entities.Count, entities.Displays.Count))]);
            else if (session.IsAwaitingChoice)
                session.Pick(session.PendingChoices[^1].Id);   // the last option is the way OUT of a room
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else
            {
                _walkEnded = "nothing was awaiting an answer";
                break;
            }
        }
        if (session?.Error is not null || play?.Error is not null)
            _walkEnded = "an error was raised";
        return best;
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
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ReportTooltips(role);
        await CaptureThenQuit(file);
    }

    private async System.Threading.Tasks.Task MapShot()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ReportTooltips("map");
        await CaptureThenQuit("smoke-map.png");
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
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ReportTooltips("reward");
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
        _enemyRow = null;
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
        {
            // NO BETWEEN-ROOMS SCREEN ANY MORE. It existed to give the run a moment it could be saved at, and
            // the run now saves itself; all it did besides was make the player press Continue to be allowed to
            // look at the map. So it is walked straight through, and what it offered — consumables, and the
            // map itself — is on the room-choice screen the player lands on instead.
            session.Continue();
            GameHost.Instance.AutoSave();
            return;
        }
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
        // A GAUNTLET SAYS WHO IS COMING, and the title card is where "from the beginning of the act" actually
        // is: Act V draws three gods of six, and the design requires the three and their order to be visible
        // before the first of them is fought, not after.
        if (RollCall(session.Run) is { Count: > 1 } gods)
            name += $"\n\n{string.Join("  ▸  ", gods)}";
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
            // A title card can name a rule — Act V's roll call is three gods called after the things they
            // do — and the seconds it is up are seconds a player may reach for one of those names.
            TooltipText = Glossary.Explain(null, text),
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

        var prose = Say(situation.TextKey ?? situation.Id);
        Title(prose);
        Explained(prose);
        foreach (var choice in session.PendingChoices)
        {
            var id = choice.Id;
            var text = Say(choice.TextKey ?? id);
            AddButton(text, () => { session.Pick(id); GameHost.Instance.AutoSave(); })
                .TooltipText = Glossary.Explain(null, text);
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
        var hover = Glossary.Explain(canBuy ? null : "Not enough gold.", $"{name} {description}");
        // On the whole row, so the rules line under the button explains its own words too.
        var row = new VBoxContainer { TooltipText = hover, MouseFilter = MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", 0);
        var button = new Button { Text = $"{name}   —   {price} gold", Disabled = !canBuy };
        button.Pressed += () => session.Pick(choiceId);
        button.TooltipText = hover;
        if (!canBuy)
            button.AddThemeColorOverride("font_disabled_color", MoonvineTheme.TextMuted);
        row.AddChild(button);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var text = MutedLabel(description);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.AddThemeFontSizeOverride("font_size", 12);
            text.MouseFilter = MouseFilterEnum.Stop;
            text.TooltipText = hover;
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
        // The shop's four shelves (BnB_Run_Systems_Master §4.1). Each is named for what stands on it, because
        // "3 General cards and 4 Character cards" is a promise the player can only see kept if the headings
        // say which is which.
        "cards-general" => "Cards",
        "cards-character" => "Bureaucrat cards",
        "relics-shop" => "Shop relics",
        "relics-normal" => "Relics",
        "stock" => "For sale",
        "reward" => "Your reward",
        // A reward that knows what it is asks under its own name. The boss's relic used to arrive on the same
        // screen, under the same word, as the card pick that came before it — the one thing the fight was for,
        // with nothing to say so.
        "reward-card" => "Your reward",
        "reward-relic" => "The relic you won",
        "reward-consumable" => "What you carry away",
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
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 0),
            TooltipText = Glossary.Explain(null, $"{name} {description}"),
        };
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
        // What the between-rooms screen used to offer, now that there is no between-rooms screen.
        foreach (var consumable in session.Run.Consumables.Where(c => c.UseEffects.Count > 0))
        {
            var id = consumable.Id;
            AddButton($"Use {consumable.DefinitionId.Value}", () => session.UseConsumable(id));
        }
        AddMap(session.PendingNodeChoices.Select(n => n.Id.Value),
            node => { session.PickNode(node); GameHost.Instance.AutoSave(); });
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

        // A GAUNTLET SAYS WHO IS COMING. Act V draws three gods of six and the design requires that the three
        // and their order are visible from the beginning of the act — an act with no rooms to read ahead has
        // nothing else to tell the player what it is going to be.
        if (RollCall(run) is { Count: > 1 } gods)
            Muted($"{gods.Count} bosses, in this order: {string.Join("  ▸  ", gods)}");
    }

    // The named fights of an act whose map is nothing but bosses, in the order the run will meet them. Empty
    // for an ordinary act, whose one boss is not announced in advance.
    private static IReadOnlyList<string> RollCall(RunState run)
    {
        var bosses = run.Map.Nodes.Where(n => n.HasTag(MapNodeTags.Boss)).ToList();
        if (bosses.Count <= 1)
            return [];
        var presentation = GameHost.Instance.Blueprint.Presentation;
        return
        [
            .. bosses
                .Select(n => n.Payload is EncounterRef fight
                    ? presentation.Encounters.GetValueOrDefault(fight.Id.Value)?.FlavorText ?? fight.Id.Value
                    : MapView.Label(MapNodeTags.Boss)),
        ];
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

        // THE DIVINE RULE AREA, if this fight has one. Directly under the round and over the arena: the same
        // place in every one of Act V's fights, which is the whole of the design's shared rule for the act —
        // the player must be able to look at one spot and read what reality currently means here.
        if (DivineRuleArea() is { } divine)
            col.AddChild(divine);

        // Arena: hero far left, enemies far right, a stretchy gap between.
        var arena = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        var heroBox = CombatantBox(combat, hero, isHero: true, HeroColumn);
        heroBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        arena.AddChild(heroBox);
        // The enemies take the rest of the room and stand at the far end of it. A FLOW row, not a fixed one:
        // the widest fight in the game is four bodies beside the hero, and five 200-wide columns with their
        // gaps do not fit across 1280 — a fixed row does not shrink, it simply walks off the right edge and
        // takes a boss's intent with it. Wrapping costs the ordinary two-body fight nothing (a single line,
        // right-aligned, exactly as before) and keeps the crowded one on the screen.
        var enemyRow = new HFlowContainer
        {
            Alignment = FlowContainer.AlignmentMode.End,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        enemyRow.AddThemeConstantOverride("h_separation", enemies.Count <= 2 ? ColumnGap : CrowdGap);
        enemyRow.AddThemeConstantOverride("v_separation", CrowdGap);
        var column = EnemyColumn(enemies.Count);
        foreach (var enemy in enemies)
            enemyRow.AddChild(CombatantBox(combat, enemy, isHero: false, column));
        arena.AddChild(enemyRow);
        _enemyRow = enemyRow;

        // The arena SCROLLS if it has to. A column is as tall as what the body is carrying, and by an Act-II
        // boss the player can be wearing a dozen statuses: the column then grows past its share, and a
        // VBoxContainer hands out minimum heights before it hands out the leftovers — so the hand, the deck
        // and the End-turn button were pushed off the bottom of the screen by the very state this pass is
        // about making readable. A scroll view has a small minimum of its own, so the hand keeps its place and
        // nothing is hidden: what does not fit is reachable rather than gone.
        var arenaView = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        arena.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        arenaView.AddChild(arena);
        col.AddChild(arenaView);

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
        endTurn.Pressed += () =>
        {
            _armedCard = null;
            play.CombatDriver.EndTurn();
            SurfaceNewProblems();
            GameHost.Instance.AutoSave();
        };
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

        // …unless a probe is fast-forwarding. A walk to Act III rebuilds this screen a few thousand times, and
        // every rebuild would queue a tween onto card nodes the NEXT rebuild frees: Godot fills the log with
        // "object was deleted while awaiting a callback" and then segfaults. The flourish is worth nothing to
        // a probe, and the frame it finally captures is a settled hand rather than one mid-flight.
        if (_cardsToAnimate.Count > 0 && !_fastForward)
            CallDeferred(nameof(AnimateDraws));
        else
            _cardsToAnimate.Clear();
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

    private const int HeroColumn = 200;
    private const int NarrowestColumn = 110;
    private const int ColumnGap = 24;
    private const int CrowdGap = 12;   // a crowd spends its room on the columns, not on the air between them

    // How wide ONE enemy column may be, given how many of them there are.
    //
    // Five bodies at the hero's width do not fit across the combat pane, and a row of fixed columns does not
    // shrink — it walks off the right edge and takes a boss's health bar and intent with it. So the crowd
    // shares out the room it actually has: two bodies look exactly as they always did, and four make four
    // narrower columns rather than four off-screen ones. The floor is what a name and an intent still read in;
    // below it the flow row wraps instead, which is ugly but on the screen.
    private int EnemyColumn(int count)
    {
        if (count <= 2)
            return HeroColumn;
        // The pane's own width, not the window's: the sidebar takes a third of the screen. It is last frame's
        // measurement, which is stable — the pane does not resize between rounds.
        var pane = _combatRoot is { } root && root.Size.X > 100 ? root.Size.X : GetViewportRect().Size.X * 0.72f;
        var room = pane - 48 - (HeroColumn + 32) - ColumnGap;   // margins, the hero's column, the gap after it
        var each = (room - ((count - 1) * CrowdGap)) / count - 32;   // 32 = the panel's own border and padding
        return (int)Math.Clamp(each, NarrowestColumn, HeroColumn);
    }

    // A combatant's column: name, a stick-figure placeholder, an HP bar, energy (hero) or intent (enemy),
    // and its status chips. When a card is armed, an enemy box becomes a clickable target.
    private Control CombatantBox(InteractiveCombat combat, CombatantState combatant, bool isHero, int width)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        box.AddThemeConstantOverride("separation", 4);

        // The name WRAPS. Without that it is the widest thing in the column and its full length becomes the
        // column's floor, which quietly defeats every attempt to make a crowd fit: "Lower Appellate Step" is
        // 190 px of minimum width that no share-out can argue with.
        // A body's NAME can itself be a rule — Act V's gods are named for the thing they do, and "Nisaba,
        // Keeper of the First Tablet" says "the First Tablet" to a player who has never seen one. So the name
        // carries the glossary hover for whatever it names, and nothing at all when it names nothing.
        var named = Name(combatant, combat);
        var name = new Label
        {
            Text = named,
            TooltipText = Glossary.Explain(null, named),
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(width, 0),
        };
        name.AddThemeFontSizeOverride("font_size", 16);
        box.AddChild(name);

        var figure = new StickFigure(isHero ? MoonvineTheme.Accent : MoonvineTheme.Danger,
            facing: isHero ? 1 : -1, dead: !combatant.IsAlive)
        { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        box.AddChild(figure);

        box.AddChild(HealthBar(combatant, width - 30));

        // The phase goes directly above what the body is about to do, because that is the line it corrects.
        if (PhaseBanner(combat, combatant) is { } phase)
            box.AddChild(phase);

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
                // What it is about to do names things; hovering says what they are.
                MouseFilter = MouseFilterEnum.Stop,
                TooltipText = Glossary.Explain(null, intent.Label),
            };
            intentLabel.AddThemeColorOverride("font_color", MoonvineTheme.IntentColor(intent.Kind));
            box.AddChild(intentLabel);
        }

        if (StatusChips(combat, combatant) is { } chips)
            box.AddChild(Bounded(chips, combatant, width));

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

    private static Control HealthBar(CombatantState combatant, int width)
    {
        var holder = new Control { CustomMinimumSize = new Vector2(width, 22) };
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

        // The hover sits on the CARD, not only on the click overlay: wherever the pointer lands on it — the
        // name, the cost, the clipped rules text — the same explanation comes up.
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(CardVisuals.CardW, CardVisuals.CardH),
            TooltipText = Glossary.Explain(presentation?.FlavorText),
        };
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
            TooltipText = Glossary.Explain(presentation?.FlavorText),
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
                label.TooltipText = Glossary.Explain(GameHost.Instance.Blueprint.Presentation.Relics
                    .GetValueOrDefault(relic.Id.Value)?.FlavorText);
                _sidebar.AddChild(label);
            }
        }
        if (run.Consumables.Count > 0)
        {
            _sidebar.AddChild(new Label { Text = "Consumables" });
            foreach (var consumable in run.Consumables)
            {
                var label = MutedLabel($"• {ConsumableName(consumable.DefinitionId.Value)}");
                label.MouseFilter = MouseFilterEnum.Stop;
                label.TooltipText = Glossary.Explain(GameHost.Instance.Blueprint.Presentation.Consumables
                    .GetValueOrDefault(consumable.DefinitionId.Value)?.FlavorText);
                _sidebar.AddChild(label);
            }
        }

        _sidebar.AddChild(new Label { Text = $"Deck ({run.Deck.Count})" });
        foreach (var group in run.Deck
            .GroupBy(card => (Name: CardName(card.DefinitionId.value) + new string('+', card.UpgradeLevel),
                Definition: card.DefinitionId.value))
            .OrderBy(g => g.Key.Name, StringComparer.Ordinal))
        {
            var label = MutedLabel(group.Count() > 1 ? $"{group.Key.Name} ×{group.Count()}" : group.Key.Name);
            label.MouseFilter = MouseFilterEnum.Stop;
            label.TooltipText = Glossary.Explain(GameHost.Instance.Blueprint.Presentation.Cards
                .GetValueOrDefault(group.Key.Definition)?.FlavorText);
            _sidebar.AddChild(label);
        }
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

    // Prose has no hover of its own, so the words IT uses are explained under it — the same lines a tooltip
    // would have shown, for the one place in the game that is read rather than pointed at.
    private void Explained(string prose)
    {
        var terms = Glossary.In(prose, limit: 3);
        if (terms.Count == 0)
            return;
        foreach (var term in terms)
        {
            var label = MutedLabel(term);
            label.AddThemeFontSizeOverride("font_size", 12);
            _main.AddChild(label);
        }
    }

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

    // A long chip list gets its own scroll instead of making the whole column taller.
    //
    // By an Act-III boss the player can be wearing two dozen statuses, and a column as tall as its chip list
    // pushes everything below the arena off the screen — or, once the arena itself scrolls, pushes the ENEMY's
    // health bar and intent below the fold at round one, which is the same fault wearing a different hat. The
    // chips are the part that grows without limit, so the chips are the part that is bounded; a body carrying
    // an ordinary handful is untouched.
    private static Control Bounded(Control chips, CombatantState combatant, int width)
    {
        const int TallEnoughToBound = 8;
        // The same set the chips are drawn from: the phase is not one of them (it is a banner now), so a
        // body wearing one must not be counted as one chip taller than it looks.
        if (combatant.Statuses.Count(s => s.Visibility == StatusVisibility.Visible && !IsPhase(s))
            <= TallEnoughToBound)
            return chips;

        var view = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(width, 150),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        chips.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        view.AddChild(chips);
        return view;
    }

    // ACT V'S ONE SHARED RULE, and it is a UI rule (boss master §Act V §4): each god owns a prominent area
    // that always sits in the same place and says what reality currently means in its fight. Its words come
    // from the fight's own presentation (ActFive: `divineRuleTitle` + `divineRule`), so a frontend needs no
    // table of gods and an act that has none — every fight in Acts I–IV — simply shows no panel.
    private static Control? DivineRuleArea()
    {
        if (Session is not { } session || session.Run.CurrentNodeId is not { } id)
            return null;
        var node = session.Run.Map.Nodes.FirstOrDefault(n => n.Id.Value == id.Value);
        if (node?.Payload is not EncounterRef fight)
            return null;
        var extra = GameHost.Instance.Blueprint.Presentation.Encounters
            .GetValueOrDefault(fight.Id.Value)?.Extra;
        if (extra is null || extra.GetValueOrDefault("divineRule") is not { Length: > 0 } rule)
            return null;

        var title = extra.GetValueOrDefault("divineRuleTitle") ?? "The divine rule";

        // The panel carries the rule as its HOVER as well as in its body. A Label lets the pointer through,
        // so the heading is hovered as whatever is beneath it — this panel — and a player who reaches for the
        // title of the area gets the same sentence rather than nothing at all.
        var panel = new PanelContainer { TooltipText = $"{title} — {rule}" };
        panel.AddThemeStyleboxOverride("panel",
            MoonvineTheme.Panel(MoonvineTheme.BgPanelStrong, MoonvineTheme.AccentLight, 8));
        var pad = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            pad.AddThemeConstantOverride(side, 10);
        panel.AddChild(pad);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        pad.AddChild(column);

        var heading = new Label
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        heading.AddThemeFontSizeOverride("font_size", 16);
        heading.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
        column.AddChild(heading);

        var body = new Label
        {
            Text = rule,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(body);
        return panel;
    }

    // WHICH BOSS THIS IS NOW, over the top of what it is about to do.
    //
    // A phased boss rotates ONE intent list, so a slot keeps its Phase-I name for the whole fight: the Warden
    // still telegraphs "Inspect the Claim" while that slot means the Phase-II thing now. Read against a chip
    // filed after the stacks and the countdowns, that looks like a wrong label; read against a banner sitting
    // on the intent, it looks like the boss changing, which is what it is.
    //
    // WHICH statuses are phases is the document's word, not this frontend's guess: the presentation manifest
    // tags them. A game that tags none loses nothing — every status simply stays a chip, as before.
    private static Control? PhaseBanner(InteractiveCombat combat, CombatantState combatant)
    {
        var registry = combat.State.DefinitionRegistry;
        var phases = combatant.Statuses
            .Where(status => status.Visibility == StatusVisibility.Visible && IsPhase(status))
            .ToList();
        if (phases.Count == 0)
            return null;

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        foreach (var status in phases)
        {
            StatusDefinition? definition = null;
            registry?.TryGetStatus(status.DefinitionId, out definition);
            var label = new Label
            {
                Text = $"▸ {StatusText(status, definition)}",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = Control.MouseFilterEnum.Pass, // the targeting overlay keeps the click
                TooltipText = Glossary.Explain(StatusTooltip(status, definition), definition?.DescriptionKey),
            };
            label.AddThemeFontSizeOverride("font_size", 15);
            label.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
            column.AddChild(label);
        }
        return column;
    }

    // The document's word on whether a status is a phase. Unknown ids are not phases — a status with no
    // presentation entry is an ordinary chip, which is what every status was before this existed.
    private static bool IsPhase(StatusInstance status) =>
        GameHost.Instance.Blueprint.Presentation.Statuses
            .GetValueOrDefault(status.DefinitionId.value)?.Tags.Contains("phase") == true;

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
        // …minus the phase, which is not one fact among the others: it says what all of them are FOR, and it
        // is drawn above the intent instead.
        var shown = combatant.Statuses
            .Where(s => s.Visibility == StatusVisibility.Visible && !IsPhase(s))
            .ToList();
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
                TooltipText = Glossary.Explain(StatusTooltip(status, definition), definition?.DescriptionKey),
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
    // A consumable's authored name, falling back to a readable form of its id.
    private static string ConsumableName(string definition) =>
        GameHost.Instance.Blueprint.Consumables.FirstOrDefault(c => c.Id == definition)?.DisplayName
            is { Length: > 0 } name
            ? name
            : Humanized(definition);

    private static string Humanized(string id)
    {
        var text = id.Replace("standard.", "", StringComparison.Ordinal)
            .Replace("event.", "", StringComparison.Ordinal)
            .Replace('.', ' ').Replace('-', ' ').Replace('_', ' ');
        return text.Length == 0 ? id : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
