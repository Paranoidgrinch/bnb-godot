using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace BnbGodot;

// The run simulator: a player made of dice. It walks the REAL screens' state — every answer goes through the
// same session/driver the mouse drives — but it answers at random: a random fork, a random door, a random
// card at a random enemy, a random pick from every offer. It does not play WELL; it plays BROADLY and fast,
// so a batch of runs touches content a careful player would never reach in a hundred sittings.
//
// Everything it does is printed, one line per answer, so the log IS the reproduction: seed + character at the
// top, then every room, every choice and every play in order, with the engine's own narration folded in.
// `tools/simulate.sh` runs one process per run (a crash then costs one run, not the batch) and files the logs.
//
//   godot --headless -- --sim [--sim-seed N] [--sim-immortal] [--sim-steps N]
public partial class SessionScreen : Control
{
    private const int SimPlaysInATurnNobodyMakes = 50;
    private const int SimTurnsAFightShouldNotNeed = 100;

    public static string? SimCharacter;   // whoever Boot rolled for this run, for the log header

    public static bool IsSimulating => OS.GetCmdlineUserArgs().Contains("--sim");

    public static int SimArg(string name, int fallback)
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var value) ? value : fallback;
    }

    private async System.Threading.Tasks.Task SimulateRun()
    {
        var seed = SimArg("--sim-seed", 1);
        var budget = SimArg("--sim-steps", 40000);
        var rng = new Random(seed);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var session = Session;
        var play = Play;
        var rooms = new List<string>();
        var acts = 1;
        string? lastRoom = null;
        var loggedNarration = 0;
        var problems = 0;
        var fights = 0;
        var reason = "the run finished";
        var crash = "";
        var hpAtActBoss = new Dictionary<int, int>();   // what each act's boss cost to REACH
        var hpBeforeRoom = 0;
        // The number the balance question actually wants: every point of health the run has taken off,
        // ADDED UP. A remaining-health reading cannot be it — the content heals, and one door in act II
        // ("perpetual_borrower", settle) puts a runner back to full, which would erase everything the act
        // had cost up to there. Healing is counted on its own, because a game that hurts a lot and heals a
        // lot is not the same game as one that does neither.
        var damageTaken = 0;
        var healed = 0;
        var damageAtActBoss = new Dictionary<int, int>();
        var hpLastSeen = session?.Run.Health.Current ?? 0;

        if (SimStringArg("--sim-policy") is { } policyPath)
        {
            _policy = SimPolicy.Load(policyPath);
            if (_policy is null)
            {
                GD.Print($"sim: could not read the policy at {policyPath}");
                GetTree().Quit(2);
                return;
            }
        }
        var policy = _policy;

        GD.Print($"sim: policy={policy?.Name ?? "random"} seed={seed} character={SimCharacter ?? "—"} "
            + $"hp={session?.Run.Health.Current}/{session?.Run.Health.Max} deck={session?.Run.Deck.Count} "
            + $"relics={string.Join(",", session?.Run.Relics.Select(r => r.Id.Value) ?? [])}");

        // The greedy walker's guards, which a random walker needs just as much: never re-offer a play the
        // engine refused, never repeat a play that moved nothing (a card may put a copy of itself back in
        // hand for ever), and give both the turn and the fight a ceiling.
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

        try
        {
            for (var step = 0; step < budget && session is not null && play is not null && !session.IsComplete; step++)
            {
                // Fold in the game's own narration since the last answer: what the engine SAID happened.
                var narration = session.Run.Log;
                for (; loggedNarration < narration.Count; loggedNarration++)
                    GD.Print($"    | {narration[loggedNarration].Message}");

                if (session.Error is not null || play.Error is not null)
                {
                    reason = "an error was raised";
                    break;
                }
                // One frame every twenty answers: the screen rebuilds into fresh nodes per answer and frees
                // the old ones deferred — a walk that never yields never lets the tree collect anything.
                if (step % 20 == 19)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                var hpNow = session.Run.Health.Current;
                if (hpNow < hpLastSeen)
                    damageTaken += hpLastSeen - hpNow;
                else if (hpNow > hpLastSeen)
                    healed += hpNow - hpLastSeen;
                hpLastSeen = hpNow;

                if (session.Run.CurrentNodeId?.Value is { } here && here != lastRoom)
                {
                    lastRoom = here;
                    var node = session.Run.Map.Nodes.FirstOrDefault(n => n.Id.Value == here);
                    var role = node is null ? "?" : MapView.Role(node);
                    rooms.Add($"{session.Run.ActNumber}:{role}");
                    acts = Math.Max(acts, session.Run.ActNumber);
                    var spent = hpBeforeRoom == 0 ? 0 : hpBeforeRoom - session.Run.Health.Current;
                    hpBeforeRoom = session.Run.Health.Current;
                    if (node is not null && node.HasTag(MapNodeTags.Boss))
                    {
                        hpAtActBoss[session.Run.ActNumber] = session.Run.Health.Current;
                        damageAtActBoss[session.Run.ActNumber] = damageTaken;
                    }
                    GD.Print($"[{clock.Elapsed.TotalSeconds,6:0.0}s {step,5}] ROOM {Where(session)} {role} "
                        + $"cost={spent} hp={session.Run.Health.Current}/{session.Run.Health.Max} "
                        + $"gold={session.Run.GetResource(StandardRunIds.Gold)} "
                        + $"deck={session.Run.Deck.Count} relics={session.Run.Relics.Count}");
                }

                if (play.CombatDriver?.Current is null && inFight)
                {
                    inFight = false;
                    GD.Print($"  fight ends: hp={session.Run.Health.Current}/{session.Run.Health.Max} "
                        + $"after {turn} turns");
                    turn = 0;
                    NewTurn();
                }

                if (play.CombatDriver is { Current: not null } driver)
                {
                    if (!inFight)
                    {
                        inFight = true;
                        fights++;
                        var enemies = driver.Current!.State.Combatants
                            .Where(c => c.Id != driver.Current.HeroId)
                            .Select(c => $"{c.Id.value}({c.Health.Current})");
                        GD.Print($"  FIGHT {Where(session)} vs {string.Join(" ", enemies)}");
                    }

                    if (driver.PendingOptionChoice is { } options)
                    {
                        var picks = SimPick(rng, options.Count, driver.PendingOptionChoiceCount);
                        GD.Print($"    option {string.Join(",", picks)} of {options.Count}");
                        driver.SupplyOptionChoice(picks);
                    }
                    else if (driver.PendingCardChoice is { } cards)
                    {
                        var picks = SimPick(rng, cards.Count, driver.PendingCardChoiceCount);
                        GD.Print($"    card-choice {string.Join(",", picks.Select(i => cards[i].DefinitionId.value))}");
                        driver.SupplyCardChoice([.. picks.Select(i => cards[i].Id)]);
                    }
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
                        var playable = combat.Hand
                            .Where(c => !refused.Contains(c.Id) && !barren.Contains(c.DefinitionId.value)
                                && CanPay(hero, c.DefinitionId.value))
                            .ToList();
                        var living = combat.State.Combatants
                            .Where(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
                            .ToList();
                        // A random player ends the turn early sometimes — the same hand played to the last
                        // point every time never shows what a held card does on the enemy's turn.
                        CardInstance? card;
                        if (policy is null)
                            card = playable.Count > 0 && rng.NextDouble() > 0.12
                                ? playable[rng.Next(playable.Count)]
                                : null;
                        else
                        {
                            // The best card in hand, and a turn that ends when the best is not worth it.
                            var best = playable
                                .Select(c => (card: c, score: Score(policy, c.DefinitionId.value)))
                                .OrderByDescending(x => x.score)
                                .FirstOrDefault();
                            card = best.card is not null && best.score >= policy.EndTurnBelow ? best.card : null;
                        }
                        if (card is not null)
                        {
                            var target = living.Count == 0 ? (CombatantId?)null
                                : policy is null ? living[rng.Next(living.Count)].Id
                                : rng.NextDouble() < policy.TargetLowestHp
                                    ? living.OrderBy(e => e.Health.Current).First().Id
                                    : living.OrderByDescending(e => e.Health.Current).First().Id;
                            var stepsBefore = combat.Steps.Count;
                            tableBeforeThePlay = TableState(combat);
                            lastPlayed = card.DefinitionId.value;
                            GD.Print($"    play {card.DefinitionId.value} -> {target?.value ?? "—"} "
                                + $"(hp {hero.Health.Current}, hand {combat.Hand.Count})");
                            driver.PlayCard(card.Id, target);
                            foreach (var bad in (driver.Current?.Steps ?? []).Skip(stepsBefore)
                                .Where(s => s.HasProblems))
                            {
                                // Two of these are the engine working, not failing: a card the rules REFUSE
                                // (a random player will try a curse) and a card that PARKS to ask its own
                                // question (the replay model reports the park as a throw, and the prompt the
                                // sim answers next arrives right behind it). Everything else is a finding.
                                var text = string.Join(" | ", bad.Problems);
                                var expected = text.Contains("was not played", StringComparison.Ordinal)
                                    || text.Contains("ReplayParked", StringComparison.Ordinal);
                                if (expected)
                                {
                                    GD.Print($"    (refused/asked: {card.DefinitionId.value})");
                                    continue;
                                }
                                problems++;
                                GD.Print($"    !! PROBLEM playing {card.DefinitionId.value} at {Where(session)}: "
                                    + text);
                            }
                            if (Refused(driver.Current, stepsBefore))
                                refused.Add(card.Id);
                            if (++playsThisTurn >= SimPlaysInATurnNobodyMakes)
                            {
                                reason = $"a turn at {Where(session)} played {playsThisTurn} cards without "
                                    + $"ending — last '{card.DefinitionId.value}'";
                                break;
                            }
                        }
                        else
                        {
                            GD.Print($"    end turn {turn + 1} (hp {hero.Health.Current}, hand {combat.Hand.Count})");
                            driver.EndTurn();
                            NewTurn();
                            if (++turn >= SimTurnsAFightShouldNotNeed)
                            {
                                reason = $"the fight at {Where(session)} did not end in {turn} turns";
                                break;
                            }
                        }
                    }
                    else
                    {
                        reason = $"the fight at {Where(session)} parked on the enemy's turn";
                        break;
                    }
                }
                else if (session.IsAwaitingNodeChoice)
                {
                    var forks = session.PendingNodeChoices;
                    var pick = policy is null
                        ? forks[rng.Next(forks.Count)]
                        : forks.OrderByDescending(n => PathWeight(policy, n)).First();
                    GD.Print($"  fork -> {pick.Id.Value} {MapView.Role(pick)} "
                        + $"(of {string.Join(" ", forks.Select(n => MapView.Role(n)))})");
                    session.PickNode(pick.Id.Value);
                }
                else if (session.IsAwaitingEntities && session.PendingEntities is { } entities)
                {
                    // A skippable offer is skipped now and then, on purpose: a deck that takes every card
                    // and a deck that refuses one are different games.
                    var take = entities.AllowSkip && (policy is null ? rng.NextDouble() < 0.2 : policy.RewardSkip > 0.5)
                        ? []
                        : SimPick(rng, entities.Displays.Count, entities.Count);
                    GD.Print($"  pick [{entities.Purpose}] -> "
                        + (take.Count == 0 ? "skipped" : string.Join(", ", take.Select(i => entities.Displays[i])))
                        + $" (of {entities.Displays.Count})");
                    session.PickEntities(take);
                }
                else if (session.IsAwaitingChoice && session.PendingSituation is { } situation)
                {
                    var choices = session.PendingChoices;
                    var choice = choices[PickChoice(policy, rng, choices)];
                    GD.Print($"  choice [{situation.Id}] -> {choice.Id} "
                        + $"(of {string.Join(" ", choices.Select(c => c.Id))})");
                    session.Pick(choice.Id);
                }
                else if (session.IsAwaitingInterlude)
                    session.Continue();
                else
                {
                    reason = $"nothing at {Where(session)} was awaiting an answer";
                    break;
                }

                if (step == budget - 1)
                    reason = $"the step budget ran out at {Where(session)}";
            }
        }
        catch (Exception ex)
        {
            crash = ex.ToString();
            reason = $"an exception escaped at {(session is null ? "—" : Where(session))}";
            GD.Print($"!! CRASH {crash}");
        }

        var error = session?.Error ?? Play?.Error ?? "none";
        var byAct = rooms.GroupBy(r => r.Split(':')[0]).Select(g =>
            $"act {g.Key}: {g.Count()} rooms ({string.Join(" ", g.GroupBy(x => x.Split(':')[1]).Select(k => $"{k.Key}×{k.Count()}"))})");
        foreach (var line in byAct)
            GD.Print($"  {line}");
        // The fitness line names no act of its own any more. It used to answer one question — "what did the
        // act-III boss cost to reach?" — and Act IV made that the wrong question by not being the last act.
        // What it states instead is the DEEPEST act whose boss room the run entered, plus the whole per-act
        // table; whoever is measuring says which act they are measuring to (tools/train.py --target-act).
        var deepestActBoss = hpAtActBoss.Count == 0 ? 0 : hpAtActBoss.Keys.Max();
        GD.Print($"sim-fitness: policy={policy?.Name ?? "random"} seed={seed} "
            + $"deepestActBoss={deepestActBoss} "
            + $"damageTaken={damageTaken} healed={healed} "
            + $"actBossDamage={string.Join(",", damageAtActBoss.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"))} "
            + $"actBossHp={string.Join(",", hpAtActBoss.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"))} "
            + $"rooms={rooms.Count} result={session?.Run.Result}");
        GD.Print($"sim-result: seed={seed} result={session?.Run.Result} acts={acts} rooms={rooms.Count} "
            + $"fights={fights} hp={session?.Run.Health.Current}/{session?.Run.Health.Max} "
            + $"problems={problems} error={error} seconds={clock.Elapsed.TotalSeconds:0.0} "
            + $"stopped because {reason}");

        // A lost run is a NORMAL outcome and exits clean; only something the run could not answer for —
        // an engine error, a refused play, a wall, a thrown exception — is worth the batch's attention.
        var clean = crash.Length == 0 && error == "none" && problems == 0
            && (session?.IsComplete ?? false);
        GetTree().Quit(clean ? 0 : 1);
    }

    // Which door a runner takes. A shop is answered as a shop — how eagerly it spends is a weight of its
    // own — and every other situation by one knob: the first option, the last, or somewhere in between.
    private static int PickChoice(SimPolicy? policy, Random rng, IReadOnlyList<EventChoice> choices)
    {
        if (policy is null)
            return rng.Next(choices.Count);
        var buys = Enumerable.Range(0, choices.Count)
            .Where(i => choices[i].Id.StartsWith("buy-", StringComparison.Ordinal)).ToList();
        var leave = choices.ToList().FindIndex(c => c.Id == "leave");
        if (leave >= 0)
            return buys.Count > 0 && rng.NextDouble() < policy.ShopBuy ? buys[rng.Next(buys.Count)] : leave;
        return Math.Clamp((int)Math.Round(policy.EventLate * (choices.Count - 1)), 0, choices.Count - 1);
    }

    // `count` distinct indices out of `available`, in random order (never more than there are).
    private static List<int> SimPick(Random rng, int available, int count)
    {
        var pool = Enumerable.Range(0, available).ToList();
        var picked = new List<int>();
        for (var i = 0; i < count && pool.Count > 0; i++)
        {
            var at = rng.Next(pool.Count);
            picked.Add(pool[at]);
            pool.RemoveAt(at);
        }
        return picked;
    }

    // ── the trained runner ────────────────────────────────────────────────────────────────────────
    // A policy is what makes one runner different from another: a handful of weights that decide which
    // card is worth playing, when a turn is over, which enemy to hit, which way to walk and what to buy.
    // No policy = the dice player above. `--sim-policy <file.json>` loads one; `tools/train.py` breeds them.
    // The fitness they are bred for is the balance question itself: starting at 9999 hp, how much health
    // does the whole game take off a runner on its way to the act-III boss?
    public sealed class SimPolicy
    {
        public string Name { get; set; } = "unnamed";
        // What a card is worth, per thing its program does (counted once from the document).
        public double WDamage { get; set; }
        public double WBlock { get; set; }
        public double WStatus { get; set; }
        public double WDraw { get; set; }
        public double WResource { get; set; }
        public double WCost { get; set; }
        public double EndTurnBelow { get; set; }      // a hand whose best card scores under this is done
        public double TargetLowestHp { get; set; }    // 1 = finish the weakest, 0 = hit the strongest
        // Which room to walk into, by the role the map generated it for.
        public double PathCombat { get; set; }
        public double PathElite { get; set; }
        public double PathShop { get; set; }
        public double PathRest { get; set; }
        public double PathEvent { get; set; }
        public double PathTreasure { get; set; }
        public double RewardSkip { get; set; }        // > 0.5: decline what may be declined
        public double ShopBuy { get; set; }           // how eagerly gold is spent
        public double EventLate { get; set; }         // 0 = always the first door, 1 = always the last

        public static SimPolicy? Load(string path)
        {
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file is null)
                return null;
            return System.Text.Json.JsonSerializer.Deserialize<SimPolicy>(file.GetAsText(),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
    }

    private static SimPolicy? _policy;

    private static string? SimStringArg(string name)
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    // What each card DOES, read once out of the shipped document: how often its program deals damage,
    // raises a guard, puts something on somebody, draws, or pays. The policy weighs these counts.
    private static Dictionary<string, double[]>? _cardFeatures;

    private static double[] Features(string cardId)
    {
        if (_cardFeatures is null)
        {
            _cardFeatures = new Dictionary<string, double[]>(StringComparer.Ordinal);
            var document = Godot.FileAccess.GetFileAsString("res://content/game.roguedeck.json");
            using var json = System.Text.Json.JsonDocument.Parse(document);
            if (json.RootElement.TryGetProperty("Cards", out var cards))
                foreach (var card in cards.EnumerateArray())
                {
                    var id = card.GetProperty("Id").GetString() ?? "";
                    var text = card.GetRawText();
                    int Count(string kind)
                    {
                        var found = 0;
                        for (var at = text.IndexOf(kind, StringComparison.Ordinal); at >= 0;
                             at = text.IndexOf(kind, at + 1, StringComparison.Ordinal))
                            found++;
                        return found;
                    }
                    _cardFeatures[id] =
                    [
                        Count("node.dealDamage"),
                        Count("node.gainBlock") + Count("node.modifyDefensivePool"),
                        Count("node.applyStatus") + Count("node.modifyStatusStacks"),
                        Count("node.drawCards") + Count("node.moveCardToZone"),
                        Count("node.gainResource"),
                    ];
                }
        }
        return _cardFeatures.TryGetValue(cardId, out var features) ? features : [0, 0, 0, 0, 0];
    }

    private double Score(SimPolicy policy, string cardId)
    {
        var f = Features(cardId);
        var cost = FullCosts(cardId).Sum(c => c.Amount);
        return policy.WDamage * f[0] + policy.WBlock * f[1] + policy.WStatus * f[2]
            + policy.WDraw * f[3] + policy.WResource * f[4] + policy.WCost * cost;
    }

    // The role weight of a room, so a runner can prefer elites (more spoils, more damage) or avoid them.
    private static double PathWeight(SimPolicy policy, RogueDeck.Run.Node node) => MapView.Role(node) switch
    {
        "elite" => policy.PathElite,
        "shop" => policy.PathShop,
        "rest" => policy.PathRest,
        "event" => policy.PathEvent,
        "treasure" => policy.PathTreasure,
        _ => policy.PathCombat,
    };
}
