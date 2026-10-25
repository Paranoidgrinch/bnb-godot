using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Reporting;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// ── THE FIGHT'S LOG: WHAT WAS PLAYED, AND WHAT IT DID, IN THE ENGINE'S ORDER ─────────
// The log used to be the run's own entries, which say nothing about a fight. It is now written from the same trace
// the rising numbers are read from (SessionEffects): one heading per step — a card, an action, the end of the turn,
// an enemy's move — and under it every wound, block, mend, status and resource change that step caused, in the
// order the engine resolved them, with the modifiers that changed a blow. The markers the engine keeps for its own
// conditions ("Struck Last Round") are not chips on the bodies (playtest 3, B5); they are written here, where a
// player who wants to know why a card did what it did can read it.
public partial class SessionScreen
{
    private readonly List<string> _fightLog = [];
    private int _fightLogRound;

    // How many lines the panel keeps: a long boss fight is a few hundred.
    private const int FightLogLines = 600;

    private void WriteFightLog(ICombatView combat, IReadOnlyList<ScenarioStepReport> steps, bool fresh)
    {
        if (fresh)
        {
            _fightLog.Clear();
            _fightLogRound = 0;
            var enemies = combat.State.Combatants.Where(c => c.Id != combat.HeroId && c.TeamId == StandardCombatIds.EnemyTeam);
            _fightLog.Add($"[b]Fight: {string.Join(", ", enemies.Select(e => Name(e, combat)))}[/b]");
        }
        foreach (var step in steps)
        {
            if (step.Round > _fightLogRound)
            {
                _fightLogRound = step.Round;
                _fightLog.Add("");
                _fightLog.Add($"[color=#{MoonvineTheme.Signal.ToHtml(false)}]── Round {step.Round} ──[/color]");
            }
            if (Heading(combat, step) is { } heading)
                _fightLog.Add(heading);
            foreach (var line in Consequences(combat, step))
                _fightLog.Add("    " + line);
            foreach (var problem in step.Problems)
                _fightLog.Add($"    [color=#{MoonvineTheme.TextMuted.ToHtml(false)}]{Escape(problem)}[/color]");
        }
        if (_fightLog.Count > FightLogLines)
            _fightLog.RemoveRange(0, _fightLog.Count - FightLogLines);
    }

    private string? Heading(ICombatView combat, ScenarioStepReport step) => step.Step switch
    {
        HeroPlaysCard play => $"[b]{BodyName(combat, combat.HeroId.value)}[/b] play [b]{Escape(CardName(play.CardId))}[/b]"
            + (play.TargetId is { } target && target != combat.HeroId.value ? $" → {BodyName(combat, target)}" : ""),
        HeroUsesAction use => $"[b]{BodyName(combat, combat.HeroId.value)}[/b] use [b]{Escape(CardName(use.ActionId))}[/b]"
            + (use.TargetId is { } target && target != combat.HeroId.value ? $" → {BodyName(combat, target)}" : ""),
        HeroUsesConsumable => $"[b]{BodyName(combat, combat.HeroId.value)}[/b] use a consumable",
        HeroEndsTurn => "[i]End of turn[/i]",
        EnemyActs act => $"[b]{BodyName(combat, act.EnemyId)}[/b]: {Escape(step.Intent?.Label ?? Humanized(act.ActionId.Split('.').Last()))}",
        AdvanceToNextRound => step.Trace.Count == 0 ? null : "[i]Start of turn[/i]",
        _ => null,
    };

    // WHY A THING HAPPENED: each effect is enqueued on a chain, and a chain started by a trigger names it
    // (EffectEnqueuedTraceEvent.TriggerId). The queue resolves the effects later — after every trigger of the event
    // has been asked — so the order of the trace says nothing about the cause; the chain does. What an effect did is
    // traced while it resolves, and its EffectResolved closes it: the lines gathered since then are its own.
    // A trigger that fired and enqueued nothing is not mentioned at all.
    private IEnumerable<string> Consequences(ICombatView combat, ScenarioStepReport step)
    {
        var causes = new Dictionary<long, string>();
        var pending = new List<string>();
        foreach (var trace in step.Trace)
        {
            switch (trace)
            {
                case EffectEnqueuedTraceEvent { TriggerId: { } trigger } queued:
                    causes.TryAdd(queued.ChainId, trigger);
                    break;
                case EffectResolvedTraceEvent resolved:
                    var cause = causes.TryGetValue(resolved.ChainId, out var id) ? TriggerName(combat, id) : null;
                    foreach (var line in pending)
                        yield return cause is null ? line : $"[color=#{MoonvineTheme.Accent.ToHtml(false)}]{cause}:[/color] {line}";
                    pending.Clear();
                    break;
                default:
                    if (Consequence(combat, trace) is { } said)
                        pending.Add(said);
                    break;
            }
        }
        foreach (var line in pending)
            yield return line;
    }

