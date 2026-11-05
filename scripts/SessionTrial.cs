using Godot;
using RogueDeck.Run;

namespace BnbGodot;

public partial class SessionScreen
{
    // `--smoke-trial [--trial-act=N]`: walk the encounter trial the way the tutorial probe walks a lesson — the simplest answer the
    // screen offers, first card that can be paid — and when a fight is lost, press "Retry this fight". It passes
    // once the first stage, its package and a fight past it are written down, with the trial saved in its own slot
    // and the player's run save untouched.
    private async System.Threading.Tasks.Task SmokeTrial()
    {
        var host = GameHost.Instance;
        var saveStamp = Godot.FileAccess.GetModifiedTime("user://run-save.json");
        Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TrialLog.LogPath));
        var stageOne = host.Play?.Session?.Run.Map.Nodes.SkipWhile(n => n.Payload is EventRef)
            .TakeWhile(n => n.Payload is EncounterRef).Count() ?? 0;
        var retried = 0;
        int Logged() => Godot.FileAccess.FileExists(TrialLog.LogPath)
            ? Godot.FileAccess.GetFileAsString(TrialLog.LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
            : 0;
        int Won() => Godot.FileAccess.FileExists(TrialLog.LogPath)
            ? Godot.FileAccess.GetFileAsString(TrialLog.LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(l => l.Contains("\"won\":true", StringComparison.Ordinal))
            : 0;
        // Past stage one, a fight logged is the proof: won or lost. The walk is the simplest player there is, and
        // Act IV's first fights past it (Sobek's Tithe, 2026-11-05) outlast it; the walk is plumbing, not balance.
        for (var step = 0; step < 6000 && !(Won() >= stageOne && Logged() > stageOne); step++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Session is not { } session || Play is not { } play)
                break;
            if (session.IsComplete)
            {
                if (session.Run.Result == RunResult.Defeat && retried < 40 && host.ResumeTrial(host.TrialAct, retry: true))
                {
                    retried++;
                    continue;
                }
                break;
            }
            if (play.Seat is { Current: not null } driver)
            {
                if (driver.PendingOptionChoice is { } options)
                    driver.SupplyOptionChoice([.. Enumerable.Range(0, Math.Min(driver.PendingOptionChoiceCount, options.Count))]);
                else if (driver.PendingCardChoice is { } cards)
                    driver.SupplyCardChoice([.. cards.Take(driver.PendingCardChoiceCount).Select(c => c.Id)]);
                else if (driver.Current!.IsHeroTurn)
                {
                    var combat = driver.Current;
                    var hero = combat.State.GetCombatant(combat.HeroId);
                    // Payable as printed AND playable as this copy stands: a copy priced up by a rule (the Chain's
                    // remembered volume) is refused, and a refused card left first in hand would be tried forever.
                    // …and payable with Burdened's surcharge on top (Act IV): a card priced past the Energy left would be
                    // refused, and tried forever.
                    var burdened = hero.Statuses.Any(st => st.DefinitionId.value == BurdenedStatus);
                    var energy = hero.Resources.TryGetValue(RogueDeck.Core.Combat.StandardCombatIds.EnergyResource, out var pool) ? pool.Current : 0;
                    var card = combat.Hand.FirstOrDefault(c => CanPay(hero, c.DefinitionId.value) && combat.CanPlay(c.Id)
                        && (!burdened || EffectiveCost(hero, c.DefinitionId.value) <= energy));
                    // The weakest enemy standing: a walk that always struck the first body would feed a Cairn every
                    // stone it throws back (Act III, 2026-11-03).
                    // A Downed body (Act IV) takes no damage until it rises, so it is not worth a card.
                    var target = combat.State.Combatants.Where(c => c.Id != combat.HeroId && c.IsAlive
                        && c.TeamId == RogueDeck.Core.Combat.StandardCombatIds.EnemyTeam
                        && !c.Statuses.Any(s => s.DefinitionId.value == "risen_downed"))
                        .OrderBy(c => c.Health.Current).FirstOrDefault()?.Id;
                    // Nothing standing that can be hurt (every body Downed): end the turn and let them rise.
                    if (card is not null && target is not null)
                        driver.PlayCard(card.Id, target);
                    else
                    {
                        driver.EndTurn();
                        host.AutoSave();
                    }
                }
            }
            else if (session.IsAwaitingNodeChoice)
            {
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
                host.AutoSave();
            }
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else if (session.IsAwaitingEntities)
                session.PickEntities([0]);
            else if (session.IsAwaitingChoice)
                session.Pick(session.PendingChoices[0].Id);
            Rebuild();
        }
        var ok = Won() >= stageOne && Logged() > stageOne && host.HasTrialSave(host.TrialAct)
            && Godot.FileAccess.GetModifiedTime("user://run-save.json") == saveStamp;
        GD.Print($"smoke-trial: logged {Logged()} fights ({Won()} won, stage one is {stageOne}), retried {retried}, "
            + $"act {host.TrialAct}, trial saved={host.HasTrialSave(host.TrialAct)}, the player's save "
            + $"{(Godot.FileAccess.GetModifiedTime("user://run-save.json") == saveStamp ? "untouched" : "WRITTEN")} "
            + $"· deck {Session?.Run.Deck.Count} · {(ok ? "PASS" : "FAIL")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
