using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

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
        var nodes = host.Play?.Session?.Run.Map.Nodes ?? [];
        var stageOne = nodes.SkipWhile(n => n.Payload is EventRef).TakeWhile(n => n.Payload is EncounterRef).Count();
        // An act without stages (Act V: the catch-up, then the six gods and no package between them) has no
        // "stage one and a fight past it"; its probe is the first god won and the next one fought.
        if (!nodes.SkipWhile(n => n.Payload is EventRef).Any(n => n.Payload is EventRef))
            stageOne = 1;
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
            SmokeTrialStep(host, session, play);
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

    // One answer of the trial walk: the simplest the screen offers, the first card that can be paid.
    private void SmokeTrialStep(GameHost host, InteractiveRunSession session, RunPlayback play)
    {
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
                // …and never a card that lends against tomorrow and comes straight back (Nanshe's Take From Tomorrow,
                // tagged `borrow`): first in hand and free, it would be played for ever.
                var card = combat.Hand.FirstOrDefault(c => CanPay(hero, c.DefinitionId.value) && combat.CanPlay(c.Id)
                    && (!burdened || EffectiveCost(hero, c.DefinitionId.value) <= energy)
                    // …and payable as the rules price it now: Enlil's No Word Shall Be Without Price puts 1 on a
                    // free card, and a refused card left first in hand would be tried for ever.
                    && PricedNow(combat, hero, c) <= energy
                    && !(combat.Registry.CardDefinitions.TryGetValue(c.DefinitionId, out var definition)
                        && definition.Tags.Contains(new RogueDeck.Core.Combat.TagId("borrow"))));
                // The weakest enemy standing: a walk that always struck the first body would feed a Cairn every
                // stone it throws back (Act III, 2026-11-03).
                // A Downed body (Act IV) takes no damage until it rises, so it is not worth a card.
                // A body held at 1 HP by a rule (Nisaba's Indelible, tagged `cannot_fall`) is not worth one either,
                // while anything else stands; a body that must be broken in time (her Final Line, `break_first`) is.
                var standing = combat.State.Combatants.Where(c => c.Id != combat.HeroId && c.IsAlive
                    && c.TeamId == RogueDeck.Core.Combat.StandardCombatIds.EnemyTeam
                    && !c.Statuses.Any(s => s.DefinitionId.value == "risen_downed")).ToList();
                bool Tagged(RogueDeck.Core.Combat.CombatantState c, string tag) =>
                    c.Statuses.Any(s => s.Tags.Contains(new RogueDeck.Core.Combat.TagId(tag)));
                var target = (standing.FirstOrDefault(c => Tagged(c, "break_first"))
                    ?? standing.Where(c => !(c.Health.Current <= 1 && Tagged(c, "cannot_fall"))).OrderBy(c => c.Health.Current).FirstOrDefault()
                    ?? standing.OrderBy(c => c.Health.Current).FirstOrDefault())?.Id;
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
    }

    private static int PricedNow(RogueDeck.Scenario.Scripting.ICombatView combat, RogueDeck.Core.Combat.CombatantState hero,
        RogueDeck.Core.Combat.CardInstance card) =>
        combat.Registry.CardDefinitions.TryGetValue(card.DefinitionId, out var definition)
            ? RogueDeck.Core.Combat.CombatCardPlayProcessor.PreviewCosts(combat.State, combat.Registry, definition, hero, null, card.Id)
                .Where(c => c.ResourceId == RogueDeck.Core.Combat.StandardCombatIds.EnergyResource).Sum(c => c.Amount)
            : 0;

    // `--smoke-trial-encounter [--trial-act=N] [--trial-encounter=<id>]`: one fight of a trial on its own (by default
    // the act's first stage-2 normal fight, or for an act without stages its first fight), walked like the full trial — its packages, then the fight — and
    // fought once more with "Retry this fight" once it is over, won or lost. It passes when both fights are
    // written down in the single-encounter notebook with "mode": "encounter" and the encounter's id, the walk
    // saved to its own slot on the way, and the full trial's notebook and save and the player's run save untouched.
    private async System.Threading.Tasks.Task SmokeTrialEncounter()
    {
        var host = GameHost.Instance;
        var act = host.TrialAct;
        var encounter = host.TrialEncounter;
        long Stamp(string path) => Godot.FileAccess.FileExists(path) ? (long)Godot.FileAccess.GetModifiedTime(path) : -1;
        var untouched = new[] { "user://run-save.json", TrialLog.SavePath(act), TrialLog.PrefightPath(act), TrialLog.LogPath };
        var stamps = untouched.Select(Stamp).ToList();
        Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TrialLog.EncounterLogPath));
        var packages = (host.Play?.Session?.Run.Map.Nodes.Count ?? 1) - 1;
        string[] Lines() => Godot.FileAccess.FileExists(TrialLog.EncounterLogPath)
            ? Godot.FileAccess.GetFileAsString(TrialLog.EncounterLogPath).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : [];
        // The act's sheet, as the title opens it: the full trial and every fight of the act, one button each.
        var panel = new TrialPanel(host, act, () => { }, () => { });
        AddChild(panel);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var buttons = panel.FindChildren("*", nameof(Button), owned: false).Select(n => n.Name.ToString()).ToList();
        var listed = buttons.Count(n => n.StartsWith("TrialEncounter_", StringComparison.Ordinal));
        var sheet = buttons.Contains("FullTrialButton") && listed == host.TrialSingles(act).Count
            && buttons.Contains($"TrialEncounter_{encounter}");
        panel.QueueFree();
        var saved = false;
        var retried = 0;
        for (var step = 0; step < 6000 && Lines().Length < 2; step++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Session is not { } session || Play is not { } play)
                break;
            saved |= host.HasTrialSave(act, single: true);
            if (session.IsComplete)
            {
                // Over, won or lost: fight it again once, from its first turn.
                if (Lines().Length == 1 && retried == 0 && host.ResumeTrial(act, retry: true, encounter))
                {
                    retried++;
                    continue;
                }
                break;
            }
            SmokeTrialStep(host, session, play);
            Rebuild();
        }
        var lines = Lines();
        var records = lines.Select(l => System.Text.Json.Nodes.JsonNode.Parse(l)!).ToList();
        var marked = records.Count(r => (string?)r["mode"] == "encounter" && (string?)r["encounter"] == encounter && (int?)r["act"] == act);
        var changed = untouched.Where((path, i) => Stamp(path) != stamps[i]).ToList();
        var ok = sheet && lines.Length == 2 && marked == 2 && retried == 1 && saved && changed.Count == 0 && host.IsTrialEncounter;
        GD.Print($"smoke-trial-encounter: act {act} {encounter} after {packages} package(s) · sheet lists {listed} of "
            + $"{host.TrialSingles(act).Count} fights{(sheet ? "" : " (WRONG)")} · logged {lines.Length} fights "
            + $"({records.Count(r => (bool?)r["won"] == true)} won, {marked} marked encounter), retried {retried}, own slot saved={saved}, "
            + $"untouched: {(changed.Count == 0 ? "all" : "WRITTEN " + string.Join(", ", changed))} "
            + $"· deck {Session?.Run.Deck.Count} · {(ok ? "PASS" : "FAIL")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
