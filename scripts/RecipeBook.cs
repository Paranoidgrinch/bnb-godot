using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace BnbGodot;

// THE HEDGE WITCH'S RECIPE BOOK (hedge_witch_master.md §9, §17; plan G4) — what the cauldron makes, and which of its
// secrets the player has found.
//
// Three sections, as the canon names them:
//   • EVERYDAY BREWING — the 35 family recipes, open from the start: BREW's presentation lists every one
//     ("recipe:<fam>+<fam>+<fam>" = "Name|effect").
//   • THINGS THAT WORKED ONCE — the Hidden Recipes the player has brewed ("hidden:<n>" = "Name|cards|effect").
//   • NOTES IN THE MARGIN — a clue for each one not yet found ("clue:<n>"), never its three cards.
//
// Discovery is the player's KNOWLEDGE, kept in `user://recipes.json` — beside the archive's fund book and for the
// same reason (Archive.cs: the meta profile is snapshotted at a run's start and written back at its end, so a
// discovery written into it mid-run would be lost). The engine never reads it: the brew only says which recipe it
// was (the hero's `hidden_recipe_brewed` counter), and this book decides whether that was news.
public sealed record FamilyRecipe(string Families, string Name, string Effect);
public sealed record HiddenRecipe(int Number, string Name, IReadOnlyList<string> Cards, string Effect, string Clue);

public static class RecipeBook
{
    private const string BrewAction = "cauldron_brew";
    private const string Path = "user://recipes.json";
    private static readonly RogueDeck.Core.Combat.CounterId Brewed = new("hidden_recipe_brewed");

    // ── the catalogue, from the game document ────────────────────────────────────────────────────────────

    public static IReadOnlyList<FamilyRecipe> Family(RunBlueprint blueprint) =>
        [.. Extra(blueprint)
            .Where(p => p.Key.StartsWith("recipe:", StringComparison.Ordinal))
            .Select(p => (Families: Short(p.Key["recipe:".Length..]), Said: Split(p.Value, 2)))
            .Select(r => new FamilyRecipe(r.Families, r.Said[0], r.Said[1]))
            .OrderBy(r => r.Name, StringComparer.Ordinal)];

    public static IReadOnlyList<HiddenRecipe> Hidden(RunBlueprint blueprint)
    {
        var extra = Extra(blueprint);
        return [.. extra
            .Where(p => p.Key.StartsWith("hidden:", StringComparison.Ordinal))
            .Select(p => (Number: int.Parse(p.Key["hidden:".Length..], System.Globalization.CultureInfo.InvariantCulture),
                Said: Split(p.Value, 3)))
            .Select(r => new HiddenRecipe(r.Number, r.Said[0], r.Said[1].Split('+'), r.Said[2],
                extra.GetValueOrDefault($"clue:{r.Number}") ?? ""))
            .OrderBy(r => r.Number)];
    }

    public static bool HasCauldron(RunBlueprint blueprint) => Extra(blueprint).Count > 0;

    private static IReadOnlyDictionary<string, string> Extra(RunBlueprint blueprint) =>
        blueprint.Presentation.Cards.GetValueOrDefault(BrewAction)?.Extra ?? new Dictionary<string, string>();

    private static string[] Split(string said, int parts)
    {
        var pieces = said.Split('|', parts);
        return [.. pieces, .. Enumerable.Repeat("", parts - pieces.Length)];
    }

    private static string Short(string families) =>
        string.Join(" + ", families.Split('+').Select(f => f.StartsWith("fam_", StringComparison.Ordinal)
            ? char.ToUpperInvariant(f[4]) + f[5..] : f));

    // ── the player's book ────────────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<int> Found = [];
    private static bool _loaded;

    private static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        if (!Godot.FileAccess.FileExists(Path))
            return;
        try
        {
            foreach (var n in System.Text.Json.JsonSerializer.Deserialize<int[]>(Godot.FileAccess.GetFileAsString(Path)) ?? [])
                Found.Add(n);
        }
        catch
        {
            // An unreadable book must never block playing, nor block filling itself again.
        }
    }

    private static void Save()
    {
        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(System.Text.Json.JsonSerializer.Serialize(Found.Order().ToArray()));
    }

    // ⚠ A ROBOT DOES NOT FILL A PLAYER'S BOOK (Archive.Robot): the probes record in memory and never write.
    private static bool? _robot;

    private static bool Robot => _robot ??= Godot.OS.GetCmdlineUserArgs().Any(a =>
        a.StartsWith("--sim", StringComparison.Ordinal) || a.StartsWith("--smoke", StringComparison.Ordinal));

    public static bool Discovered(int number)
    {
        Load();
        return Found.Contains(number);
    }

    public static int DiscoveredCount
    {
        get
        {
            Load();
            return Found.Count;
        }
    }

    // Called with every redraw of the run screen. The brew leaves its recipe's number on the hero until the next
    // brew; the first time a number is seen it is news, and its name comes back for the screen to announce.
    public static string? Observe(RunPlayback? play, RunBlueprint blueprint)
    {
        if (play?.CombatDriver?.Current is not { } fight)
            return null;
        var number = fight.State.GetCombatant(fight.HeroId).GetCounter(Brewed);
        if (number <= 0)
            return null;
        Load();
        if (!Found.Add(number))
            return null;
        if (!Robot)
            Save();
        return Hidden(blueprint).FirstOrDefault(r => r.Number == number)?.Name ?? $"Recipe {number}";
    }

    // For the probe and the player's own reveal: write one in, or forget them all.
    internal static void Discover(int number)
    {
        Load();
        if (Found.Add(number) && !Robot)
            Save();
    }

    public static void Reset()
    {
        Found.Clear();
        _loaded = true;
        if (Godot.FileAccess.FileExists(Path))
            Godot.DirAccess.RemoveAbsolute(Godot.ProjectSettings.GlobalizePath(Path));
    }
}
