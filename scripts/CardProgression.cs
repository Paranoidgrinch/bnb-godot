using System.Text.Json;
using RogueDeck.Run;

namespace BnbGodot;

// ── THE CARD PROGRESSION (user, 2026-11-17; bnb-content/META_PROGRESSION_PLAN.md §1) ──────────────────────────
// A character starts with part of its cards and relics (the Lieutenant: 30 of 80 cards, 10 of 19 relics) and
// earns the rest level by level. Every run pays XP by how far it got; every level from 2 to 13 puts a sealed
// folder (a booster) into the Safe Deposit Box, and the player breaks its seal there. The booster's cards and
// relics are offered from the next run on. Variety over power (user): the start set wins Act I as well as the
// full pool does, the boosters bring more ways to play.
//
// What the game document says (Converter/Progression.cs → Extra["progression:<character>"]): the start set, the
// boosters level by level, the XP each thing pays and the XP each level costs.
//
// How a run is held to it: right before a normal run starts, every card and relic the player does not own yet
// is set as a profile flag "locked:<id>"; the engine mirrors those into the run, which never offers a locked id
// in a reward, a shop or a random draw (CharacterContent.IsLocked), and the profile lets go of them at once.
// Trials, the bench, the golden set, today's run and the other characters play the full pool.
public sealed record ProgressionBooster(int Level, string Name, IReadOnlyList<string> Cards, IReadOnlyList<string> Relics);

public sealed record ProgressionXp(int Room, int Elite, int Boss, int Win);

public sealed record ProgressionDefinition(
    string Character,
    IReadOnlyList<string> StartCards,
    IReadOnlyList<string> StartRelics,
    IReadOnlyList<ProgressionBooster> Boosters,
    ProgressionXp Xp,
    // XP to go from level n to n+1, for n = 1, 2, …
    IReadOnlyList<int> LevelCosts)
{
    public int MaxLevel => LevelCosts.Count + 1;

    public IEnumerable<string> AllCards => StartCards.Concat(Boosters.SelectMany(b => b.Cards));
    public IEnumerable<string> AllRelics => StartRelics.Concat(Boosters.SelectMany(b => b.Relics));
}

public sealed record XpGain(int Rooms, int Elites, int Bosses, bool Won, int Total, int LevelBefore, int LevelAfter);

public static class CardProgression
{
    public const string ExtraPrefix = "progression:";
    private const string XpPrefix = "bnb.xp.";
    private const string OpenedPrefix = "bnb.booster.opened.";
    public const string LockPrefix = CharacterContent.LockedPrefix;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly Dictionary<string, ProgressionDefinition?> Cache = [];
    private static RunBlueprint? _cachedFor;

    public static ProgressionDefinition? For(RunBlueprint blueprint, string character)
    {
        if (!ReferenceEquals(_cachedFor, blueprint))
        {
            Cache.Clear();
            _cachedFor = blueprint;
        }
        if (Cache.TryGetValue(character, out var known))
            return known;
        ProgressionDefinition? found = null;
        if (blueprint.Presentation.Game?.Extra.GetValueOrDefault(ExtraPrefix + character) is { } text)
        {
            try
            {
                found = JsonSerializer.Deserialize<ProgressionDefinition>(text, Json);
            }
            catch (JsonException)
            {
                found = null;
            }
        }
        return Cache[character] = found;
    }

    public static IEnumerable<ProgressionDefinition> All(RunBlueprint blueprint) =>
        blueprint.Characters.Select(c => For(blueprint, c.Id)).OfType<ProgressionDefinition>();

    // ── reading the profile ───────────────────────────────────────────────────────────────────────────────

    public static int Xp(MetaState meta, string character) => meta.GetCounter(XpPrefix + character);

    public static int LevelOf(ProgressionDefinition definition, int xp)
    {
        var level = 1;
        foreach (var cost in definition.LevelCosts)
        {
            if (xp < cost)
                break;
            xp -= cost;
            level++;
        }
        return level;
    }

    // XP into the current level and what the next one costs (0 at the top).
    public static (int Into, int Needed) Progress(ProgressionDefinition definition, int xp)
    {
        foreach (var cost in definition.LevelCosts)
        {
            if (xp < cost)
                return (xp, cost);
            xp -= cost;
        }
        return (0, 0);
    }