    private string? Consequence(ICombatView combat, CombatTraceEvent trace)
    {
        switch (trace)
        {
            case DamageResolvedTraceEvent hit when hit.AmountAfterModifiers > 0 || hit.HealthLost > 0:
            {
                var steps = hit.ModifierSteps.Where(m => m.Before != m.After)
                    .Select(m => $"{Humanized(m.ModifierId)} {m.Before}→{m.After}").ToList();
                var how = steps.Count > 0 ? $" ({Escape(string.Join(", ", steps))})" : "";
                var blocked = hit.BlockedAmount <= 0 ? ""
                    : hit.HealthLost == 0 ? ", all blocked"
                    : $", {hit.BlockedAmount} blocked";
                var who = hit.TargetCombatantId.value;
                return $"{BodyName(combat, who)} {Verb(combat, who, "take")} {hit.AmountAfterModifiers}{how}{blocked}"
                    + $" → {hit.HealthAfter} HP" + (hit.HealthBefore > 0 && hit.HealthAfter <= 0 ? " — [b]down[/b]" : "");
            }
            case BlockGainResolvedTraceEvent guard when guard.BlockAfter != guard.BlockBefore:
                return $"{BodyName(combat, guard.TargetCombatantId.value)} {Verb(combat, guard.TargetCombatantId.value, "gain")}"
                    + $" {guard.BlockAfter - guard.BlockBefore} Block → {guard.BlockAfter}";
            case HealResolvedTraceEvent mend when mend.HealedAmount > 0:
                return $"{BodyName(combat, mend.TargetCombatantId.value)} {Verb(combat, mend.TargetCombatantId.value, "heal")}"
                    + $" {mend.HealedAmount} → {mend.HealthAfter} HP";
            case MaxHealthChangeResolvedTraceEvent max when max.AppliedDelta != 0:
                return $"{BodyName(combat, max.TargetCombatantId.value)}: max HP {Signed(max.AppliedDelta)} → {max.NewMax}";
            case StatusApplicationResolvedTraceEvent status:
            {
                var name = StatusLabel(combat, status.StatusDefinitionId.value);
                var amount = status.RequestedStacks != 0 ? status.RequestedStacks : status.RequestedDurationTurns;
                var said = status.Outcome switch
                {
                    StatusApplicationOutcome.BlockedByInterceptor => $"{name} is stopped",
                    StatusApplicationOutcome.ReplacedByInterceptor => $"{name} is replaced",
                    _ => $"{name} {Signed(amount)}" + (status.ResultingStacks > 0 ? $" (now {status.ResultingStacks})" : ""),
                };
                return $"{BodyName(combat, status.TargetCombatantId.value)}: {said}";
            }
            case ResourceChangeResolvedTraceEvent resource when resource.AppliedDelta != 0
                && resource.Kind != ResourceChangeKind.Refilled:
                return $"{BodyName(combat, resource.CombatantId.value)}: {Escape(ResourceLabel(resource.ResourceId).Trim())}"
                    + $" {Signed(resource.AppliedDelta)} → {resource.NewCurrent}";
            case RandomTargetsSelectedTraceEvent random when random.SelectedTargetIds.Count > 0:
                return $"[color=#{MoonvineTheme.TextMuted.ToHtml(false)}]at random: "
                    + string.Join(", ", random.SelectedTargetIds.Select(id => BodyName(combat, id.value))) + "[/color]";
            default:
                return null;
        }
    }

