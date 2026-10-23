using RogueDeck.Run;

namespace BnbGodot;

// WHO HAS BEEN EARNED (user, 2026-10-23; the chain is bnb-content's CharacterUnlocks). The game document says, for
// each locked character, who must be playing ("any" or a character id) and how many act bosses that run must have
// beaten: "unlock:<character>" = "<who>:<bosses>". This counts the run in play and sets the character's UnlockFlag
// the moment the boss falls — so the menu offers the new character even if the run is never finished.
//
// ⚠ The engine saves the profile it loaded at the start of a run when that run completes, which would take a flag
// set in between back out. So this is asked on EVERY change, including the one after the completion: what it
// counts is the run itself, which still says what was beaten.
public static class CharacterUnlocks
{
    public const string Any = "any";
    private const string PlayedPrefix = "bnb.played.";
    private const string SeenPrefix = "bnb.unlock_seen.";

    public static void Observe(GameHost host)
    {
        if (host.IsTutorial || host.IsMultiplayer || host.IsProbe || host.Play?.Session?.Run is not { } run)
            return;
        var bosses = RunTally.Counts.GetValueOrDefault(RunTally.Bosses);
        if (bosses == 0)
            return;
        var who = run.CharacterId ?? host.Blueprint.Characters.FirstOrDefault()?.Id;
        MetaState? meta = null;
        foreach (var (character, flag, by, needed) in Chain(host.Blueprint))
        {
            if ((by != Any && by != who) || bosses < needed)
                continue;
            meta ??= host.Meta;
            if (meta.HasFlag(flag))
                continue;
            meta.SetFlag(flag);
            Godot.GD.Print($"unlocked: {character}");
        }
        if (meta is not null)
            host.SaveMeta(meta);
    }

    // Every locked character: its flag, who must earn it and with how many bosses.
    public static IEnumerable<(string Character, string Flag, string By, int Bosses)> Chain(RunBlueprint blueprint)
    {
        var extra = blueprint.Presentation.Game?.Extra ?? new Dictionary<string, string>();
        foreach (var character in blueprint.Characters)
        {
            if (character.UnlockFlag is not { } flag || extra.GetValueOrDefault($"unlock:{character.Id}") is not { } rule)
                continue;
            var parts = rule.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var bosses))
                yield return (character.Id, flag, parts[0], bosses);
        }
    }

    public static string Hint(RunBlueprint blueprint, string character) =>
        blueprint.Presentation.Game?.Extra.GetValueOrDefault($"unlock.hint:{character}") ?? "Locked.";

    // Has this character been played (or taught) on this machine? The tutorial question is asked once.
    public static bool HasPlayed(MetaState meta, string character) => meta.HasFlag(PlayedPrefix + character);

    public static void MarkPlayed(GameHost host, string character)
    {
        var meta = host.Meta;
        meta.SetFlag(PlayedPrefix + character);
        host.SaveMeta(meta);
    }

    // Characters unlocked since the menu last said so — the title screen announces them once.
    public static IReadOnlyList<RunCharacter> Unannounced(GameHost host)
    {
        var meta = host.Meta;
        return [.. host.Blueprint.Characters.Where(c => c.UnlockFlag is { } flag && meta.HasFlag(flag)
            && !meta.HasFlag(SeenPrefix + c.Id))];
    }

    public static void Announced(GameHost host, IEnumerable<RunCharacter> characters)
    {
        var meta = host.Meta;
        foreach (var character in characters)
            meta.SetFlag(SeenPrefix + character.Id);
        host.SaveMeta(meta);
    }
}
