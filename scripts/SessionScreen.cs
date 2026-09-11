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
    private const int SidebarWidth = 320;
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
    // The hand's faces as they were last laid out, so `--smoke-format` can measure the real nodes.
    private readonly List<Control> _handFaces = [];
    private Control? _deckTopNode;
    private Control? _deckHolder;              // the pile itself: made once per fight, never rebuilt (its back is a video)
    private readonly List<Control> _deckStills = [];
    private Label? _deckCount;
    private static bool _fastForward;   // a smoke probe is walking: draw the screen, do not animate it
    private Control? _enemyRow;   // the live enemy row, so a probe can measure what the layout did with it
    private Control? _regionArena;  // the fixed regions, kept so a probe can measure that they stay put
    private Control? _regionHand;

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

        var side = new VBoxContainer { CustomMinimumSize = new Vector2(SidebarWidth, 0) };
        side.AddThemeConstantOverride("separation", 10);
        var sidePanel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        // ⚠ NO SIDEWAYS SCROLL IN THE SIDEBAR. A ScrollContainer that may scroll horizontally gives its child
        // the child's MINIMUM width; with it off, the child is stretched to the panel. That is the difference
        // between a relic shelf that wraps into rows and one that is a single column of squares.
        var sideScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _sidebar = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sideScroll.AddChild(_sidebar);
        sidePanel.AddChild(sideScroll);
        side.AddChild(sidePanel);
        var logPanel = new PanelContainer { CustomMinimumSize = new Vector2(SidebarWidth, 200) };
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
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-upgrade"))
            _ = SmokeRoom(MapNodeTags.Rest, "smoke-upgrade.png", andThen: "improve");
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
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-format"))
            _ = SmokeFormat();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-bug-run"))
            _ = SmokeBugInRun();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-shelf"))
            _ = SmokeShelf();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-deck"))
            _ = SmokeDeck();
        else if (OS.GetCmdlineUserArgs().Contains("--smoke-window"))
            _ = SmokeWindow();
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

    // HOW MANY PICTURES ARE ACTUALLY ON THE SCREEN — counted off the live scene, not off the code that built
    // it. D5's claim is that every place the player chooses something draws the thing rather than describing
    // it, and the only honest way to check that is to walk what was drawn and ask each texture where it came
    // from: an art slot, or the frame and the furniture. A screen that should be offering cards and reports
    // zero cards is the bug this exists to catch.
    private void ReportPictures(string screen)
    {
        // ⚠ THE PANE AND THE SIDEBAR ARE COUNTED APART. The shelf on the right wears the relics the player
        // already owns and is on screen whatever room they are in — counted together with the pane, a room that
        // draws nothing at all would still report a relic, and the number would stop meaning "this screen draws
        // what it is offering", which is the only thing it is for.
        GD.Print($"smoke-pictures [{screen}]: pane {Census(_main)}{Census(_combatRoot, add: true)} · "
            + $"worn {Census(_sidebar)}");
    }

    private static string Census(Godot.Node? root, bool add = false)
    {
        var slots = new Dictionary<string, int> { ["cards"] = 0, ["relics"] = 0, ["enemies"] = 0, ["characters"] = 0 };
        var furniture = 0;
        if (root is not null)
            Walk(root);

        void Walk(Godot.Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is TextureRect { Texture: { } texture })
                {
                    var path = texture.ResourcePath;
                    var kind = slots.Keys.FirstOrDefault(k => path.StartsWith($"res://assets/art/{k}/", StringComparison.Ordinal));
                    if (kind is not null)
                        slots[kind]++;
                    else
                        furniture++; // the card frame, the deck back — drawn, but not a slot
                }
                Walk(child);
            }
        }

        var total = slots.Values.Sum() + furniture;
        if (add && total == 0)
            return ""; // the combat root is empty outside a fight and has nothing to say about it
        return (add ? " + " : "")
            + $"{slots["cards"]} card · {slots["relics"]} relic · {slots["enemies"]} body · "
            + $"{slots["characters"]} hero · {furniture} frame";
    }

    // HOW MUCH OF A COLUMN THE ARENA ACTUALLY SHOWS. D4a chose to let the arena SCROLL rather than push the
    // hand off the bottom, and "what does not fit is reachable rather than gone" has been the answer ever
    // since — but nobody had ever put a number on how much does not fit. At an Act V boss the answer turned
    // out to be "the telegraph", which is not a thing a player can be asked to go looking for. This is the
    // number to watch whenever anything in a column grows.
    private void ReportArena()
    {
        if (_regionArena is not { } band || !IsInstanceValid(band)
            || _enemyRow is not { } row || !IsInstanceValid(row))
            return;
        var tallest = row.GetChildren().OfType<Control>().Select(c => c.Size.Y).DefaultIfEmpty(0).Max();
        var shown = band.Size.Y;
        GD.Print($"  arena: viewport {shown:0} tall, tallest column {tallest:0}"
            + (tallest > shown + 1 ? $" — {tallest - shown:0} BELOW THE FOLD (scroll)" : " — all of it visible"));
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
    internal static string Where(InteractiveRunSession session)
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

        ReportArena();

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
        var wanted = BossNameArgument();

        // A KNOWN SEED SKIPS THE SEARCH. Finding a named god costs one whole five-act walk per seed tried,
        // so the search below is minutes of walking to reach a fight somebody already knows where to find.
        // `--seed <n>` is the answer written down: nanna_sin is on 1, inanna on 5.
        if (BossSeedArgument() is { } pinned)
            GameHost.Instance.StartNewRun(pinned, health: 9999);

        bool Found() => Session is { } s && s.Run.ActNumber >= act
            && Play?.CombatDriver?.Current is not null && AtABoss()
            && (wanted.Length == 0 || Enemies().Any(e =>
                e.DefinitionId.value.Contains(wanted, StringComparison.OrdinalIgnoreCase)));

        await WalkUntil(stop: Found, prefer: node => node.HasTag(MapNodeTags.Boss), budget: 9000);

        // A NAMED boss may not be in this run at all: Act V fields THREE OF SIX gods, in an order the seed
        // picks, so asking for one god and walking one run is asking for a one-in-two chance. The crowd probe
        // learnt this first — one seed is not a search — and a god is the sharper case, because the whole
        // point of aiming the probe is to look at a fight that is not the one the default seed happens to
        // hold.
        if (!Found() && wanted.Length > 0 && BossSeedArgument() is null)
            foreach (var seed in new[] { 5, 7, 1, 2, 3, 4, 6, 8, 9, 11 })
            {
                GameHost.Instance.StartNewRun(seed, health: 9999);
                await WalkUntil(stop: Found, prefer: node => node.HasTag(MapNodeTags.Boss), budget: 9000);
                if (Found())
                {
                    GD.Print($"  '{wanted}' found on seed {seed}");
                    break;
                }
            }

        // Pass the requested rounds WITHOUT attacking: the probe is unkillable, so ending the turn is the one
        // way to let the fight develop without also ending it.
        //
        // …unless the fight's state is a consequence of what the PLAYER does, which `--plays N` is for.
        // Inanna claims a card whether or not anybody moves, so watching her needed nothing; Nanna-Sin counts
        // the Nth card played in a turn, and a probe that never plays a card counts nothing and reports an
        // empty hand — which would read as "the stamp never reaches the screen" when the truth is that the
        // probe never gave it anything to stamp.
        for (var round = 0; round < BossRoundsArgument() && Play?.CombatDriver?.Current is { } waiting; round++)
        {
            if (waiting.IsHeroTurn)
            {
                for (var played = 0; played < BossPlaysArgument(); played++)
                {
                    if (Play?.CombatDriver is not { } driver || driver.Current is not { } fight
                        || !fight.IsHeroTurn)
                        break;
                    // A card that asks a question parks the whole fight until it is answered — and an
                    // unanswered question refuses every End Turn after it, so a probe that plays without
                    // answering passes one round and reports six.
                    if (driver.PendingOptionChoice is { } options)
                    {
                        driver.SupplyOptionChoice(
                            [.. Enumerable.Range(0, Math.Min(driver.PendingOptionChoiceCount, options.Count))]);
                        continue;
                    }
                    if (driver.PendingCardChoice is { } cards)
                    {
                        driver.SupplyCardChoice([.. cards.Take(driver.PendingCardChoiceCount).Select(c => c.Id)]);
                        continue;
                    }
                    var hand = fight.State.GetCombatant(fight.HeroId);
                    var playable = fight.Hand.FirstOrDefault(c =>
                        !c.DefinitionId.value.Contains("red_tape")
                        && !c.DefinitionId.value.Contains("unsigned_form")
                        && CanPay(hand, c.DefinitionId.value));
                    if (playable is null)
                        break;
                    driver.PlayCard(playable.Id, fight.State.Combatants
                        .FirstOrDefault(c => c.Id != fight.HeroId && c.IsAlive
                            && c.TeamId == StandardCombatIds.EnemyTeam)?.Id);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }

                if (Play?.CombatDriver is { Current: { IsHeroTurn: true } }
                    && Play.CombatDriver.PendingOptionChoice is null
                    && Play.CombatDriver.PendingCardChoice is null)
                    Play.CombatDriver.EndTurn();
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        Rebuild();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var combat = Play?.CombatDriver?.Current;
        GD.Print($"smoke-boss {act}: act={Session?.Run.ActNumber} boss={(AtABoss() ? "yes" : "NO")} "
            + $"round={combat?.Round} ended={_walkEnded} error={Session?.Error ?? Play?.Error ?? "none"}");
        GD.Print($"  facing: {Facing()}");
        ReportArena();   // an Act V arena is the shortest in the game — its rule band costs it 104 points
        // The Divine Rule Area, read back out of the tree it was built into: a headless probe cannot take a
        // screenshot, so this is the only way to say that the one UI surface Act V's design REQUIRES is
        // actually on the screen and not merely a method that returned without throwing.
        GD.Print($"  divine rule area: {DivineRuleOnScreen() ?? "none"}");
        // …and the same question for the stamp a fight puts on the CARDS. Inanna's whole decision is "which
        // of these is hers", and a mark the engine can see and the player cannot is not a decision.
        GD.Print($"  card stamps: {StampsOnScreen()}");
        // …and how far into a boss's future the screen is actually showing. Nanshe's tablet promises three
        // days at once, and until this step the screen drew one whatever the player had been granted.
        GD.Print($"  forecast: {ForecastOnScreen()}");
        if (combat is not null)
            foreach (var body in combat.State.Combatants)
                GD.Print($"  [{Name(body, combat)}] {StatusLine(combat, body)}");

        ReportTooltips($"boss{act}");
        await CaptureThenQuit($"smoke-boss{act}.png");
    }

    // Which stamps are on the hand as DRAWN — the labels themselves, not the marks the state carries. A card
    // the engine has marked and the screen has not is exactly the failure this reports.
    private string StampsOnScreen()
    {
        var labels = new List<string>();
        void Collect(Godot.Node node)
        {
            if (node is Label label && CardMarks.Values.Any(m => m.Label == label.Text))
                labels.Add(label.Text);
            foreach (var child in node.GetChildren())
                Collect(child);
        }
        if (_combatRoot is not null)
            Collect(_combatRoot);

        var marked = Play?.CombatDriver?.Current is { } fight
            ? fight.Hand.Count(c => c.Marks.Any(m => CardMarks.ContainsKey(m.value)))
            : 0;
        return labels.Count == 0 && marked == 0
            ? "none in hand"
            : $"{labels.Count} shown of {marked} marked — {string.Join(", ", labels.Distinct())}";
    }

    // How many of an enemy's coming actions are ON THE SCREEN, against how many the engine is willing to
    // project. The two numbers disagreeing is the whole failure: a sight the player was granted and the
    // screen never drew.
    private string ForecastOnScreen()
    {
        if (Play?.CombatDriver?.Current is not { } fight)
            return "no fight";

        var enemy = fight.State.Combatants.FirstOrDefault(c => c.Id != fight.HeroId && c.IsAlive);
        if (enemy is null)
            return "no body";

        var projected = fight.UpcomingIntentsFor(enemy.Id);
        var labels = new List<string>();
        void Collect(Godot.Node node)
        {
            if (node is Label label && projected.Any(i => label.Text.EndsWith(i.Label, StringComparison.Ordinal)))
                labels.Add(label.Text.Replace("\n", " / "));
            foreach (var child in node.GetChildren())
                Collect(child);
        }
        if (_combatRoot is not null)
            Collect(_combatRoot);

        return $"{labels.Count} shown of {projected.Count} projected — {string.Join(" · ", labels)}";
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

    // WHICH boss, when the act has several. Act V is three of six gods in an order the seed picks, so
    // "--smoke-boss 5" always lands on whichever came first and the other two are unreachable — which is no
    // use to a probe that exists to look at ONE fight's screen. `--boss <fragment>` walks past the gods it
    // does not want (the probe is unkillable, so walking past means winning) until it is standing in front
    // of the one it does. Empty = the first boss of the act, as before.
    private static string BossNameArgument()
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, "--boss");
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : "";
    }

    // HOW LONG TO STAND THERE before the screen is read. A boss's opening round shows none of what makes it a
    // boss: Inanna has claimed nothing yet, Nisaba's tablet is at its first counts, and a probe reporting
    // round 1 is reporting the least interesting screen the fight has. `--rounds N` passes N turns first.
    private static int BossRoundsArgument()
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, "--rounds");
        return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var rounds) ? rounds : 0;
    }

    // WHICH SEED to walk, when the caller already knows where the fight is. Without it a named boss costs a
    // full game walk per seed until one holds them; with it, one walk.
    private static int? BossSeedArgument()
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, "--seed");
        return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var seed) ? seed : null;
    }

    // HOW MANY CARDS TO PLAY on each of those rounds, default none. See the walk above: some fights only
    // put anything on the screen once the player has done something.
    private static int BossPlaysArgument()
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, "--plays");
        return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var plays) ? plays : 0;
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
    //
    // `andThen` takes ONE more step once the room is reached: the branch whose words contain it. The campfire's
    // amendment is behind such a branch, and what is behind it is the DECK pick — a different kind of candidate
    // from a reward's (the cards you already own, upgrade levels and all), on the same screen. Nothing else in
    // the battery ever reaches it.
    private async System.Threading.Tasks.Task SmokeRoom(string role, string file, string? andThen = null)
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
        if (andThen is not null && session is { IsAwaitingChoice: true })
        {
            var branch = session.PendingChoices
                .FirstOrDefault(c => Say(c.TextKey ?? c.Id).Contains(andThen, StringComparison.OrdinalIgnoreCase));
            if (branch is not null)
                session.Pick(branch.Id);
            else
                GD.Print($"smoke-room {role}: no branch named \"{andThen}\" — "
                    + string.Join(" | ", session.PendingChoices.Select(c => Say(c.TextKey ?? c.Id))));
        }

        Rebuild();
        GD.Print($"smoke-room {role}: choice={session?.IsAwaitingChoice} entities={session?.IsAwaitingEntities} "
            + $"error={session?.Error ?? Play?.Error ?? "none"}");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ReportPictures(role);
        ReportTooltips(role);
        await CaptureThenQuit(file);
    }

    private async System.Threading.Tasks.Task MapShot()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ReportTooltips("map");
        await HoverTheBoss();
        await CaptureThenQuit("smoke-map.png");
    }

    // THE SAME GAME AT THREE WINDOW SIZES. The whole point of a design canvas is that nothing in the frontend
    // knows how big the window is: the regions, the cards and the pile are laid out in canvas units and the
    // engine scales the page. This resizes the real window under a real fight and reports the pane in CANVAS
    // units — at 16:9 every number must be identical whatever the window is, and at another aspect the pane
    // may only get WIDER (that is what `expand` buys). A frontend that measured pixels would drift here.
    private async System.Threading.Tasks.Task SmokeWindow()
    {
        var session = Session;
        var play = Play;
        for (var step = 0; step < 60 && session is not null && play is not null; step++)
        {
            if (play.CombatDriver?.Current is not null) break;
            if (session.IsAwaitingNodeChoice) session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude) session.Continue();
            else if (session.IsAwaitingEntities) session.PickEntities([0]);
            else if (session.IsAwaitingChoice) session.Pick(session.PendingChoices[^1].Id);
            else break;
        }
        if (Play?.CombatDriver?.Current is null || DisplayServer.GetName().Contains("headless"))
        {
            GD.Print("smoke-window: needs a window and a fight");
            GetTree().Quit();
            return;
        }

        foreach (var (w, h) in new[] { (1280, 720), (1920, 1080), (1600, 1000) })
        {
            DisplayServer.WindowSetSize(new Vector2I(w, h));
            for (var i = 0; i < 4; i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Rebuild();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            var canvas = GetViewportRect().Size;
            var arena = _regionArena?.GetGlobalRect() ?? new Rect2();
            var hand = _regionHand?.GetGlobalRect() ?? new Rect2();
            var deck = _deckHolder?.GetGlobalRect() ?? new Rect2();
            // The CARDS against the pile, not the band against the pile: the band is the whole width of the
            // pane and is meant to lie over the corner; what may not touch the pile is the leftmost card.
            var first = _handFaces.Count > 0 ? _handFaces[0].GetGlobalRect() : new Rect2();
            var overlap = _handFaces.Count > 0 && deck.Intersects(first) ? "OVERLAP" : "clear";
            GD.Print($"smoke-window: window={w}x{h} canvas={canvas.X:0}x{canvas.Y:0} "
                + $"arena={arena.Size.X:0}x{arena.Size.Y:0}@{arena.Position.Y:0} "
                + $"hand={hand.Size.X:0}x{hand.Size.Y:0} cards@{first.Position.X:0} deck→{deck.End.X:0} {overlap}");
            GetViewport().GetTexture().GetImage().SavePng($"user://smoke-window-{w}x{h}.png");
        }
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        GetTree().Quit();
    }

    // Put the pointer somewhere the way the window manager would: a real motion event through the GUI, not a
    // warp. ⚠ `Input.WarpMouse` moves the cursor but delivers no motion to an unfocused window, so a probe
    // that warps and then asks who is hovered always hears "nobody" — which says nothing about the control.
    private async System.Threading.Tasks.Task PointAt(Vector2 at)
    {
        Input.WarpMouse(at);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
    }

    // Park the pointer on the boss room and hold it there long enough for the tooltip to open, then say what
    // it says. ⚠ THE BOSS ROOM IS A DISABLED BUTTON — it is not on the fork, so it cannot be clicked, and
    // "the tooltip text was set" is not the same claim as "a player hovering it reads the name". So
    // `smoke-map-boss.png` is taken with the pointer still on the room: the popup is IN the picture or it is
    // not, and the line printed below says which control the GUI thinks is under the mouse.
    private async System.Threading.Tasks.Task HoverTheBoss()
    {
        if (DisplayServer.GetName().Contains("headless"))
            return;
        Button? boss = null;
        void Walk(Godot.Node n)
        {
            if (n is Button b && b.TooltipText.Contains("ends the act", StringComparison.Ordinal))
                boss ??= b;
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(this);
        if (boss is null)
        {
            GD.Print("smoke-map: no boss room on this map");
            return;
        }
        // The boss room is at the FOOT of an act that is taller than the window, so it has to be scrolled to
        // before a pointer can be put on it.
        _mainScroll.ScrollVertical = Math.Max(0, (int)(boss.GlobalPosition.Y - _mainScroll.GlobalPosition.Y
            + _mainScroll.ScrollVertical - _mainScroll.Size.Y / 2));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await PointAt(boss.GetGlobalRect().GetCenter());
        var onBoss = GetViewport().GuiGetHoveredControl() == boss;
        GetViewport().GetTexture().GetImage().SavePng("user://smoke-map-boss.png");

        // The control test: an ENABLED room, hovered the same way. If this one registers and the boss does
        // not, the pointer is fine and being disabled is what costs the room its tooltip.
        Button? open = null;
        void Reachable(Godot.Node n)
        {
            if (n is Button b && !b.Disabled && b.TooltipText.Length > 0) open ??= b;
            foreach (var c in n.GetChildren()) Reachable(c);
        }
        Reachable(this);
        var onOpen = false;
        if (open is not null)
        {
            _mainScroll.ScrollVertical = 0;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await PointAt(open.GetGlobalRect().GetCenter());
            onOpen = GetViewport().GuiGetHoveredControl() == open;
        }
        GD.Print($"smoke-map: boss hover = \"{boss.TooltipText}\" disabled={boss.Disabled} "
            + $"hovered={(onBoss ? "yes" : "no")} · control(enabled room) hovered={(onOpen ? "yes" : "no")}");
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
        ReportPictures("reward");
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

    // Esc opens the settings, anywhere in a run — the window is most often found wanting during a fight, not
    // on a menu. The overlay is a child of the SCREEN, not of anything Rebuild() clears, so a state change
    // underneath it (an enemy acting while it is open) redraws the game behind the dialog and leaves it alone.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is null || !@event.IsActionPressed("ui_cancel"))
            return;
        // Esc closes the topmost thing first. The report window is opened FROM the settings window, so it is
        // the one on top; without this, Esc out of a half-typed report would reopen the settings behind it.
        if (GetNodeOrNull(BugReportPanel.OverlayName) is { } reporting)
        {
            reporting.QueueFree();
        }
        else if (GetNodeOrNull("SettingsOverlay") is { } open)
        {
            open.QueueFree();
        }
        else
        {
            // ⚠ THE SCREEN IS CAPTURED HERE, one line before the veil exists. A player presses Esc because
            // something on screen is wrong; one step later that something is behind a dimmed sheet and a
            // dialog, and a screenshot taken then is a picture of the menu. See BugReport.Remember.
            BugReport.Remember(GetViewport());
            var overlay = SettingsPanel.Overlay(
                () => GetNodeOrNull("SettingsOverlay")?.QueueFree(),
                OpenBugReport);
            overlay.Name = "SettingsOverlay";
            AddChild(overlay);
        }
        GetViewport().SetInputAsHandled();
    }

    // The settings window steps aside for the report window — two stacked dialogs over a fight is one too many,
    // and Esc is ambiguous with both open.
    private void OpenBugReport()
    {
        GetNodeOrNull("SettingsOverlay")?.QueueFree();
        BugReportPanel.Open(this);
    }

    private void Rebuild()
    {
        var session = Session;
        // Combat gets the graphical scene (_combatRoot); everything else the ordinary list (_mainScroll).
        var inCombat = session is not null && Play?.Error is null && session.Error is null
            && !session.IsAwaitingChoice && !session.IsAwaitingEntities && !session.IsAwaitingNodeChoice
            && !session.IsAwaitingInterlude && Play?.CombatDriver?.Current is not null;

        foreach (var child in _main.GetChildren())
            child.QueueFree();
        foreach (var child in _combatRoot.GetChildren())
        {
            // ⚠ A CLIP THAT IS REBUILT NEVER PLAYS. Every state change in a fight — a card played, an enemy
            // acting, a card drawn — runs this teardown, and the deck's back is a VideoStreamPlayer: freed and
            // made again, it starts at 0.00 every single time, so a 36-second turn of the card back never got
            // past its first tenth of a second. MEASURED (`players a13: …@1.59` then `players b0: …@0.00`, a
            // brand-new instance id): the pile visibly snapped back to the top of the loop on every click.
            // The pile is therefore built ONCE per fight and updated in place; it is freed when the fight is.
            if (inCombat && child == _deckHolder)
                continue;
            child.QueueFree();
        }
        foreach (var child in _sidebar.GetChildren())
            child.QueueFree();

        if (!inCombat && _deckHolder is { } stale)
        {
            stale.QueueFree();
            _deckHolder = null;
            _deckStills.Clear();
            _deckCount = null;
        }
        _combatRoot.Visible = inCombat;
        _mainScroll.Visible = !inCombat;
        if (!inCombat)
            _deckTopNode = null;
        _enemyRow = null;
        _regionArena = null;
        _regionHand = null;
        if (!inCombat)
            _shownHandIds.Clear(); // a fresh fight re-deals; its opening hand animates in

        if (Play is null || session is null)
        {
            Title("No run active.");
            return;
        }

        if (Play.Error is { } hostError)
            Title($"Error: {hostError}", MoonvineTheme.Harm);
        else if (session.Error is { } runError)
            Title($"Run error: {runError}", MoonvineTheme.Harm);
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
        Banner(name, RollCall(session.Run) is { Count: > 1 } gods ? string.Join("  ▸  ", gods) : null);
    }

    // A title card that fades away by itself: the whole screen dimmed, the act's name across it, and under it
    // — QUIETER — whoever the act is bringing. They used to be one label at one size, so the roll call shouted
    // as loudly as the act's own name and the card had no first thing to read. A subtitle is a subtitle.
    private void Banner(string text, string? under = null)
    {
        if (!IsInsideTree())
            return; // a headless probe can redraw on its way out of the tree; there is nobody to show it to

        // ⚠ NOT OVER A PROBE. Every boss screenshot in the battery is taken in the first seconds of an act, so
        // the card was standing over the one screen the shot was for — six reviews of a dimmed veil. A title
        // card is for a human watching the game start; a probe is not watching.
        if (_fastForward)
            return;

        var veil = new ColorRect { Color = new Color(MoonvineTheme.Bg, 0.82f), MouseFilter = MouseFilterEnum.Ignore };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 14);
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        column.Alignment = BoxContainer.AlignmentMode.Center;

        var title = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
        column.AddChild(title);

        if (under is not null)
        {
            var roll = new Label
            {
                Text = under,
                // A title card can name a rule — Act V's gods are called after the things they do — and the
                // seconds it is up are seconds a player may reach for one of those names.
                TooltipText = Glossary.Explain(null, under),
                MouseFilter = MouseFilterEnum.Stop,
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            roll.AddThemeFontSizeOverride("font_size", 20);
            roll.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
            column.AddChild(roll);
        }

        veil.AddChild(column);
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

        // AN EVENT THAT HANDS SOMETHING OVER SHOWS IT. A door that offers a card or a relic was asking the
        // player to take a sentence on trust — and an event's choices are authored with their effects right
        // there, so what a branch GIVES is knowable without the engine being asked anything new. The branch is
        // still a button (its words are the offer, and most branches give nothing a picture can hold); the
        // thing it gives stands under it.
        foreach (var choice in session.PendingChoices)
        {
            var id = choice.Id;
            var text = Say(choice.TextKey ?? id);
            var offer = RunEntityLabeler.ArtForGrant(choice.Effects);

            // ⚠ THE OFFER MUST BELONG TO ITS BRANCH. Three buttons and one picture loose beneath them is a
            // picture that belongs to whichever door the eye happens to be nearest — so the branch and what it
            // hands over are ONE block, tight, and the next branch starts a new one.
            var block = offer is null ? null : new VBoxContainer();
            block?.AddThemeConstantOverride("separation", 2);

            var button = new Button { Text = text, TooltipText = Glossary.Explain(null, text) };
            button.Pressed += () => { session.Pick(id); GameHost.Instance.AutoSave(); };
            (block ?? (Container)_main).AddChild(button);
            if (block is null)
                continue;

            switch (offer)
            {
                // Not clickable: the BUTTON is the choice. A card drawn here is being shown, not offered —
                // two ways to take the same branch is two chances to take it by accident.
                case EntityArt { Kind: EntityArt.Card } card:
                    var faces = Gallery();
                    faces.AddChild(CardPick(card.Id, card.UpgradeLevel, selected: false, caption: null, onClick: null));
                    block.AddChild(faces);
                    break;
                case EntityArt { Kind: EntityArt.Relic } relic:
                    var offered = Shelf();
                    offered.AddChild(RelicIcon(relic.Id));
                    block.AddChild(offered);
                    break;
            }
            _main.AddChild(block);
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

        // THE STOCK, AS OBJECTS ON A SHELF. A shop is the one screen in the game that is nothing but a choice,
        // and the thing it was asking the player to choose between was a stack of labelled buttons. Every slot
        // knows what it grants (its payload), so every slot can be drawn as the thing it grants: a card as a
        // card with its price under it, a relic as the tile it will wear on the shelf. Only what is NOT an
        // object — the card-removal service, the restock — stays a row, because it is a service and not a thing.
        foreach (var group in shelf.Slots.GroupBy(slot => slot.GroupId))
        {
            _main.AddChild(MutedLabel(Say(group.Key)));
            HFlowContainer? gallery = null;
            foreach (var slot in group)
            {
                var entry = slot.Entry;
                var name = Say(entry.TextKey ?? entry.Id);
                switch (RunEntityLabeler.ArtForGrant(entry.Payload))
                {
                    case EntityArt { Kind: EntityArt.Card } card:
                        gallery ??= AddGallery();
                        gallery.AddChild(ShopCard(session, affordable, entry.Id, card.Id, slot.Price));
                        break;
                    case EntityArt { Kind: EntityArt.Relic } relic:
                        AddShopRow(session, affordable, entry.Id, name, slot.Price,
                            WhatItDoes(entry.Payload), RelicIcon(relic.Id));
                        break;
                    default:
                        AddShopRow(session, affordable, entry.Id, name, slot.Price, WhatItDoes(entry.Payload));
                        break;
                }
            }
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

    // A CARD ON THE SHELF. The same face the hand will hold, with the price under it, dimmed as one object when
    // the purse cannot reach it — which is what a shop is half for: knowing what you cannot yet afford. Dimming
    // also takes the click away (the face disables its own overlay), so an unaffordable card cannot be bought
    // by a player who did not read the number.
    private Control ShopCard(
        InteractiveRunSession session, IReadOnlyDictionary<string, EventChoice> affordable,
        string choiceId, string definitionId, int price)
    {
        var canBuy = affordable.ContainsKey(choiceId);
        return CardPick(
            definitionId, upgradeLevel: 0, selected: false,
            caption: canBuy ? $"{price} gold" : $"{price} gold — too dear",
            onClick: canBuy ? () => session.Pick(choiceId) : null,
            dimmed: !canBuy);
    }

    private void AddShopRow(
        InteractiveRunSession session, IReadOnlyDictionary<string, EventChoice> affordable,
        string choiceId, string name, int price, string description = "", Control? icon = null)
    {
        var canBuy = affordable.ContainsKey(choiceId);
        var hover = Glossary.Explain(canBuy ? null : "Not enough gold.", $"{name} {description}");
        // On the whole row, so the rules line under the button explains its own words too.
        var column = new VBoxContainer { TooltipText = hover, MouseFilter = MouseFilterEnum.Pass };
        column.AddThemeConstantOverride("separation", 0);
        var button = new Button { Text = $"{name}   —   {price} gold", Disabled = !canBuy };
        button.Pressed += () => session.Pick(choiceId);
        button.TooltipText = hover;
        if (!canBuy)
            button.AddThemeColorOverride("font_disabled_color", MoonvineTheme.TextMuted);
        column.AddChild(button);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var text = MutedLabel(description);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.AddThemeFontSizeOverride("font_size", 12);
            text.MouseFilter = MouseFilterEnum.Stop;
            text.TooltipText = hover;
            column.AddChild(text);
        }

        // With a picture the row becomes tile-then-words; without one it is the column it always was.
        if (icon is null)
        {
            _main.AddChild(column);
        }
        else
        {
            var withIcon = new HBoxContainer { TooltipText = hover, MouseFilter = MouseFilterEnum.Pass };
            withIcon.AddThemeConstantOverride("separation", 8);
            if (!canBuy)
                icon.Modulate = new Color(1, 1, 1, 0.45f); // out of reach, and the object says so as one object
            withIcon.AddChild(icon);
            column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            withIcon.AddChild(column);
            _main.AddChild(withIcon);
        }
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

    // THE REWARD SCREEN. Everything a fight, a chest or an event hands over comes through here — a card pick, a
    // relic, a deck card to be struck out — and until D5 all of it was a column of sentences. The pick now says
    // what each option is a picture OF (EntityArt, which exists for exactly this), so a card is drawn as a card
    // and a relic wears its shelf tile. An option that is a picture of nothing — gold, a door to a further
    // reward the run has not rolled yet — keeps the words, because there is nothing to draw.
    private void RenderEntityPick(InteractiveRunSession session, EntitySelectionRequest entities)
    {
        Title(Say(entities.Purpose));
        Muted(entities.Displays.Count <= entities.Count ? "Yours:" : $"Pick {entities.Count}");
        HFlowContainer? gallery = null;
        for (var i = 0; i < entities.Displays.Count; i++)
        {
            var index = i;
            var description = index < entities.Descriptions.Count ? entities.Descriptions[index] : "";
            var selected = _selectedEntities.Contains(index);
            void Toggle()
            {
                if (!_selectedEntities.Remove(index))
                {
                    if (entities.Count == 1)
                        _selectedEntities.Clear();
                    if (_selectedEntities.Count < entities.Count)
                        _selectedEntities.Add(index);
                }
                Rebuild();
            }

            switch (entities.ArtAt(index))
            {
                // Cards go side by side in one wrapping row: a card pick is a COMPARISON, and three faces in a
                // column cannot be compared without scrolling past the one you were looking at.
                case EntityArt { Kind: EntityArt.Card } card:
                    gallery ??= AddGallery();
                    gallery.AddChild(CardPick(card.Id, card.UpgradeLevel, selected, caption: null, Toggle));
                    break;
                // A relic keeps its row — its rules are prose, and prose does not fit in a square — but the row
                // now begins with the object itself.
                case EntityArt { Kind: EntityArt.Relic } relic:
                    _main.AddChild(EntityOption(
                        entities.Displays[index], description, selected, Toggle, RelicIcon(relic.Id)));
                    break;
                default:
                    _main.AddChild(EntityOption(entities.Displays[index], description, selected, Toggle));
                    break;
            }
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

    // ── D5: A CHOICE YOU CAN SEE ─────────────────────────────────────────────────
    // Wherever the player picks something, the thing is DRAWN and not merely described. The hand has shown
    // real cards since D4a and the shelf real relics since D3; everything else the player chooses from — the
    // reward after a fight, the shop's stock, an event that hands something over — was still a list of
    // sentences, and a game whose cards have faces cannot ask you to choose between two paragraphs.
    //
    // Three widgets serve all of it, because all of it is the same two things: a card, or a relic.

    // A ROW OF PICTURES THAT WRAPS. Three cards after a fight, seven objects on a shelf: an HFlowContainer is
    // the one container that fills a row and then starts another, so a wide shelf grows downwards rather than
    // off the side of the pane. Centred, because a reward is the thing you are looking at.
    //
    // ⚠ NOT inside a CenterContainer — that hands its child the child's MINIMUM, and a flow container's
    // minimum width is one card, which would stack the whole shelf into a single column. The same trap the
    // sidebar's shelf documents; only the container's own alignment can centre a row that wraps.
    private static HFlowContainer Gallery()
    {
        var row = new HFlowContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = FlowContainer.AlignmentMode.Center,
        };
        row.AddThemeConstantOverride("h_separation", 10);
        row.AddThemeConstantOverride("v_separation", 10);
        return row;
    }

    private HFlowContainer AddGallery()
    {
        var row = Gallery();
        _main.AddChild(row);
        return row;
    }

    // ONE CARD, OUTSIDE A FIGHT. CardBlockButton cannot serve here: it asks the combat what the hero can pay
    // for and what a rule forbids, and a reward screen has no combat — what a card you are being GIVEN costs
    // is not a question anyone is asking. Everything else is the very same face the hand draws, which is the
    // whole point: the card you are offered has to be recognisable as the card you will later hold.
    //
    // `caption` is a SHORT line under the card (a price). It is short on purpose — a column is as wide as its
    // widest child, so a sentence here would push the cards apart; what a card does belongs in its plaque and
    // its hover, where the hand already puts it.
    private Control CardPick(
        string definitionId, int upgradeLevel, bool selected, string? caption, Action? onClick, bool dimmed = false)
    {
        var presentation = GameHost.Instance.Blueprint.Presentation.Cards.GetValueOrDefault(definitionId);
        var rules = presentation?.FlavorText ?? "";
        var tooltip = string.Join("\n", new[] { CostLabel(definitionId), Glossary.Explain(rules) }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var face = CardVisuals.Face(
            new CardVisuals.CardFace(
                Id: definitionId,
                // An improvement prints its "+" on the NAME and draws the same picture — no art slot in this
                // game has a "+" in it, and a card that was upgraded is still a picture of that card.
                Title: CardName(definitionId) + new string('+', upgradeLevel),
                Cost: CostBadge(definitionId),
                Rules: rules,
                Rarity: presentation?.Rarity,
                Tooltip: tooltip,
                Dimmed: dimmed,
                Armed: selected),
            onClick);
        if (string.IsNullOrWhiteSpace(caption))
            return face;

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        column.AddChild(face);
        var label = MutedLabel(caption);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.CustomMinimumSize = new Vector2(CardVisuals.CardW, 0);
        label.AddThemeFontSizeOverride("font_size", 12);
        label.MouseFilter = MouseFilterEnum.Stop;
        label.TooltipText = tooltip;
        if (dimmed)
            label.AddThemeColorOverride("font_color", new Color(MoonvineTheme.TextMuted, 0.6f));
        column.AddChild(label);
        return column;
    }

    // A relic, on the same tile the shelf wears it on — its pool frame included, so what RANK of relic this is
    // gets answered before its name is read. Bigger than the shelf's 50 px: on a reward or a shop shelf the
    // relic is the thing being looked at, not a thing being glanced past.
    private const int RelicPickSize = 64;

    // A consumable is worn on the same tile and spent rather than kept — and on the way-screen it is a THING
    // the player chooses to use, which is the whole of D5's rule. It was offered there as its raw id.
    private Control ConsumableIcon(string definitionId, float size = RelicPickSize)
    {
        var look = GameHost.Instance.Blueprint.Presentation.Consumables.GetValueOrDefault(definitionId);
        var name = ConsumableName(definitionId);
        return CardVisuals.Tile(new CardVisuals.RelicFace(
            Id: definitionId,
            Title: name,
            Pool: look?.Frame,
            Tooltip: $"{name}\n{Glossary.Explain(look?.FlavorText)}",
            Off: false), size);
    }

    private Control RelicIcon(string relicId, float size = RelicPickSize)
    {
        var look = GameHost.Instance.Blueprint.Presentation.Relics.GetValueOrDefault(relicId);
        var name = Play?.RelicNames.GetValueOrDefault(relicId) ?? Humanized(relicId);
        return CardVisuals.Tile(new CardVisuals.RelicFace(
            Id: relicId,
            Title: name,
            Pool: look?.Frame,
            Tooltip: $"{name}\n{Glossary.Explain(look?.FlavorText)}",
            Off: false), size);
    }

    // A pickable option showing the name on top and its ability/rules text beneath — so a card reward
    // pick shows WHAT each card does. The whole panel is clickable via a transparent overlay button.
    //
    // `icon` puts a picture at the head of the row: a relic is an OBJECT and reads as one, but unlike a card it
    // carries its rules in prose too long for a 64 px square, so it gets the picture AND the words rather than
    // one instead of the other. An option that is a picture of nothing passes none and is the row it always was.
    private static Control EntityOption(
        string name, string description, bool selected, Action onPressed, Control? icon = null)
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 0),
            TooltipText = Glossary.Explain(null, $"{name} {description}"),
        };
        panel.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(
            selected ? MoonvineTheme.BgControl : MoonvineTheme.BgPanel,
            selected ? MoonvineTheme.AccentLight : new Color(MoonvineTheme.Accent, 0.3f)));

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
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
        if (icon is null)
        {
            panel.AddChild(column);
        }
        else
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            // The row's own click overlay lies OVER the tile, so the tile's hover never fires here — the panel's
            // tooltip is what the player gets, and it already carries the name and the rules the row prints.
            row.AddChild(icon);
            row.AddChild(column);
            panel.AddChild(row);
        }

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
        // What the between-rooms screen used to offer, now that there is no between-rooms screen — and shown as
        // the object it is, on the same tile the sidebar wears it on. "Use standard.scheduled_the_collapse" was
        // the last raw id left anywhere the player is asked to choose something.
        foreach (var consumable in session.Run.Consumables.Where(c => c.UseEffects.Count > 0))
        {
            var id = consumable.Id;
            var definition = consumable.DefinitionId.Value;
            _main.AddChild(EntityOption(
                $"Use {ConsumableName(definition)}",
                GameHost.Instance.Blueprint.Presentation.Consumables.GetValueOrDefault(definition)?.FlavorText ?? "",
                selected: false,
                () => session.UseConsumable(id),
                ConsumableIcon(definition)));
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
            var definition = consumable.DefinitionId.Value;
            _main.AddChild(EntityOption(
                $"Use {ConsumableName(definition)}",
                GameHost.Instance.Blueprint.Presentation.Consumables.GetValueOrDefault(definition)?.FlavorText ?? "",
                selected: false,
                () => session.UseConsumable(id),
                ConsumableIcon(definition)));
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
            victory ? MoonvineTheme.Accent : MoonvineTheme.Harm);
        AddButton("Back to title", () => GetTree().ChangeSceneToFile("res://scenes/Boot.tscn"));
    }

    // ── combat (graphical: hero left, enemies right, hand bottom-center) ──────────

    // THE COMBAT PANE IS FIXED REGIONS, NOT A STACK OF BOXES. Every part of a fight has a place of its own —
    // the heading at the top, the divine rule under it when a god is running the room, the arena between, the
    // hand along the bottom and the draw pile in the corner beside it — and each of them keeps that place
    // whatever the fight contains. It used to be one VBoxContainer, which hands out its children's MINIMUM
    // heights first and the leftovers afterwards: a boss fight with a rule panel and a dozen statuses ate the
    // arena's share, and the hero's own health bar was cut in half at the bottom of its column (measured on
    // `smoke-boss5.png`, Act V). Regions cannot do that to each other: what overflows one scrolls INSIDE it.
    //
    // The numbers are design units on a 1280 × 720 canvas. The window is not 1280 × 720 — the stretch mode
    // scales this whole canvas to whatever the player set (see DisplaySettings) — so these are the only
    // coordinates in the file that need to be true, and they are true at every resolution.
    private const int PaneInset = 20;
    private const int HeadlineBand = 30;   // "Round N"
    // Act V's rule area — the same spot in every one of its fights.
    //
    // ⚠ IT WAS 104 AND THAT PUT THE PRIORITY BACKWARDS. A god's decree is read ONCE, on entering the room; the
    // telegraph under it is read every single turn — and at 104 the band left the arena 222 points, which is
    // not enough for a column to show what the god is about to do. The design requires the rule to sit in the
    // same PLACE in every Act V fight, which it still does; it never required it to be tall enough for its
    // longest decree, and it has had a scroll of its own and a hover since the day it was built.
    private const int DivineBand = 80;
    private const int HintBand = 22;       // "click an enemy to play it"
    private const int HandBand = 214;      // a card plus the fan's lean
    private const int ControlBand = 44;    // End turn, consumables
    private const int DeckBand = 190;      // the pile's corner: 24 in + a 150-wide leaning stack + air
    private static int BottomBand => HintBand + HandBand + ControlBand + 12;

    // The three shapes a region can have. All plain Controls on purpose — a container would report its
    // children's combined minimum upward and the region would stop being fixed.
    private Control TopRegion(int top, int height, int left = PaneInset, int right = PaneInset)
        => Region(0f, 0f, top, top + height, left, right);

    private Control MiddleRegion(int top, int bottom, int left = PaneInset, int right = PaneInset)
        => Region(0f, 1f, top, -bottom, left, right);

    private Control BottomRegion(int above, int height, int left = PaneInset, int right = PaneInset)
        => Region(1f, 1f, -(above + height), -above, left, right);

    private Control Region(float anchorTop, float anchorBottom, int offsetTop, int offsetBottom, int left, int right)
    {
        var region = new Control
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = anchorTop, AnchorBottom = anchorBottom,
            OffsetLeft = left, OffsetRight = -right, OffsetTop = offsetTop, OffsetBottom = offsetBottom,
            MouseFilter = MouseFilterEnum.Pass,
        };
        _combatRoot.AddChild(region);
        return region;
    }

    // What a region is worth in width right now — last frame's pane, which does not change between rounds.
    private float PaneWidth => _combatRoot is { } root && root.Size.X > 100
        ? root.Size.X
        : GetViewportRect().Size.X * 0.72f;

    private void RenderCombatGraphical(InteractiveRunSession session, InteractiveCombat combat)
    {
        var play = Play!;
        var hero = combat.State.GetCombatant(combat.HeroId);
        var enemies = combat.State.Combatants
            .Where(c => c.Id != combat.HeroId && c.TeamId == StandardCombatIds.EnemyTeam).ToList();

        // THE HEADING, in its own band at the top.
        var head = TopRegion(PaneInset, HeadlineBand);
        var round = new Label { Text = $"Round {combat.Round}", HorizontalAlignment = HorizontalAlignment.Center };
        round.AddThemeFontSizeOverride("font_size", 18);
        round.SetAnchorsPreset(LayoutPreset.FullRect);
        head.AddChild(round);

        // THE DIVINE RULE AREA, if this fight has one. Directly under the round and over the arena: the same
        // place in every one of Act V's fights, which is the whole of the design's shared rule for the act —
        // the player must be able to look at one spot and read what reality currently means here. It is a band
        // of its OWN, so a long decree scrolls inside its own panel instead of eating the arena's height.
        var arenaTop = PaneInset + HeadlineBand + 8;
        if (DivineRuleArea() is { } divine)
        {
            var rule = TopRegion(arenaTop, DivineBand);
            var ruleView = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            ruleView.SetAnchorsPreset(LayoutPreset.FullRect);
            divine.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            ruleView.AddChild(divine);
            rule.AddChild(ruleView);
            arenaTop += DivineBand + 8;
        }

        // Arena: hero far left, enemies far right, a stretchy gap between.
        var arena = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        var column = EnemyColumn(enemies.Count);
        var nameBand = NameBandFor(
            enemies.Select(e => (Name(e, combat), column)).Append((Name(hero, combat), HeroColumn)));
        // The arena is what is left of the canvas between the bands above it and the hand below.
        var arenaHeight = GetViewportRect().Size.Y - arenaTop - BottomBand;
        var bodyHeight = BodyHeightFor(arenaHeight, nameBand);

        var heroBox = CombatantBox(combat, hero, isHero: true, HeroColumn, nameBand, bodyHeight, arenaHeight);
        // ⚠ THE COLUMNS HANG FROM A COMMON TOP, they are not each centred in the row. Centred, a body carrying
        // three status chips sits higher than one carrying none, and what the arena runs out of room for is a
        // different part of every column. Hung, the row reads as a row and the overflow is in one place.
        heroBox.SizeFlagsVertical = SizeFlags.ShrinkBegin;
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
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        enemyRow.AddThemeConstantOverride("h_separation", enemies.Count <= 2 ? ColumnGap : CrowdGap);
        enemyRow.AddThemeConstantOverride("v_separation", CrowdGap);
        foreach (var enemy in enemies)
            enemyRow.AddChild(CombatantBox(combat, enemy, isHero: false, column, nameBand, bodyHeight, arenaHeight));
        arena.AddChild(enemyRow);
        _enemyRow = enemyRow;

        // The arena SCROLLS if it has to. A column is as tall as what the body is carrying, and by an Act-II
        // boss the player can be wearing a dozen statuses: the column then grows past its share, and a
        // VBoxContainer hands out minimum heights before it hands out the leftovers — so the hand, the deck
        // and the End-turn button were pushed off the bottom of the screen by the very state this pass is
        // about making readable. A scroll view has a small minimum of its own, so the hand keeps its place and
        // nothing is hidden: what does not fit is reachable rather than gone.
        var arenaView = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        arenaView.SetAnchorsPreset(LayoutPreset.FullRect);
        arena.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        arenaView.AddChild(arena);
        var arenaBand = MiddleRegion(arenaTop, BottomBand + PaneInset);
        arenaBand.AddChild(arenaView);
        _regionArena = arenaBand;

        // THE BOTTOM BAND: whatever the fight is asking of the player right now — a prompt raised by a card,
        // the note that the enemies are moving, or the hand and its controls.
        var bottom = BottomRegion(PaneInset, BottomBand, left: PaneInset, right: PaneInset);
        var bottomBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bottomBox.SetAnchorsPreset(LayoutPreset.FullRect);
        bottomBox.AddThemeConstantOverride("separation", 8);
        bottom.AddChild(bottomBox);

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

            bottomBox.AddChild(box);
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
            bottomBox.AddChild(choiceBox);
            return;
        }

        if (!combat.IsHeroTurn)
        {
            bottomBox.AddChild(new Label { Text = "Resolving enemy actions…", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        var hint = new Label
        {
            Text = _armedCard is not null ? "Click an enemy to play it — or the card again to cancel." : " ",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        hint.CustomMinimumSize = new Vector2(0, HintBand);
        bottomBox.AddChild(hint);

        // The deck pile sits in the bottom-left corner; build it first so its top card is the fly-in origin.
        // ⚠ THE HAND DOES NOT REACH INTO THE PILE'S CORNER. They are two regions, and the hand's is the one
        // that starts where the pile's ends — the fan used to be centred on the whole pane and a big hand
        // simply lay across the deck it was dealt from.
        BuildDeckPile(combat);
        var handRegion = new Control
        {
            CustomMinimumSize = new Vector2(0, HandBand),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        bottomBox.AddChild(handRegion);
        _regionHand = handRegion;
        BuildHand(handRegion, combat, hero);

        var controls = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        controls.CustomMinimumSize = new Vector2(0, ControlBand);
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
        bottomBox.AddChild(controls);
    }

    // The draw pile in the bottom-left corner: a few offset card backs (the top one animated), plus a count.
    //
    // Built once per fight and UPDATED afterwards — see the note in Rebuild(): the animated back is a video,
    // and a video that is re-created cannot play. So the nodes here are made on the first render of a fight
    // and only their visibility, their position and the count change from then on.
    private void BuildDeckPile(InteractiveCombat combat)
    {
        var drawCount = combat.State.GetCardZones(combat.HeroId).GetCardsInZone(CardZone.DrawPile).Count;

        const int lift = 4;      // how far up and to the right each card in the stack sits on the one below
        const int caption = 26;  // the count's own band, under the ink
        const int lean = 4 * lift; // the FULL stack's slant, held fixed so a thinning pile does not move house

        // ⚠ A PILE'S FOOTPRINT IS ITS INK, NOT ONE CARD. The stack leans up and to the right, so it stands
        // taller and wider than a single back by the whole lean, and the count needs a band of its own below
        // it. Measured as one card the caption has nowhere to go: `BottomWide` was being applied to a Label
        // that had not been laid out yet, so its anchor rect was zero-high and its minimum height pushed it
        // straight out of the bottom of the holder and onto the pane's own hairline. It had been printing
        // 2 px off the edge of the window since the pile was built; D2 made it gold, and a gold thing sitting
        // on the border is not something you can keep not seeing.
        var footprint = new Vector2(CardVisuals.CardW + lean, CardVisuals.CardH + lean + caption);

        if (_deckHolder is null || !IsInstanceValid(_deckHolder))
        {
            var holder = new Control { CustomMinimumSize = footprint, Size = footprint };
            holder.SetAnchorsPreset(LayoutPreset.BottomLeft);
            holder.Position = new Vector2(24, -footprint.Y - 16);
            _combatRoot.AddChild(holder);
            _deckHolder = holder;

            // Static backs for depth, laid from the BOTTOM of the lean upward; the top one animates.
            _deckStills.Clear();
            for (var i = 0; i < 3; i++)
            {
                var still = CardVisuals.Back(animated: false);
                holder.AddChild(still);
                _deckStills.Add(still);
            }
            var top = CardVisuals.Back(animated: true);
            holder.AddChild(top);
            _deckTopNode = top;

            // ⚠ THE GOLD IN THIS CORNER IS THE COUNT, NOT A FRAME. The plan asked for a gold frame around the
            // pile so the back's violet would not read as a second accent — but the clip turned out to carry
            // its own ornate border, and a gold ring drawn around an already-framed painting is not an accent,
            // it is a picture in the wrong frame. The count is the only chrome the pile actually owns, so it
            // is the thing that goes gold: the corner still answers to the rest of the screen, and the
            // artwork is left to be artwork.
            _deckCount = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 1f, AnchorBottom = 1f,
                OffsetLeft = 0f, OffsetRight = 0f, OffsetTop = -caption, OffsetBottom = 0f,
            };
            _deckCount.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
            holder.AddChild(_deckCount);
        }

        // The pile draws FIRST, on every render including the one that made it: a card flying out of the deck
        // has to pass over the deck, and the regions it flies into are added after this. (The regions are
        // transparent Controls, so "first" costs the pile nothing else.)
        _combatRoot.MoveChild(_deckHolder, 0);

        // What the count says, and how thick the stack looks, is all that changes from render to render. The
        // pile thins from its top card downward, which is where a dealt card comes off a real one.
        var backing = Math.Min(drawCount > 0 ? drawCount - 1 : 0, 3);
        for (var i = 0; i < _deckStills.Count; i++)
        {
            _deckStills[i].Visible = i < backing;
            _deckStills[i].Position = new Vector2(i * lift, lean - i * lift);
        }
        if (_deckTopNode is not null && IsInstanceValid(_deckTopNode))
        {
            _deckTopNode.Visible = drawCount > 0;
            _deckTopNode.Position = new Vector2(backing * lift, lean - backing * lift);
        }
        if (_deckCount is not null && IsInstanceValid(_deckCount))
            _deckCount.Text = $"Draw {drawCount}";
    }

    // The hand as manually-placed card faces (a centered row), so newly-drawn cards can fly in from the deck.
    // It is laid inside the hand REGION — the bottom band minus the draw pile's corner — and centred in that.
    private void BuildHand(Control region, InteractiveCombat combat, CombatantState hero)
    {
        var cards = combat.Hand.ToList();

        // A HAND IS HELD, NOT SHELVED, AND IT IS LAID OUT ON A STEP RATHER THAN A FIXED GAP. Up to five cards
        // it is a plain row at a full gap; past that the step closes up and the cards overlap left-under-right
        // and the row leans into a shallow fan — which is what makes an overlap read as a hand of cards rather
        // than as a layout that ran out of room, and what keeps a big hand of big cards from walking off both
        // edges. The room it has is its REGION: the bottom band minus the draw pile's corner.
        const int gap = 12;
        const int fanned = 5; // beyond this many, the row starts closing up
        var available = Math.Max(CardVisuals.CardW, PaneWidth - DeckBand - PaneInset * 2);
        var step = (float)(CardVisuals.CardW + gap);
        if (cards.Count > fanned)
            step = Math.Min(step, Math.Max(36f, (available - CardVisuals.CardW) / (cards.Count - 1)));
        var totalWidth = (Math.Max(cards.Count, 1) - 1) * step + CardVisuals.CardW;

        // The tilt is a fixed SPREAD shared out, not a fixed angle per card, so a hand of twelve leans no
        // further than a hand of four — it just leans in smaller increments.
        var middle = (cards.Count - 1) / 2f;
        var tilt = cards.Count > 1 ? Math.Min(2.2f, 9f / (cards.Count - 1)) : 0f;
        const float arc = 3f; // how much lower each step from the middle of the fan sits

        // The row is placed in its region by hand — anchored to the region's bottom-left and pushed right by
        // the pile's corner plus half the room left over. A plain Control governed by anchors, so nothing the
        // cards do can push it around and the fan sits exactly where the arithmetic says.
        // ⚠ A FLYING CARD COMES OUT FROM OVER THE PILE, so the band it lands in has to draw in front of the
        // pile: the pile is moved to the front of the combat root's children on every render and every region
        // is added after it.
        var inner = new Control
        {
            AnchorTop = 1f, AnchorBottom = 1f, AnchorLeft = 0f, AnchorRight = 0f,
            OffsetTop = -(CardVisuals.CardH + 8 + arc * middle + 12),
            OffsetBottom = 0f,
            OffsetLeft = DeckBand - PaneInset + Math.Max(0f, (available - totalWidth) / 2f),
        };
        inner.OffsetRight = inner.OffsetLeft + Math.Max(totalWidth, 1);
        region.AddChild(inner);

        _cardsToAnimate.Clear();
        _handFaces.Clear();
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var cardId = card.Id;
            var armed = _armedCard is { } a && a.value == cardId.value;
            var face = CardBlockButton(combat, hero, card, armed, () => OnCardClicked(cardId));
            var fromMiddle = i - middle;
            face.Position = new Vector2(i * step, Math.Abs(fromMiddle) * arc);
            face.Size = new Vector2(CardVisuals.CardW, CardVisuals.CardH);
            // Pivot at the card's centre — the same point AnimateDraws flips a freshly-drawn card around, so
            // the fan and the deal do not fight over where the card turns.
            face.PivotOffset = new Vector2(CardVisuals.CardW / 2f, CardVisuals.CardH / 2f);
            face.RotationDegrees = fromMiddle * tilt;
            inner.AddChild(face);
            _handFaces.Add(face);
            if (!_shownHandIds.Contains(cardId.value))
                _cardsToAnimate.Add(face); // newly drawn → fly it in
        }
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
    private const int BodyHeight = 150; // the most room a body may stand in, whatever it is a picture of
    private const int ShortBody = 90;   // …and the least, when the arena has to buy back room — see BodyHeightFor

    // What the fixed parts of a column cost, so the chips at its foot can be told what is left. Estimates, and
    // named as such: a Label's height is not knowable before it is laid out. They are checked by measurement
    // rather than by arithmetic — `--smoke-crowd` and `--smoke-boss` print the arena's viewport against the
    // tallest column, and that number is what says whether these are still true.
    private const int HealthBarHeight = 22;
    private const int PhaseLine = 22;      // one phase, at 15 pt
    private const int PromisePlate = 74;   // the first telegraph: a head line, two wrapped lines, its margins
    private const int ForecastPlate = 50;  // a later day, quieter and usually one line
    private const int NameSize = 16;    // the name over a body

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
        var pane = PaneWidth;
        var room = pane - 48 - (HeroColumn + 32) - ColumnGap;   // margins, the hero's column, the gap after it
        var each = (room - ((count - 1) * CrowdGap)) / count - 32;   // 32 = the panel's own border and padding
        return (int)Math.Clamp(each, NarrowestColumn, HeroColumn);
    }

    // A combatant's column: name, a stick-figure placeholder, an HP bar, energy (hero) or intent (enemy),
    // and its status chips. When a card is armed, an enemy box becomes a clickable target.
    // HOW TALL A BODY MAY STAND. It was a constant, and at an Act V boss the constant was wrong: the divine
    // rule takes a fixed 104-point band out of the arena, and name + figure + health bar alone then fill
    // everything that is left — so NISABA'S TELEGRAPH WAS BELOW THE FOLD AT ROUND ONE. A player who cannot see
    // what a god is about to do is not playing the fight, and no amount of scrolling is a substitute for a
    // telegraph being where the eye already is.
    //
    // So the figure yields. It is the least informative part of the column — a picture of the thing, next to
    // the three facts that decide the turn — and it is the only part with any give in it. What must be legible
    // without scrolling is reserved first; the body takes what is left, down to a floor of 90 points, and an
    // ordinary fight with room to spare is untouched at the full 150.
    private static int BodyHeightFor(float arenaHeight, int nameBand)
    {
        // The health bar, one telegraph plate, one row of chips, and the separations between all of them.
        const int MustBeLegible = 22 + 74 + 26 + 24;
        return Mathf.Clamp(
            Mathf.FloorToInt(arenaHeight - nameBand - MustBeLegible), ShortBody, BodyHeight);
    }

    // ONE BAND FOR THE WHOLE ARENA, as tall as the longest name in THIS fight. A name that wraps used to make
    // its own column taller and push its health bar a line below its neighbours' — three bars at three heights
    // in the one row whose entire job is to be compared across. A fixed two-line band fixed that and charged
    // 25 points to every fight whose names all fit on one line, which the arena (already 44 short of its
    // tallest column) cannot afford. So it is measured: the tallest name decides, and a fight of short names
    // pays nothing.
    //
    // ⚠ MEASURED WITH THE LINE SPACING THE LABEL WILL DRAW WITH. GetMultilineStringSize asks the FONT how tall
    // the block is; a Label then adds the theme's line_spacing between every line. The same trap the card's
    // plaque documents — a band measured to fit exactly loses its last line to the clip.
    private static int NameBandFor(IEnumerable<(string Name, int Width)> columns)
    {
        var face = MoonvineTheme.Font ?? ThemeDB.Singleton.FallbackFont;
        var one = face.GetHeight(NameSize);
        var tallest = 0f;
        foreach (var (name, width) in columns)
        {
            var block = face.GetMultilineStringSize(name, HorizontalAlignment.Center, width, NameSize).Y;
            var lines = Mathf.Max(1, Mathf.RoundToInt(block / one));
            tallest = Mathf.Max(tallest, lines * one + (lines - 1) * 3);
        }
        return Mathf.CeilToInt(tallest) + 2;
    }

    private Control CombatantBox(
        InteractiveCombat combat, CombatantState combatant, bool isHero, int width, int nameBand,
        int bodyHeight, float arenaHeight)
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0) };
        box.AddThemeConstantOverride("separation", 4);
        // WHAT THIS COLUMN HAS SPENT SO FAR, so the chips at the foot of it can be told what is left. Every
        // other part of a column is a fixed size and MUST be legible without scrolling — a telegraph the
        // player has to go looking for is not a telegraph — and the chips are the one part that grows without
        // limit. So the chips are the part that is bounded, and they are bounded by the room, not by a count.
        var spent = 4;

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
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            // ⚠ EVERY NAME IN THE ARENA GETS THE SAME BAND — see NameBandFor. Free to be its own height, a
            // name that wraps drops its own health bar a line below its neighbours'.
            CustomMinimumSize = new Vector2(width, nameBand),
            ClipText = true,
        };
        name.AddThemeFontSizeOverride("font_size", NameSize);
        box.AddChild(name);
        spent += nameBand + 4;

        // The body: its picture if the file is there, the stick figure until it is. A hero has a character
        // slot rather than an enemy one, so it keeps the figure for now.
        var figure = CardVisuals.Body(
            isHero ? null : combatant.DefinitionId.value,
            isHero ? MoonvineTheme.Accent : MoonvineTheme.Harm,
            facing: isHero ? 1 : -1,
            dead: !combatant.IsAlive,
            width: width - 20,
            height: bodyHeight);
        box.AddChild(figure);
        spent += bodyHeight + 4;

        box.AddChild(HealthBar(combatant, width - 30));
        spent += HealthBarHeight + 4;

        // The phase goes directly above what the body is about to do, because that is the line it corrects.
        if (PhaseBanner(combat, combatant) is { } phase)
        {
            box.AddChild(phase);
            spent += PhaseLine * PhaseCount(combatant) + 4;
        }

        if (isHero)
        {
            var energy = new Label
            {
                Text = ResourcePoolsLine(combatant),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            energy.AddThemeColorOverride("font_color", MoonvineTheme.Signal);
            box.AddChild(energy);
            spent += 24 + 4;
        }
        else if (combatant.IsAlive)
        {
            // WHAT IT IS ABOUT TO DO, AND AS FAR PAST THAT AS THE PLAYER CAN SEE. The engine has always been
            // able to project an enemy's next several actions for a hero who has been granted the sight
            // (the Article of Full Disclosure; Nanshe's Ration Tablet, which shows all three days of a
            // Distribution before the first of them) — and this screen only ever drew the first one, so a
            // faculty the player had been given reached nothing. The extra days are drawn dimmer and
            // numbered, because they are a forecast and the first line is the promise.
            var days = combat.UpcomingIntentsFor(combatant.Id);
            for (var ahead = 0; ahead < days.Count; ahead++)
            {
                box.AddChild(IntentPlate(days[ahead], ahead, width));
                spent += (ahead == 0 ? PromisePlate : ForecastPlate) + 4;
            }
        }

        if (StatusChips(combat, combatant) is { } chips)
            box.AddChild(Bounded(chips, combatant, width, Mathf.FloorToInt(arenaHeight) - spent));

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

    // The run's health, on the SAME bar a fight draws. It was a line of text out here and a filled track in
    // there, for one number — and this is the one the player reads between rooms, deciding whether to take
    // the elite. A magnitude drawn as a magnitude in one place and spelled out in the other is two facts as
    // far as the eye is concerned.
    private static Control RunHealthBar(RunState run, int width) =>
        Track(run.Health.Current, run.Health.Max, block: 0, width);

    private static Control HealthBar(CombatantState combatant, int width) =>
        Track(combatant.Health.Current, combatant.Health.Max, Block(combatant), width);

    private static Control Track(int current, int max, int block, int width)
    {
        var holder = new Control { CustomMinimumSize = new Vector2(width, 22) };
        // A health bar is a MAGNITUDE, not an alert, so it is allowed the red the warnings gave up: blood
        // on an empty track, and the same bar for hero and enemy because health is health.
        var bg = new ColorRect { Color = MoonvineTheme.BgRaised };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        holder.AddChild(bg);
        var ratio = max > 0 ? Mathf.Clamp((float)current / max, 0, 1) : 0;
        var fill = new ColorRect { Color = MoonvineTheme.Harm };
        fill.SetAnchorsPreset(LayoutPreset.FullRect);
        fill.AnchorRight = ratio;
        fill.OffsetRight = 0;
        holder.AddChild(fill);
        var label = new Label
        {
            Text = $"{current}/{max}" + (block > 0 ? $"   🛡{block}" : ""),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetAnchorsPreset(LayoutPreset.FullRect);
        holder.AddChild(label);
        return holder;
    }

    // A hand/choice card as a black block: name, cost, and its ability text. `onClick` arms/plays it.
    // The marks a card may wear on its face. Content places marks freely and most of them are plumbing; these
    // are the ones a fight asks the player to act on.
    private static readonly Dictionary<string, (string Label, string Explanation)> CardMarks =
        new(StringComparer.Ordinal)
        {
            ["eanna_claim"] = ("PROPERTY OF EANNA",
                "Inanna has entered this copy in the Eanna Ledger. Its first play each turn costs 1 Energy "
                + "less, and every use of it writes 1 Temple Due. Dedicating it settles 4."),
            // Nanna-Sin's two stamps. The whole decision his fight asks — which action should return when the
            // count comes again — is made on the cards, so both of them have to be legible ON the card.
            ["moon_counted"] = ("COUNTED",
                "Nanna-Sin counted this copy. When the Lunar Count that took it comes round again, a free "
                + "copy of it is in your hand for that turn."),
            ["lunar_echo"] = ("LUNAR ECHO",
                "A copy the moon returned. It costs nothing this turn and is gone at the end of it — and "
                + "under the Full Moon it happens a second time at half strength. It cannot itself be "
                + "counted, and playing it does not take the count from a card that can."),
        };

    // A THIN CALLER. Everything about how a card LOOKS lives in CardVisuals.Face; this decides only what is
    // true of this card right now — what it is called, what it costs, whether it can be paid for, what has
    // been done to this copy — and hands that over.
    private Control CardBlockButton(InteractiveCombat combat, CombatantState hero, CardInstance card, bool highlighted, Action onClick)
    {
        var definition = card.DefinitionId.value;
        // TWO SEPARATE REASONS A CARD CANNOT BE PLAYED, and the card face has to show both. The purse is one
        // the screen can work out for itself; a RULE that forbids the play is not — a decree that caps the
        // turn at four cards, or forbids a kind following its own kind, makes the fifth card genuinely
        // unavailable, and a card refused only when it is clicked is a rule the player was never shown.
        var affordable = CanPay(hero, definition) && combat.CanPlay(card.Id);
        var presentation = GameHost.Instance.Blueprint.Presentation.Cards.GetValueOrDefault(definition);
        var rules = presentation?.FlavorText ?? "";

        // WHAT HAS BEEN DONE TO THIS COPY, and not what kind of card it is. A per-instance mark is content's
        // way of making one copy of a card special — Inanna's claim is the first that the PLAYER is asked to
        // plan around, and a stamp that only the engine can see is a rule nobody was told. Anything not named
        // in CardMarks is a mark the player was never meant to read, and stays invisible.
        var marks = card.Marks
            .Where(mark => CardMarks.ContainsKey(mark.value))
            .Select(mark => CardMarks[mark.value])
            .ToList();

        // The hover carries the PRICE as well as the explanation: the ring on the frame has room for a number
        // and nothing else, so what the number is denominated in is said here.
        var tooltip = string.Join("\n", new[] { CostLabel(definition), Glossary.Explain(rules) }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        return CardVisuals.Face(
            new CardVisuals.CardFace(
                Id: definition,
                Title: CardName(definition),
                Cost: CostBadge(definition),
                Rules: rules,
                Rarity: presentation?.Rarity,
                Tooltip: tooltip,
                Dimmed: !affordable,
                Armed: highlighted,
                Marks: marks),
            onClick);
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

    // THE PILE MUST KEEP PLAYING. The deck's back is a 36-second clip, and the screen it stands on is rebuilt
    // from scratch on every state change in a fight. When the pile was rebuilt with it, its VideoStreamPlayer
    // was a new object each time and started at 0.00 — so the loop never got past its first tenth of a second
    // and the card back visibly snapped back on every click. This plays three cards and reports whether the
    // clip is still the SAME player and whether its position moved forward; a rebuilt pile fails both.
    private async System.Threading.Tasks.Task SmokeDeck()
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
            GD.Print("smoke-deck: needs a window (the clip does not decode headless)");
            GetTree().Quit();
            return;
        }

        (ulong Id, double At)? first = null;
        (ulong Id, double At)? last = null;
        var players = 0;
        for (var round = 0; round < 4; round++)
        {
            await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
            var found = new List<VideoStreamPlayer>();
            void Walk(Godot.Node n)
            {
                if (n is VideoStreamPlayer v) found.Add(v);
                foreach (var c in n.GetChildren()) Walk(c);
            }
            Walk(this);
            players = Math.Max(players, found.Count);
            if (found.Count > 0)
            {
                var seen = (found[0].GetInstanceId(), (double)found[0].StreamPosition);
                first ??= seen;
                last = seen;
            }
            if (Play?.CombatDriver?.Current is { } fight && fight.IsHeroTurn)
            {
                var hero = fight.State.GetCombatant(fight.HeroId);
                var card = fight.Hand.FirstOrDefault(c => CanPay(hero, c.DefinitionId.value));
                var target = fight.State.Combatants.FirstOrDefault(c => c.Id != fight.HeroId && c.IsAlive
                    && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
                if (card is not null) Play.CombatDriver.PlayCard(card.Id, target);
            }
        }

        var kept = first is { } f && last is { } l && f.Id == l.Id;
        var advanced = first is { } f2 && last is { } l2 && l2.At > f2.At + 0.5;
        GetViewport().GetTexture().GetImage().SavePng("user://smoke-deck.png");
        GD.Print($"smoke-deck: players={players} same-clip={(kept ? "yes" : "NO")} "
            + $"advanced={(advanced ? "yes" : "NO")} from={first?.At:0.00} to={last?.At:0.00} "
            + $"{(kept && advanced ? "PASS" : "FAIL")}");
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

    // ⚠ THE FORMAT BUG, MEASURED RATHER THAN ASSUMED. The complaint was that a card in the hand changes
    // shape when it is clicked. A face is HANDED a size, but Godot clamps a Control's size UP to its combined
    // minimum — so any child that reports a minimum of its own (a wrapping Label is the classic one) can push
    // the card out of the frame it was given, and how far it pushes depends on the width it happened to be
    // measured at. Clicking calls Rebuild(), which measures everything again from scratch: same card, new
    // shape.
    //
    // This probe arms and disarms its way through a hand ten times and reports, per slot, the smallest and
    // largest that slot ever was. It arms the card DIRECTLY instead of going through OnCardClicked, because a
    // self-only card would be spent by a real click and the hand would change under the measurement — the
    // state on screen (armed → rebuilt → cancelled → rebuilt) is exactly the one a click produces. PASS is
    // one size per slot, ten clicks apart.
    // THE SHELF AT A HOSTILE COUNT. Four relics prove nothing: a strip that wraps is only tested by a run
    // that has won enough to fill it, and the Boss pool alone holds 69 — a five-act run really can wear that
    // many. So the probe puts them on rather than walking for them, because what is being measured is the
    // LAYOUT and not the drop rate, and a layout does not care how a relic was earned.
    //
    // It asks three things a screenshot cannot: does every tile sit inside the panel, does the shelf wrap
    // into rows instead of one column, and does the sidebar carry the overflow by scrolling rather than by
    // pushing the log off the screen.
    // THE REPORT AS A PLAYER ACTUALLY MAKES ONE: mid-fight, Esc, the button in the menu. The title-screen probe
    // (--smoke-bug) covers the window; this covers the two things only a run can prove — that Esc captures the
    // fight BEFORE the menu covers it, and that the diagnostics block knows which room, seed and fight this was.
    // Those act/seed/health lines are never executed by the title-screen path.
    private async System.Threading.Tasks.Task SmokeBugInRun()
    {
        var session = Session;
        var play = Play;
        for (var step = 0; step < 300 && session is not null && play is not null; step++)
        {
            if (play.CombatDriver?.Current is { IsHeroTurn: true })
                break;
            if (session.IsAwaitingNodeChoice)
                session.PickNode((session.PendingNodeChoices.FirstOrDefault(n => n.HasTag(MapNodeTags.Combat))
                    ?? session.PendingNodeChoices[0]).Id.Value);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else if (session.IsAwaitingEntities)
                session.PickEntities([0]);
            else if (session.IsAwaitingChoice)
                session.Pick(session.PendingChoices[^1].Id);
            else if (play.CombatDriver?.Current is { } combat)
                play.CombatDriver.EndTurn();
            else
                break;
        }
        for (var i = 0; i < 4; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // Esc through the REAL handler, so the capture happens where a player's keystroke makes it happen.
        var escape = new InputEventAction { Action = "ui_cancel", Pressed = true };
        _UnhandledInput(escape);
        for (var i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"smoke-bug-run: menu open={GetNodeOrNull("SettingsOverlay") is not null}"
            + $" · screen remembered={BugReport.LastScreen is not null}");

        if (FindButton(GetNodeOrNull("SettingsOverlay"), "Report a bug") is not { } button)
        {
            GD.Print("smoke-bug-run: THE MENU HAS NO REPORT BUTTON");
            GetTree().Quit();
            return;
        }
        button.EmitSignal(BaseButton.SignalName.Pressed);
        for (var i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (GetNodeOrNull(BugReportPanel.OverlayName)
                ?.FindChild(nameof(BugReportPanel), recursive: true, owned: false) is not BugReportPanel panel)
        {
            GD.Print("smoke-bug-run: the window did not open");
            GetTree().Quit();
            return;
        }
        panel.Fill("The enemy's intent said 9 damage and it hit me for 14.");
        for (var i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng("user://smoke-bug-run.png");
        await panel.Submit();
        GD.Print("smoke: screenshot user://smoke-bug-run.png");
        GetTree().Quit();
    }

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

    private async System.Threading.Tasks.Task SmokeShelf()
    {
        var wanted = SimArg("--shelf", 69);
        var session = Session;
        if (session is null)
        {
            GD.Print("smoke-shelf: no session");
            GetTree().Quit();
            return;
        }

        // One from every pool before a second from any of them, so all six frames are on the shelf however
        // small the count is — a probe that filled the strip with 69 boss relics would look right and prove
        // only that one frame works.
        var presentation = GameHost.Instance.Blueprint.Presentation.Relics;
        var byPool = GameHost.Instance.Blueprint.Relics
            .GroupBy(r => presentation.GetValueOrDefault(r.Id)?.Frame ?? "normal", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.ToList())
            .ToList();
        var worn = new List<string>();
        for (var round = 0; worn.Count < wanted; round++)
        {
            var got = false;
            foreach (var pool in byPool.Where(pool => round < pool.Count))
            {
                if (worn.Count >= wanted) break;
                worn.Add(pool[round].Id);
                got = true;
            }
            if (!got) break;   // the document has fewer relics than the count asked for
        }
        foreach (var id in worn)
            session.Run.AddRelic(new RogueDeck.Run.RelicInstance(
                session.Run.Content.GetRelic(new RogueDeck.Run.RelicId(id))));
        // A relic can be switched off mid-run, and "(off)" has to survive the move from a list to a shelf.
        var off = 0;
        for (var i = 6; i < session.Run.Relics.Count; i += 7, off++)
            session.Run.Relics[i].SetEnabled(false);

        Rebuild();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var shelf = FindShelf(this);
        var tiles = shelf is null ? [] : shelf.GetChildren().OfType<Control>().ToList();
        var rows = tiles.Select(t => Mathf.RoundToInt(t.Position.Y)).Distinct().Count();
        var perRow = rows > 0 ? tiles.Count / rows : 0;
        var panel = shelf?.GetParent() as Control;
        var right = tiles.Count > 0 ? tiles.Max(t => t.GlobalPosition.X + t.Size.X) : -1;
        var limit = panel is not null ? panel.GlobalPosition.X + panel.Size.X : GetViewportRect().Size.X;
        var scroll = FindScroll(this);
        var viewport = scroll?.Size.Y ?? 0;
        var content = scroll?.GetVScrollBar()?.MaxValue ?? 0;

        GD.Print($"smoke-shelf: worn={session.Run.Relics.Count} (asked {wanted}, {off} switched off) "
            + $"tiles={tiles.Count} rows={rows} ~{perRow}/row "
            + $"right={right:0}/{limit:0} outside={(right > limit + 1 ? "YES" : "no")} "
            + $"content={content:0} viewport={viewport:0} scrolls={(content > viewport + 1 ? "yes" : "no")} "
            + $"error={Session?.Error ?? "none"}");
        if (tiles.Count > 0)
            GD.Print($"  tile={tiles[0].Size.X:0}x{tiles[0].Size.Y:0} pools=" + string.Join(" ", byPool
                .Select(pool => $"{presentation.GetValueOrDefault(pool[0].Id)?.Frame}:{pool.Count}")));
        // WHAT THE HOVER ACTUALLY SAYS, read back out of the live tree. A headless run cannot hover, and
        // "the tooltip was set" is not "the tooltip says the name and the rules" — so one is printed whole.
        if (tiles.FirstOrDefault(t => !string.IsNullOrEmpty(t.TooltipText)) is { } sample)
            GD.Print($"  hover ⟨{sample.TooltipText.Replace("\n", " ⏎ ")}⟩");
        if (rows <= 1 && tiles.Count > 1)
            GD.Print("  ⚠ ONE ROW — the shelf is not wrapping");
        if (perRow <= 1 && tiles.Count > 1)
            GD.Print("  ⚠ ONE COLUMN — the shelf was handed its minimum width, not the panel's");

        ReportTooltips("shelf");
        await CaptureThenQuit("smoke-shelf.png");
    }

    private static HFlowContainer? FindShelf(Godot.Node node)
    {
        if (node is HFlowContainer flow) return flow;
        foreach (var child in node.GetChildren())
            if (FindShelf(child) is { } found) return found;
        return null;
    }

    private static ScrollContainer? FindScroll(Godot.Node node)
    {
        if (node is ScrollContainer scroll && FindShelf(scroll) is not null) return scroll;
        foreach (var child in node.GetChildren())
            if (FindScroll(child) is { } found) return found;
        return null;
    }

    private async System.Threading.Tasks.Task SmokeFormat()
    {
        var session = Session;
        for (var i = 0; i < 8 && Play?.CombatDriver?.Current is null && session is not null; i++)
        {
            if (session.IsAwaitingNodeChoice) session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude) session.Continue();
            else break;
        }
        if (Play?.CombatDriver?.Current is not { } combat)
        {
            GD.Print("smoke-format: no fight reached");
            GetTree().Quit();
            return;
        }

        var names = combat.Hand.Select(c => CardName(c.DefinitionId.value)).ToList();
        var low = new List<Vector2>();
        var high = new List<Vector2>();

        async System.Threading.Tasks.Task Settle()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        void Measure()
        {
            for (var i = 0; i < _handFaces.Count; i++)
            {
                var size = _handFaces[i].Size;
                while (low.Count <= i) { low.Add(size); high.Add(size); }
                low[i] = new Vector2(Math.Min(low[i].X, size.X), Math.Min(low[i].Y, size.Y));
                high[i] = new Vector2(Math.Max(high[i].X, size.X), Math.Max(high[i].Y, size.Y));
            }
        }

        await Settle();
        Measure();
        for (var click = 0; click < 10 && _handFaces.Count > 0; click++)
        {
            var hand = Play?.CombatDriver?.Current?.Hand.ToList() ?? [];
            if (hand.Count == 0) break;
            _armedCard = hand[click % hand.Count].Id;
            Rebuild();
            await Settle();
            Measure();
            _armedCard = null;
            Rebuild();
            await Settle();
            Measure();
        }

        // TWO separate ways a card can be the wrong shape, and the first one is the loud one: a slot that is
        // never the size it was handed. A card 24 px wider than its slot overlaps the neighbour it was placed
        // 12 px clear of, and a card whose title wraps to two lines is taller than the card beside it and
        // hangs out of the hand row into whatever is under it. Drift across clicks is the second.
        var asked = new Vector2(CardVisuals.CardW, CardVisuals.CardH);
        var wrong = 0;
        for (var i = 0; i < low.Count; i++)
        {
            var steady = low[i].IsEqualApprox(high[i]);
            var right = steady && low[i].IsEqualApprox(asked);
            if (!right) wrong++;
            GD.Print($"smoke-format: slot {i} \"{(i < names.Count ? names[i] : "?")}\" "
                + $"w {low[i].X:0.#}-{high[i].X:0.#} h {low[i].Y:0.#}-{high[i].Y:0.#} "
                + (right ? "ok" : steady ? "WRONG SIZE" : "DRIFTS"));
        }
        GD.Print($"smoke-format: {low.Count} slots, asked for {asked.X:0}x{asked.Y:0}, "
            + $"{wrong} off over 10 clicks — {(wrong == 0 ? "PASS" : "FAIL")}");
        await CaptureThenQuit("smoke-format.png");
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

        // HEALTH IS THE SAME BAR IT IS IN A FIGHT. It was a line of text here and a filled track three inches
        // to the left, for the same number — and this is the one the player reads between rooms, when deciding
        // whether to take the elite. A magnitude that is drawn as a magnitude in one place and spelled out in
        // the other is two facts as far as the eye is concerned.
        _sidebar.AddChild(RunHealthBar(run, SidebarWidth - PaneInset * 2));

        // ⚠ AND THE RESOURCES BY THEIR NAMES. "gold: 276" printed the resource's ID, which is the same fault
        // the way-screen's "Use standard.scheduled_the_collapse" was — the document names these things and the
        // run playback already holds the table.
        foreach (var (resource, amount) in run.Resources.OrderBy(r => r.Key.Value, StringComparer.Ordinal))
            _sidebar.AddChild(MutedLabel(
                $"{Play?.ResourceNames.GetValueOrDefault(resource.Value) ?? Humanized(resource.Value)}: {amount}"));

        // ── the shelf ────────────────────────────────────────────────────────────
        // What is worn is drawn as objects, not spelled out as a list. A relic strip only works if the eye
        // can take the whole of it in at once, and the words are one hover away — the same words the list
        // used to print, through the same glossary.
        if (run.Relics.Count > 0)
        {
            _sidebar.AddChild(new Label { Text = $"Relics ({run.Relics.Count})" });
            var shelf = Shelf();
            foreach (var relic in run.Relics)
            {
                var look = GameHost.Instance.Blueprint.Presentation.Relics.GetValueOrDefault(relic.Id.Value);
                shelf.AddChild(CardVisuals.Tile(new CardVisuals.RelicFace(
                    Id: relic.Id.Value,
                    Title: relic.Definition.DisplayName,
                    // The pool the relic was won from, which the document names for exactly this reason: the
                    // visual canon gives every pool its own frame, so the shelf is read by rank before a
                    // single object on it is recognised. A relic without one gets the quiet frame.
                    Pool: look?.Frame,
                    Tooltip: $"{relic.Definition.DisplayName}{(relic.Enabled ? "" : " (off)")}\n"
                        + Glossary.Explain(look?.FlavorText),
                    Off: !relic.Enabled)));
            }
            _sidebar.AddChild(shelf);
        }
        // A consumable is worn the same way and is spent rather than kept, so it earns its own shelf under
        // its own heading — same tile, no pool, because it was never drawn from one.
        if (run.Consumables.Count > 0)
        {
            _sidebar.AddChild(new Label { Text = $"Consumables ({run.Consumables.Count})" });
            var shelf = Shelf();
            foreach (var consumable in run.Consumables)
            {
                var id = consumable.DefinitionId.Value;
                var look = GameHost.Instance.Blueprint.Presentation.Consumables.GetValueOrDefault(id);
                shelf.AddChild(CardVisuals.Tile(new CardVisuals.RelicFace(
                    Id: id,
                    Title: ConsumableName(id),
                    Pool: look?.Frame,
                    Tooltip: $"{ConsumableName(id)}\n{Glossary.Explain(look?.FlavorText)}",
                    Off: false)));
            }
            _sidebar.AddChild(shelf);
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

    // THE SHELF ITSELF. An HFlowContainer is the one container that fills a row and then starts another, so
    // the strip grows downwards as relics are won and never sideways off the panel — and the sidebar already
    // scrolls, so sixty-nine of them cost a scroll and not a layout.
    //
    // ⚠ A SCROLLCONTAINER HANDS ITS CHILD A MINIMUM, NOT A WIDTH — and a wrapping container's minimum width
    // is ONE tile, so inside a scroll that may scroll sideways this shelf would have come out as a single
    // column sixty-nine squares tall. The sidebar's horizontal scrolling is switched off where it is built,
    // which is what makes the ScrollContainer stretch the shelf to the panel and lets it wrap. Third door
    // into the same trap as D1's card minimum and D3's one-letter-per-line relic name: only a container's
    // own rules decide what its children get.
    private static HFlowContainer Shelf()
    {
        var shelf = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        shelf.AddThemeConstantOverride("h_separation", 4);
        shelf.AddThemeConstantOverride("v_separation", 4);
        return shelf;
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

    // A passing word — a run saved, a rule that refused a play. It was a bare line of amber text laid straight
    // over whatever happened to be beneath it, which on a hand of cards is a sentence written across a card.
    // It gets a ground of its own, and it fades out rather than vanishing: a message that disappears between
    // two frames is one the player is never sure they saw.
    private void Toast(string message)
    {
        var plate = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(MoonvineTheme.BgRaised, MoonvineTheme.Signal));
        var label = new Label { Text = message, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeColorOverride("font_color", MoonvineTheme.Signal);
        plate.AddChild(label);

        // CenterBottom anchors a control by its top-left to the middle of the bottom edge, so a plate as wide
        // as its words hangs off to the right of centre unless it is pulled back by half of itself. The bare
        // Label had the same fault and nobody saw it, because a line of text with no ground has no edge to be
        // wrong about.
        plate.SetAnchorsPreset(LayoutPreset.CenterBottom);
        AddChild(plate);
        // ⚠ ASK FOR THE MINIMUM, NOT THE SIZE. A control added this frame has not been laid out yet and its
        // Size is still zero, so a plate centred on Size.X is not centred at all — it is exactly where the
        // un-centred bare Label used to be, and the bug would have looked like the fix.
        plate.Position -= new Vector2(plate.GetCombinedMinimumSize().X / 2, 56);

        var fade = CreateTween();
        fade.TweenInterval(2.2);
        fade.TweenProperty(plate, "modulate:a", 0.0f, 0.6);
        fade.TweenCallback(Callable.From(plate.QueueFree));
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

    // What goes IN THE RING on the card frame: the amount, and only the amount. The hole is 6.8 % of the
    // card's width — a glyph and a number do not both fit — so the ring says how much and the hover says of
    // what. Every card in the game is priced in energy today; if one ever is not, the amounts still line up
    // and the hover is where the difference is legible.
    private string CostBadge(string definitionId)
    {
        var costs = FullCosts(definitionId);
        return costs.Count == 0 ? "0" : string.Join("\u00b7", costs.Select(c => c.Amount.ToString()));
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
    // health bar and intent below the fold at round one, which is the same fault wearing a different hat.
    //
    // ⚠ IT USED TO BOUND BY A COUNT, AND A COUNT IS THE WRONG QUESTION. Eight chips was the trigger, and at
    // Nisaba — an Act V arena, 104 points of it spent on the divine rule — a body wearing FOUR was enough to
    // push the boss's telegraph 149 points below the fold. What matters is not how many chips there are but
    // how much room is left after the things that must be legible, so that is what is asked. Under two rows'
    // worth, the chips scroll in two rows: a column whose fixed parts do not fit is a fight the arena cannot
    // show, and the scroll is the honest way to say so.
    private static Control Bounded(Control chips, CombatantState combatant, int width, int room)
    {
        const int ChipRow = 26;
        const int Floor = ChipRow * 2;
        var rows = combatant.Statuses.Count(s => s.Visibility == StatusVisibility.Visible && !IsPhase(s));
        if (rows * ChipRow <= room)
            return chips;   // it fits in the room this column has; nothing to bound

        var view = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(width, Mathf.Max(Floor, room)),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        chips.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        view.AddChild(chips);
        return view;
    }

    private static int PhaseCount(CombatantState combatant) =>
        combatant.Statuses.Count(s => s.Visibility == StatusVisibility.Visible && IsPhase(s));

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
    // WHAT IT IS ABOUT TO DO — the one line on this screen a player reads every single turn, and until D6 it
    // was set in the same weight as the four lines around it. It is a PLATE now: a ground of its own and a
    // rail down its leading edge in the intent's colour, so what kind of turn is coming can be read from the
    // colour of a band before a word of it is read. That is the whole point of a telegraph.
    //
    // ⚠ THE RAIL IS ON THE LEFT AND NOTHING ELSE IS. A box outlined all the way round is a panel, and the
    // column is already made of panels; one edge reads as an accent instead of as another container.
    private static Control IntentPlate(
        RogueDeck.Scenario.Authoring.ActionIntent intent, int ahead, int width)
    {
        // AS FAR PAST THE FIRST AS THE PLAYER CAN SEE. The engine projects an enemy's next several actions for
        // a hero who has been granted the sight (the Article of Full Disclosure; Nanshe's Ration Tablet, which
        // shows all three days of a Distribution before the first). The extra days are a FORECAST and are
        // drawn as one — quiet ground, quiet rail, numbered — because the first line is the promise.
        var promise = ahead == 0;
        var colour = promise ? MoonvineTheme.IntentColor(intent.Kind) : MoonvineTheme.TextMuted;

        var plate = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Pass, // the targeting overlay keeps the click
            // What it is about to do names things; hovering says what they are.
            TooltipText = Glossary.Explain(null, intent.Label),
        };
        var box = MoonvineTheme.Panel(
            promise ? MoonvineTheme.BgRaised : MoonvineTheme.BgPanelStrong, colour, radius: 4);
        box.BorderWidthTop = box.BorderWidthBottom = box.BorderWidthRight = 0;
        box.BorderWidthLeft = promise ? 3 : 2;
        box.ContentMarginLeft = 8;
        box.ContentMarginRight = box.ContentMarginTop = box.ContentMarginBottom = 5;
        plate.AddThemeStyleboxOverride("panel", box);

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        column.AddThemeConstantOverride("separation", 1);

        var head = new Label
        {
            Text = promise
                ? $"{RogueDeck.Scenario.Authoring.IntentDisplay.Glyph(intent.Kind)} "
                    + RogueDeck.Scenario.Authoring.IntentDisplay.KindWord(intent.Kind)
                : $"then {new string('I', ahead + 1)}",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        head.AddThemeFontSizeOverride("font_size", promise ? 15 : 12);
        head.AddThemeColorOverride("font_color", colour);
        column.AddChild(head);

        var says = new Label
        {
            Text = intent.Label,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            // ⚠ The plate is inside a column of a FIXED width and a Label's minimum is its longest word; without
            // a ceiling a body whose intent says "Reconsideration" widens every column in the arena.
            CustomMinimumSize = new Vector2(width - 34, 0),
        };
        says.AddThemeFontSizeOverride("font_size", promise ? 14 : 12);
        says.AddThemeColorOverride("font_color", promise ? MoonvineTheme.TextSoft : MoonvineTheme.TextMuted);
        column.AddChild(says);

        plate.AddChild(column);
        return plate;
    }

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
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);

        foreach (var status in shown)
        {
            StatusDefinition? definition = null;
            registry?.TryGetStatus(status.DefinitionId, out definition);
            var colour = status.Polarity switch
            {
                StatusPolarity.Buff => MoonvineTheme.Accent,
                StatusPolarity.Debuff => MoonvineTheme.Harm,
                _ => MoonvineTheme.TextMuted,
            };
            var hover = Glossary.Explain(StatusTooltip(status, definition), definition?.DescriptionKey);

            // ⚠ A CHIP HAS TO BE AN OBJECT. These were coloured text in a row, and four of them under a body
            // read as a sentence about it rather than as four things it is carrying — which is the one
            // question the row exists to answer: how MANY, and are they mine or against me. A ground and a
            // hairline make them countable at a glance; the polarity keeps the colour it had.
            var chip = new PanelContainer
            {
                MouseFilter = Control.MouseFilterEnum.Pass, // let the targeting overlay keep the click
                TooltipText = hover,
            };
            var box = MoonvineTheme.Panel(MoonvineTheme.BgRaised, new Color(colour, 0.45f), radius: 4);
            box.ContentMarginLeft = box.ContentMarginRight = 6;
            box.ContentMarginTop = box.ContentMarginBottom = 2;
            chip.AddThemeStyleboxOverride("panel", box);

            var text = new Label
            {
                Text = StatusText(status, definition),
                MouseFilter = Control.MouseFilterEnum.Pass,
                TooltipText = hover,
            };
            text.AddThemeFontSizeOverride("font_size", 13);
            text.AddThemeColorOverride("font_color", colour);
            chip.AddChild(text);
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