    // A trigger's id is `<status>_trigger<n>` (StatusDataRebuild) or `encounter_<id>_trigger<n>`; a relic's rule is a
    // status named after the relic. Named by what the player knows: the relic, the status, or the fight.
    private string TriggerName(ICombatView combat, string triggerId)
    {
        var cut = triggerId.LastIndexOf("_trigger", StringComparison.Ordinal);
        var owner = cut > 0 ? triggerId[..cut] : triggerId;
        if (owner.StartsWith("encounter_", StringComparison.Ordinal))
            return "Fight rule";
        if (Session?.Run.Relics.FirstOrDefault(r => owner == r.Id.Value || owner.StartsWith(r.Id.Value + "_", StringComparison.Ordinal))
            is { } relic)
            return Escape(relic.Definition.DisplayName);
        return StatusLabel(combat, owner);
    }

    // "You take", "Clerk takes".
    private static string Verb(ICombatView combat, string id, string verb) => id == combat.HeroId.value ? verb : verb + "s";

    private string BodyName(ICombatView combat, string id) =>
        combat.State.Combatants.FirstOrDefault(c => c.Id.value == id) is { } body
            ? Escape(body.Id == combat.HeroId ? "You" : Name(body, combat))
            : Escape(Humanized(id));

    private static string StatusLabel(ICombatView combat, string id)
    {
        StatusDefinition? definition = null;
        combat.State.DefinitionRegistry?.TryGetStatus(new StatusDefinitionId(id), out definition);
        return Escape(definition is { DisplayNameKey.Length: > 0 } ? definition.DisplayNameKey : Humanized(id));
    }

    private static string Signed(int n) => n > 0 ? $"+{n}" : n.ToString();

    // BBCode: a "[" in a name or a problem would open a tag.
    private static string Escape(string text) => text.Replace("[", "[lb]");

    // What the log panel shows: the fight's log in a fight; between rooms the last fight's log, then the run's entries.
    private string LogText(InteractiveRunSession session)
    {
        var run = Escape(string.Join("\n", session.Run.Log.TakeLast(60).Select(entry => entry.Message)));
        if (_fightLog.Count == 0)
            return run;
        return Play?.Seat?.Current is not null ? string.Join("\n", _fightLog) : string.Join("\n", _fightLog) + "\n\n" + run;
    }

    // `--smoke-log`: the first fight, three rounds of every card that can be played, then the log printed and
    // photographed open.
    private async System.Threading.Tasks.Task SmokeLog()
    {
        var session = Session;
        for (var i = 0; i < 8 && Play?.Seat?.Current is null && session is not null; i++)
        {
            if (session.IsAwaitingNodeChoice) session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude) session.Continue();
            else break;
        }
        if (Play?.Seat is not { Current: not null } driver)
        {
            GD.Print("smoke-log: no fight reached");
            GetTree().Quit(1);
            return;
        }
        for (var round = 0; round < 3 && driver.Current is { IsHeroTurn: true }; round++)
        {
            for (var plays = 0; plays < 10; plays++)
            {
                var combat = driver.Current!;
                var hero = combat.State.GetCombatant(combat.HeroId);
                if (!combat.IsHeroTurn || combat.Hand.FirstOrDefault(c => combat.CanPlay(c.Id) && CanPay(hero, c.DefinitionId.value))
                        is not { } card)
                    break;
                var target = combat.State.Combatants
                    .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
                driver.PlayCard(card.Id, target);
                for (var asked = 0; asked < 5; asked++)
                {
                    if (driver.PendingOptionChoice is not null) driver.SupplyOptionChoice([0]);
                    else if (driver.PendingCardChoice is { } cards)
                        driver.SupplyCardChoice([.. cards.Take(driver.PendingCardChoiceCount).Select(c => c.Id)]);
                    else break;
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (driver.Current is not { IsHeroTurn: true })
                break;
            EndTurnNow();
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
        }
        foreach (var line in _fightLog)
            GD.Print("log| " + new System.Text.RegularExpressions.Regex(@"\[/?[a-z]+(=[^\]]*)?\]").Replace(line, "").Replace("[lb]", "["));
        _logPanel.Visible = true;
        await CaptureThenQuit("smoke-log.png");
    }
}
