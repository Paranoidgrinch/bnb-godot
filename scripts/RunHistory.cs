using System.Text.Json;
using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace BnbGodot;

// THE PLAYER'S OWN RECORD OF THE RUNS THEY PLAYED — one line per run, kept in `user://run-history.json`, read by
// the Run history screen and by nothing else. It is a SUMMARY written once at the end of a run: who played, how
// it ended, how far it got, what it ended holding and what ended it. The full recording (seed + every answer)
// is a different thing and goes home to bnb-runs; it is deleted here once it has been sent, which is why the
// history cannot simply read the recordings back.
//
// ⚠ Only a human's run is written (RunLog.Enabled — no command-line arguments): a probe or a simulated batch
// would otherwise hand the player a history of a hundred runs they never played. Same rule as the archive.
//
// ⚠ PLAIN LISTS AND STRINGS ONLY — no tuples. System.Text.Json drops a ValueTuple's fields without a word, and a
// history that silently loses its deck is the serialisation fault this project has already paid for once.
public sealed class RunSummary
{
    public string? StartedUtc { get; set; }
    public string? EndedUtc { get; set; }
    public int Seed { get; set; }
    public string? Character { get; set; }
    // "Victory", "Defeat" or "Abandoned".
    public string Result { get; set; } = "";
    public int Act { get; set; }
    // Rooms entered over the whole run; -1 when it is not known (a run recorded before the count existed).
    public int Rooms { get; set; } = -1;
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int Gold { get; set; }
    // What the run ended in front of: the enemies of the last fight, or null.
    public string? EndedAt { get; set; }
    public List<string> Deck { get; set; } = [];
    public List<string> Relics { get; set; } = [];
    // Bodies felled over the run (RunTally): enemies of any kind, and of those the elites and the bosses.
    public int Enemies { get; set; }
    public int Elites { get; set; }
    public int Bosses { get; set; }
}

public static class RunHistory
{
    // The probe points this at a file of its own, so the round trip it checks is through a FILE (an in-memory
    // one proves nothing about a save) and the player's history is never touched.
    internal static string Path = "user://run-history.json";
    private const int Kept = 500;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static object? _recorded;

    public static List<RunSummary> Load()
    {
        if (!Godot.FileAccess.FileExists(Path))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<RunSummary>>(Godot.FileAccess.GetFileAsString(Path), Options) ?? [];
        }
        catch (JsonException ex)
        {
            // A damaged file is set aside rather than overwritten: the next run starts a fresh history, and the
            // old one is still on disk for anyone who wants it back.
            GD.PushWarning($"run history unreadable, set aside: {ex.Message}");
            DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(Path), ProjectSettings.GlobalizePath(Path + ".bad"));
            return [];
        }
    }

    // Called on every state change; writes once, the first time it sees the run finished.
    public static void Observe(RunPlayback? play, RunRecording? recording)
    {
        if (play?.Session is not { IsComplete: true } session || ReferenceEquals(_recorded, play) || !RunLog.Enabled)
            return;
        _recorded = play;
        var run = session.Run;
        var enemies = RunTally.LastFoes;
        Append(new RunSummary
        {
            StartedUtc = recording?.StartedUtc,
            EndedUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            Seed = run.RandomSeed,
            Character = play.HeroName,
            Result = run.Result.ToString(),
            Act = run.ActNumber,
            Rooms = recording?.Rooms.Count ?? -1,
            Health = Math.Max(run.Health.Current, 0),
            MaxHealth = run.Health.Max,
            Gold = run.Resources.GetValueOrDefault(StandardRunIds.Gold),
            EndedAt = run.Result == RunResult.Victory || enemies is not { Count: > 0 } ? null : string.Join(" & ", enemies),
            Deck = [.. run.Deck.Select(card => CardName(play, card.DefinitionId.value) + new string('+', card.UpgradeLevel))],
            Relics = [.. run.Relics.Select(relic => relic.Definition.DisplayName)],
            Enemies = recording?.Tallies.GetValueOrDefault(RunTally.Enemies) ?? 0,
            Elites = recording?.Tallies.GetValueOrDefault(RunTally.Elites) ?? 0,
            Bosses = recording?.Tallies.GetValueOrDefault(RunTally.Bosses) ?? 0,
        });
    }

    // A run given up for a new one. Only the recording is left of it, so only what the recording knows is kept.
    public static void Abandoned(RunRecording recording)
    {
        if (!RunLog.Enabled)
            return;
        var character = GameHost.Instance.Blueprint.Characters
            .FirstOrDefault(c => c.Id == recording.Start.Character)?.Start.HeroName ?? recording.Start.Character;
        Append(new RunSummary
        {
            StartedUtc = recording.StartedUtc,
            EndedUtc = recording.EndedUtc,
            Seed = recording.Start.Seed,
            Character = character,
            Result = "Abandoned",
            Rooms = recording.Rooms.Count,
            Enemies = recording.Tallies.GetValueOrDefault(RunTally.Enemies),
            Elites = recording.Tallies.GetValueOrDefault(RunTally.Elites),
            Bosses = recording.Tallies.GetValueOrDefault(RunTally.Bosses),
        });
    }

    internal static void Append(RunSummary summary)
    {
        var all = Load();
        all.Add(summary);
        if (all.Count > Kept)
            all.RemoveRange(0, all.Count - Kept);
        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(all, Options));
    }

    private static string CardName(RunPlayback play, string definition) =>
        play.CardNames.GetValueOrDefault(definition) ?? definition;
}
