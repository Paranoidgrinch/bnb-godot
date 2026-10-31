using Godot;
using RogueDeck.Run;

namespace BnbGodot;

public partial class SessionScreen
{
    // `--smoke-trial`: walk the encounter trial the way the tutorial probe walks a lesson — the simplest answer the
    // screen offers, first card that can be paid — and when a fight is lost, press "Retry this fight". It passes
    // once the first stage, its package and a fight past it are written down, with the trial saved in its own slot
    // and the player's run save untouched.
    private async System.Threading.Tasks.Task SmokeTrial()
    {
        var host = GameHost.Instance;
        var saveStamp = Godot.FileAccess.GetModifiedTime("user://run-save.json");
        Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TrialLog.LogPath));
        var stageOne = host.Play?.Session?.Run.Map.Nodes.TakeWhile(n => n.Payload is EncounterRef).Count() ?? 0;
        var retried = 0;
        int Logged() => Godot.FileAccess.FileExists(TrialLog.LogPath)
            ? Godot.FileAccess.GetFileAsString(TrialLog.LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
            : 0;
        int Won() => Godot.FileAccess.FileExists(TrialLog.LogPath)
            ? Godot.FileAccess.GetFileAsString(TrialLog.LogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(l => l.Contains("\"won\":true", StringComparison.Ordinal))
            : 0;
        for (var step = 0; step < 6000 && Won() <= stageOne; step++)
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
                    var card = combat.Hand.FirstOrDefault(c => CanPay(hero, c.DefinitionId.value));
                    var target = combat.State.Combatants.FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive
                        && c.TeamId == RogueDeck.Core.Combat.StandardCombatIds.EnemyTeam)?.Id;
                    if (card is not null)
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
        var ok = Won() > stageOne && host.HasTrialSave(1)
            && Godot.FileAccess.GetModifiedTime("user://run-save.json") == saveStamp;
        GD.Print($"smoke-trial: logged {Logged()} fights ({Won()} won, stage one is {stageOne}), retried {retried}, "
            + $"trial saved={host.HasTrialSave(1)}, the player's save "
            + $"{(Godot.FileAccess.GetModifiedTime("user://run-save.json") == saveStamp ? "untouched" : "WRITTEN")} "
            + $"· deck {Session?.Run.Deck.Count} · {(ok ? "PASS" : "FAIL")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
