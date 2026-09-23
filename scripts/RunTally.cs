using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace BnbGodot;

// WHAT THE PLAYER FELLED, written into the run's recording (RunRecording.Tallies), so it travels home to
// bnb-runs with the run and the alpha ranking can add it up without replaying anything.
//
//   enemies — every enemy body that went down, whatever it was
//   elites  — elite FIGHTS won (the room's tag), not elite bodies
//   bosses  — boss fights won
// ⚠ Fights, not bodies, for the named two: a body's Frame is the strongest room it ever stands in, so a boss's
// retinue is framed "boss" too, and counting bodies made seven boss rooms into seventeen bosses.
//
// ⚠ READ FROM THE FIGHT'S END, NEVER BY WATCHING THE FIGHT. The finishing blow and the end of a fight arrive in
// one step, and the host rebuilds the fight object on every answer, so a watcher saw almost nothing (14 bodies
// over a 110-room marathon). Every finished fight says who fell (CombatResolvedRunEvent.Fallen).
// ⚠ AND EACH END IS COUNTED ONCE, BY WHERE IT HAPPENED. The run's history is not a ledger: the session rebases on
// a checkpoint after every room and replays the answers since, so the same fight's end turns up as a new event
// object on every answer, and the history forgets it at the next room. It is keyed act/room/nth-end-in-that-room.
// ⚠ A fight resumed half-won does not count again the bodies that were already down when it was saved.
public static class RunTally
{
    public const string Enemies = "enemies";
    public const string Elites = "elites";
    public const string Bosses = "bosses";

    private static object? _play;
    private static readonly HashSet<string> Counted = new(StringComparer.Ordinal);
    private static List<string>? _alreadyDown;
    private static bool _looked;
    // A run nobody records (a probe, a simulated run) still counts — here, so the end-of-run report has numbers.
    private static readonly Dictionary<string, int> Unrecorded = new(StringComparer.Ordinal);

    // WHO THE PLAYER WAS LAST FIGHTING, by name — what the certificate and the history enter as the cause of death.
    // Kept while a fight is on, because a lost fight is gone from the driver by the time the run says it is over.
    public static IReadOnlyList<string> LastFoes { get; private set; } = [];

    // The counts of the run in play: the recording's own when there is one.
    public static IReadOnlyDictionary<string, int> Counts => RunLog.Current?.Tallies ?? Unrecorded;

    public static void Observe(RunPlayback? play)
    {
        var tallies = RunLog.Current?.Tallies ?? Unrecorded;
        if (!ReferenceEquals(play, _play))
        {
            _play = play;
            _alreadyDown = null;
            _looked = false;
            LastFoes = [];
            Counted.Clear();
            if (RunLog.Current is null)
                Unrecorded.Clear();
        }
        if (play?.Session?.Run is not { } run)
            return;
        if (play.CombatDriver?.Current is { IsOver: false } live)
            LastFoes = live.State.Combatants
                .Where(c => c.Id != live.HeroId && c.TeamId == RogueDeck.Core.Combat.StandardCombatIds.EnemyTeam
                    && c.IsAlive)
                .Select(c => play.EnemyNames.GetValueOrDefault(c.Id.value) ?? c.Id.value)
                .Distinct()
                .ToList();
        // The first look at a fresh playback: a fight already under way was saved part-fought.
        if (!_looked)
        {
            _looked = true;
            if (play.CombatDriver?.Current is { } fight)
                _alreadyDown = fight.State.Combatants
                    .Where(c => c.Id != fight.HeroId && c.TeamId == RogueDeck.Core.Combat.StandardCombatIds.EnemyTeam
                        && !c.IsAlive)
                    .Select(c => c.DefinitionId.value)
                    .ToList();
        }

        var nth = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fought in run.EventHistory.OfType<CombatResolvedRunEvent>())
        {
            var room = $"{run.ActNumber}:{fought.NodeId.Value}";
            var key = $"{room}#{nth[room] = nth.GetValueOrDefault(room) + 1}";
            if (!Counted.Add(key))
                continue;
            var fallen = fought.Fallen?.ToList() ?? [];
            if (_alreadyDown is { } before)
            {
                foreach (var id in before)
                    fallen.Remove(id);
                _alreadyDown = null;
            }
            foreach (var _ in fallen)
                Add(tallies, Enemies);
            if (fought.Result == RogueDeck.Core.Combat.CombatResult.Victory && fought.Tags is { } tags)
            {
                if (tags.Contains(MapNodeTags.Boss))
                    Add(tallies, Bosses);
                else if (tags.Contains(MapNodeTags.Elite))
                    Add(tallies, Elites);
            }
        }
    }

    private static void Add(Dictionary<string, int> tallies, string key) =>
        tallies[key] = tallies.GetValueOrDefault(key) + 1;
}