    public static bool IsOpened(MetaState meta, ProgressionDefinition definition, ProgressionBooster booster) =>
        meta.HasFlag($"{OpenedPrefix}{definition.Character}.{booster.Level}");

    public static bool IsEarned(MetaState meta, ProgressionDefinition definition, ProgressionBooster booster) =>
        LevelOf(definition, Xp(meta, definition.Character)) >= booster.Level;

    public static IReadOnlySet<string> OwnedCards(MetaState meta, ProgressionDefinition definition) =>
        definition.StartCards.Concat(definition.Boosters.Where(b => IsOpened(meta, definition, b)).SelectMany(b => b.Cards)).ToHashSet();

    public static IReadOnlySet<string> OwnedRelics(MetaState meta, ProgressionDefinition definition) =>
        definition.StartRelics.Concat(definition.Boosters.Where(b => IsOpened(meta, definition, b)).SelectMany(b => b.Relics)).ToHashSet();

    public static IReadOnlyList<string> Locked(MetaState meta, ProgressionDefinition definition)
    {
        var cards = OwnedCards(meta, definition);
        var relics = OwnedRelics(meta, definition);
        return [.. definition.AllCards.Where(c => !cards.Contains(c)), .. definition.AllRelics.Where(r => !relics.Contains(r))];
    }

    // Break the seal: the booster's cards and relics are the player's from the next run on.
    public static bool Open(GameHost host, ProgressionDefinition definition, ProgressionBooster booster)
    {
        var meta = host.Meta;
        if (!IsEarned(meta, definition, booster) || IsOpened(meta, definition, booster))
            return false;
        meta.SetFlag($"{OpenedPrefix}{definition.Character}.{booster.Level}");
        host.SaveMeta(meta);
        return true;
    }

    // ── a run held to what the player owns ────────────────────────────────────────────────────────────────

    public static void Arm(GameHost host, string? character, bool daily)
    {
        var meta = host.Meta;
        ClearLocks(meta);
        if (!daily && character is not null && For(host.Blueprint, character) is { } definition)
            foreach (var id in Locked(meta, definition))
                meta.SetFlag(LockPrefix + id);
        host.SaveMeta(meta);
    }

    public static void Disarm(GameHost host)
    {
        var meta = host.Meta;
        if (ClearLocks(meta))
            host.SaveMeta(meta);
    }

    public static bool ClearLocks(MetaState meta)
    {
        var locks = meta.Flags.Where(f => f.StartsWith(LockPrefix, StringComparison.Ordinal)).ToList();
        foreach (var flag in locks)
            meta.ClearFlag(flag);
        return locks.Count > 0;
    }

    // ── XP at the run's end ───────────────────────────────────────────────────────────────────────────────

    private static RogueDeck.Sandbox.Composition.RunPlayback? _settled;

    public static XpGain? LastGain { get; private set; }

    // Asked on every change, like Shells.Observe, and booked once per run. A loss pays its progress too.
    public static void Observe(GameHost host)
    {
        if (host.IsTutorial || host.IsMultiplayer || host.IsProbe || host.IsTrial
            || host.Play is not { Session: { IsComplete: true } session } play || ReferenceEquals(_settled, play))
            return;
        _settled = play;
        var run = session.Run;
        var character = run.CharacterId ?? host.Blueprint.Characters.FirstOrDefault()?.Id ?? "";
        var tally = RunTally.Counts;
        var rooms = RunLog.Latest?.Rooms.Count ?? run.EventHistory.OfType<NodeEnteredRunEvent>().Count();
        LastGain = Book(host, character, rooms, tally.GetValueOrDefault(RunTally.Elites), tally.GetValueOrDefault(RunTally.Bosses),
            run.Result == RunResult.Victory);
    }

    public static XpGain? Book(GameHost host, string character, int rooms, int elites, int bosses, bool won)
    {
        var meta = host.Meta;
        ClearLocks(meta);
        if (For(host.Blueprint, character) is not { } definition)
        {
            host.SaveMeta(meta);
            return null;
        }
        var xp = definition.Xp;
        var total = Math.Max(rooms, 0) * xp.Room + elites * xp.Elite + bosses * xp.Boss + (won ? xp.Win : 0);
        var before = LevelOf(definition, Xp(meta, character));
        meta.AddCounter(XpPrefix + character, total);
        var after = LevelOf(definition, Xp(meta, character));
        host.SaveMeta(meta);
        return new XpGain(rooms, elites, bosses, won, total, before, after);
    }
}
