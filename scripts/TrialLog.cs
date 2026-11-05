using System.Text.Json;
using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace BnbGodot;

// ── THE ENCOUNTER TRIAL'S NOTEBOOK (bnb-content EncounterTrial) ──────────────────────────────────────────────
// Every fight of a trial, written down as it ends: one JSON line in user://trial-log.jsonl — which encounter,
// won or lost, the health it cost out of how much, how many rounds it took, and the deck and relics it was
// fought with. That file is what the balance is read from afterwards (bnb-content tools/trial_report.py).
//
// It also keeps the trial's RETRY: the moment a fight opens, the run is saved aside (user://trial-prefight-<act>.json),
// so a lost fight can be fought again from its first turn instead of ending the trial.
//
// ⚠ Read from the fight's END, keyed by where it happened, like RunTally — and, like RunTally, on the engine's own
// change callback, not on the deferred redraw: a fight won and walked on from in one answer has left the run's
// history (and its log) by the time a redraw looks.
//
// A SINGLE-ENCOUNTER TRIAL (GameHost.TrialEncounter) keeps a notebook of its own, user://trial-encounter-log.jsonl:
// the same lines, plus "mode": "encounter" and the stage of the fight; and a save and retry of its own per act
// (trial-encounter-save-<act>.json, trial-encounter-prefight-<act>.json, with the encounter its save holds in
// trial-encounter-<act>.txt), so it never touches a full trial in progress. A key holds the result too, so a fight lost and then retried
// and won is written down twice: both happened.
public static class TrialLog
{
    // A probe (`--smoke-trial`) writes beside the player's files, never into them.
    public static string Slot => OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--smoke", StringComparison.Ordinal)) ? "-smoke" : "";

    public static string LogPath => $"user://trial-log{Slot}.jsonl";

    public static string EncounterLogPath => $"user://trial-encounter-log{Slot}.jsonl";

    public static string PrefightPath(int act, bool single = false) =>
        single ? $"user://trial-encounter-prefight-{act}{Slot}.json" : $"user://trial-prefight-{act}{Slot}.json";

    public static string SavePath(int act, bool single = false) =>
        single ? $"user://trial-encounter-save-{act}{Slot}.json" : $"user://trial-save-{act}{Slot}.json";

    public static string EncounterOfSavePath(int act) => $"user://trial-encounter-{act}{Slot}.txt";

    private static object? _play;
    private static readonly HashSet<string> Written = new(StringComparer.Ordinal);
    private static string? _openedAt;
    // The rounds of the fight in progress (one fight at a time: the trial is a straight line).
    private static int _rounds;

    public static string LogFile => ProjectSettings.GlobalizePath(LogPath);

    public static string EncounterLogFile => ProjectSettings.GlobalizePath(EncounterLogPath);

    // The notebook the trial in play writes to.
    public static string LogPathOf(GameHost host) => host.IsTrialEncounter ? EncounterLogPath : LogPath;

    public static string LogFileOf(GameHost host) => ProjectSettings.GlobalizePath(LogPathOf(host));

    public static void Observe(GameHost host)
    {
        var play = host.Play;
        if (!ReferenceEquals(play, _play))
        {
            _play = play;
            Written.Clear();
            _openedAt = null;
            _rounds = 0;
        }
        if (play?.Session?.Run is not { } run)
            return;

        // A fight just opened: put the run aside, so it can be taken up again from here.
        if (play.Seat?.Current is { IsOver: false } live)
        {
            var where = $"{run.ActNumber}:{run.CurrentNodeId?.Value}";
            _rounds = Math.Max(_rounds, live.State.CurrentRound);
            if (_openedAt != where)
            {
                _openedAt = where;
                if (live.State.CurrentRound <= 1 && play.SaveJson() is { } json)
                    using (var file = Godot.FileAccess.Open(PrefightPath(host.TrialAct, host.IsTrialEncounter), Godot.FileAccess.ModeFlags.Write))
                        file?.StoreString(json);
            }
        }

        var nth = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fought in run.EventHistory.OfType<CombatResolvedRunEvent>())
        {
            var room = $"{run.ActNumber}:{fought.NodeId.Value}";
            var key = $"{run.RandomSeed}|{room}#{nth[room] = nth.GetValueOrDefault(room) + 1}|{fought.Result}|{fought.DamageTaken}";
            if (!Written.Add(key))
                continue;
            var node = run.Map.Nodes.FirstOrDefault(n => n.Id == fought.NodeId);
            var line = new
            {
                at = DateTime.Now.ToString("s"),
                seed = run.RandomSeed,
                act = host.TrialAct,
                node = fought.NodeId.Value,
                encounter = (node?.Payload as EncounterRef)?.Id.Value,
                kind = fought.Tags?.FirstOrDefault(),
                won = fought.Result == RogueDeck.Core.Combat.CombatResult.Victory,
                lost = fought.DamageTaken,
                left = fought.HeroHpRemaining,
                max = run.Health.Max,
                rounds = _rounds,
                deck = run.Deck.Select(c => c.DefinitionId.value + (c.UpgradeLevel > 0 ? "+" : "")).ToList(),
                relics = run.Relics.Select(r => r.Id.Value).ToList(),
                played = fought.CardsPlayed,
            };
            var record = JsonSerializer.SerializeToNode(line)!.AsObject();
            if (host.IsTrialEncounter)
            {
                record["mode"] = "encounter";
                record["stage"] = host.TrialSingles(host.TrialAct).FirstOrDefault(s => s.Encounter == host.TrialEncounter)?.Stage;
            }
            var path = LogPathOf(host);
            using var file = Godot.FileAccess.FileExists(path)
                ? Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.ReadWrite)
                : Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            if (file is null)
                continue;
            file.SeekEnd();
            file.StoreLine(record.ToJsonString());
            _openedAt = null;
            _rounds = 0;
        }
    }
}
