using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace BnbGodot;

// WHAT THE PLAYER HAS MET, AND WHAT IT WAS — the first layer of meta progression.
//
// A run ends and everything it taught you goes with it: the elite that opened with 40 Block, the relic you
// could not afford, the god whose rule you only half read while losing to it. The archive is where that
// knowledge is KEPT. It is not a reward and it unlocks nothing — it is a record, and the only thing it costs
// to fill is playing.
//
// Two halves, deliberately apart:
//   • THE CATALOGUE is built from the game document and is the same for everybody. It is not a list anybody
//     maintains: the enemies come out of the ACTS' OWN ENCOUNTER POOLS, so an entry exists exactly when a run
//     can draw it. (61 of the document's 294 encounters are in no pool at all — old drafts. A slot no player
//     can ever fill is a lie about how much there is to find, so they are not here.)
//   • THE FUND BOOK is the player's, in `user://archive.json`.
//
// ⚠⚠ THE FUND BOOK IS NOT IN `metastate.json`, AND MUST NOT BE. `RunPlayback` loads the meta profile when a
// run STARTS and writes that whole loaded object back when the run ENDS (see RunPlayback.Start). Anything
// written into that file DURING a run — which is when every discovery happens — is overwritten by a snapshot
// taken before it. The archive would have silently lost a whole run's finds at the moment the run was won.
public enum ArchiveKind
{
    Enemies,
    Elites,
    Cards,
    Relics,
    Bosses,
    Gods,
}

// One thing the player can meet, flattened for the screen. The panel renders; it looks nothing up.
public sealed record ArchiveEntry(
    ArchiveKind Kind,
    string Id,
    string Name,
    string? Frame,                                    // the relic pool / card rarity the tile is framed in
    string? Prose,                                    // rules text — a card's, a relic's, a god's decree
    string? ProseTitle,                               // a god's Divine Rule wears its own name
    IReadOnlyList<(string Label, string Value)> Facts,
    IReadOnlyList<string> Moves,                      // what an enemy can do, in its own telegraph words
    string? Upgraded = null);                         // what the same card says once it has been improved

public static class Archive
{
    // The six categories in the order the player asked to read them in.
    public static readonly ArchiveKind[] Kinds =
        [ArchiveKind.Enemies, ArchiveKind.Elites, ArchiveKind.Cards, ArchiveKind.Relics,
         ArchiveKind.Bosses, ArchiveKind.Gods];

    public static string Title(ArchiveKind kind) => kind switch
    {
        ArchiveKind.Enemies => "Enemies",
        ArchiveKind.Elites => "Elites",
        ArchiveKind.Cards => "Cards",
        ArchiveKind.Relics => "Relics",
        ArchiveKind.Bosses => "Bosses",
        _ => "Gods",
    };

    // Which SLOT of the fund book an entry is written in. Keyed by what the thing IS (an enemy, a card, a
    // relic) and not by which tab it is shown under, so re-ranking an enemy in the document — a standard
    // promoted to an elite — never costs a player a discovery they made.
    private static string Slot(ArchiveKind kind) => kind switch
    {
        ArchiveKind.Cards => "card",
        ArchiveKind.Relics => "relic",
        _ => "enemy",
    };

    // The art family CardVisuals reaches for. Same reasoning as Slot.
    public static string ArtFamily(ArchiveKind kind) => kind switch
    {
        ArchiveKind.Cards => "cards",
        ArchiveKind.Relics => "relics",
        _ => "enemies",
    };

    // ── the catalogue ────────────────────────────────────────────────────────────

    private static readonly Dictionary<ArchiveKind, List<ArchiveEntry>> Catalogue = new();
    private static string _builtFor = "";

    public static void Build(RunBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var stamp = $"{blueprint.Encounters.Count}/{blueprint.Cards.Count}/{blueprint.Relics.Count}/{blueprint.Acts?.Count ?? 0}";
        if (_builtFor == stamp)
            return;
        _builtFor = stamp;
        Catalogue.Clear();
        foreach (var kind in Kinds)
            Catalogue[kind] = [];

        BuildEnemies(blueprint);
        BuildCards(blueprint);
        BuildRelics(blueprint);
    }

    public static IReadOnlyList<ArchiveEntry> Entries(ArchiveKind kind) =>
        Catalogue.TryGetValue(kind, out var list) ? list : [];

    // What one act's pools reach, with the act it belongs to — the only honest answer to "can a run draw
    // this". Both generators realize their rooms from the SAME pools (the strategic spec carries shape only),
    // so this covers the game whichever one the player picked on the title screen.
    private static void BuildEnemies(RunBlueprint blueprint)
    {
        // enemy id → everything every encounter it stands in says about it.
        var health = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        var actsOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var actions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var statuses = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var divine = new Dictionary<string, string>(StringComparer.Ordinal); // enemy id → its god encounter
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        var byId = blueprint.Encounters.ToDictionary(e => e.Id.Value, StringComparer.Ordinal);

        foreach (var act in blueprint.Acts ?? [])
        {
            var actName = Short(act.NameKey) ?? act.Id;
            var pools = act.MapGeneration?.Encounters;
            if (pools is null)
                continue;
            foreach (var role in pools.ByRole.Keys)
            {
                foreach (var candidate in pools.For(role))
                {
                    if (!byId.TryGetValue(candidate.Encounter.Value, out var encounter))
                        continue;
                    var look = blueprint.Presentation.Encounters.GetValueOrDefault(encounter.Id.Value);
                    foreach (var enemy in encounter.Enemies)
                    {
                        Pool(health, enemy.Id).Add(enemy.MaxHealth);
                        AddOnce(actsOf, enemy.Id, actName);
                        names.TryAdd(enemy.Id, enemy.DisplayName ?? Humanized(enemy.Id));
                        foreach (var action in enemy.Actions)
                            AddOnce(actions, enemy.Id, action.value);
                        foreach (var status in enemy.StartingStatuses ?? [])
                            AddOnce(statuses, enemy.Id, status.Status.value);
                        if (look?.Extra.ContainsKey("divineRule") == true)
                            divine.TryAdd(enemy.Id, encounter.Id.Value);
                    }
                }
            }
        }

        var moveOf = blueprint.EnemyActions.ToDictionary(a => a.Id, a => a.Intent.Label, StringComparer.Ordinal);
        var statusName = blueprint.Statuses.ToDictionary(
            s => s.Id, s => string.IsNullOrWhiteSpace(s.NameKey) ? s.Id : s.NameKey!, StringComparer.Ordinal);

        foreach (var (id, pool) in health.OrderBy(p => names.GetValueOrDefault(p.Key, p.Key), StringComparer.Ordinal))
        {
            var look = blueprint.Presentation.Enemies.GetValueOrDefault(id);
            var kind = divine.ContainsKey(id) ? ArchiveKind.Gods
                : look?.Frame switch
                {
                    "elite" => ArchiveKind.Elites,
                    "boss" => ArchiveKind.Bosses,
                    _ => ArchiveKind.Enemies,
                };

            var facts = new List<(string, string)>
            {
                ("Health", pool.Min == pool.Max ? $"{pool.Min}" : $"{pool.Min}–{pool.Max}"),
                ("Met in", string.Join(" · ", actsOf.GetValueOrDefault(id) ?? [])),
            };
            if (look?.Tags.Count > 0)
                facts.Add(("Traits", string.Join(", ", look.Tags.Select(Humanized))));
            if (statuses.GetValueOrDefault(id) is { Count: > 0 } opens)
                facts.Add(("Opens with", string.Join(", ", opens.Select(s => statusName.GetValueOrDefault(s, s)))));

            string? decree = null, decreeTitle = null;
            if (divine.TryGetValue(id, out var godEncounter)
                && blueprint.Presentation.Encounters.GetValueOrDefault(godEncounter) is { } godLook)
            {
                decree = godLook.Extra.GetValueOrDefault("divineRule");
                decreeTitle = godLook.Extra.GetValueOrDefault("divineRuleTitle");
            }

            Catalogue[kind].Add(new ArchiveEntry(
                kind, id, names.GetValueOrDefault(id, Humanized(id)), look?.Frame,
                decree, decreeTitle, facts,
                [.. (actions.GetValueOrDefault(id) ?? []).Select(a => moveOf.GetValueOrDefault(a, Humanized(a)))]));
        }
    }

    // ⚠ AN IMPROVED CARD IS NOT A SECOND CARD. 134 of the document's 363 card definitions are the `+` form of
    // one already in it, and shelving both would put "Paper Cut" beside "Paper Cut", claim there are 363
    // cards to find when there are 229, and make every tally the archive prints a third too big. The upgrade
    // is folded into the card it improves, where it belongs: as the other thing that card can say. (It is the
    // same reading the art already takes — both ids declare one picture, CardVisuals.Slot.)
    private static void BuildCards(RunBlueprint blueprint)
    {
        var ids = blueprint.Cards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var improved = blueprint.Cards
            .Where(c => c.Id.EndsWith('+') && ids.Contains(c.Id.TrimEnd('+')))
            .ToDictionary(
                c => c.Id.TrimEnd('+'),
                c => blueprint.Presentation.Cards.GetValueOrDefault(c.Id)?.FlavorText ?? c.DescriptionKey,
                StringComparer.Ordinal);

        foreach (var card in blueprint.Cards
            .Where(c => !(c.Id.EndsWith('+') && improved.ContainsKey(c.Id.TrimEnd('+'))))
            .OrderBy(c => c.NameKey ?? c.Id, StringComparer.Ordinal))
        {
            var look = blueprint.Presentation.Cards.GetValueOrDefault(card.Id);
            var facts = new List<(string, string)>
            {
                ("Cost", card.Costs.Count == 0 ? "⚡0" : string.Join(" · ", card.Costs.Select(Priced))),
            };
            if (look?.Rarity is { Length: > 0 } rarity)
                facts.Add(("Rarity", Humanized(rarity)));
            if (card.Tags.Count > 0)
                facts.Add(("Type", string.Join(", ", card.Tags.Select(t => Humanized(t.value)))));
            Catalogue[ArchiveKind.Cards].Add(new ArchiveEntry(
                ArchiveKind.Cards, card.Id, card.NameKey ?? Humanized(card.Id), look?.Rarity,
                look?.FlavorText ?? card.DescriptionKey, null, facts, [],
                improved.GetValueOrDefault(card.Id)));
        }
    }

    // What one cost reads as. Every card in the game is priced in energy, and the bolt is what the hand and
    // the shelf already print; anything else says its own name rather than borrowing the bolt's.
    private static string Priced(ResourceCost cost) =>
        cost.ResourceId == StandardCombatIds.EnergyResource ? $"⚡{cost.Amount}" : $"{Humanized(cost.ResourceId.value)} {cost.Amount}";

    private static void BuildRelics(RunBlueprint blueprint)
    {
        foreach (var relic in blueprint.Relics.OrderBy(r => r.DisplayName, StringComparer.Ordinal))
        {
            var look = blueprint.Presentation.Relics.GetValueOrDefault(relic.Id);
            var facts = new List<(string, string)>();
            if (look?.Rarity is { Length: > 0 } rarity)
                facts.Add(("Pool", Humanized(rarity)));
            Catalogue[ArchiveKind.Relics].Add(new ArchiveEntry(
                ArchiveKind.Relics, relic.Id, relic.DisplayName, look?.Frame ?? look?.Rarity,
                look?.FlavorText, null, facts, []));
        }
    }

    // ── the fund book ────────────────────────────────────────────────────────────

    private const string Path = "user://archive.json";
    private static readonly HashSet<string> Found = new(StringComparer.Ordinal);
    private static bool _loaded;

    private static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        foreach (var key in Read(Path))
            Found.Add(key);
    }

    private static IReadOnlyList<string> Read(string path)
    {
        if (!Godot.FileAccess.FileExists(path))
            return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(
                Godot.FileAccess.GetFileAsString(path)) ?? [];
        }
        catch
        {
            // An unreadable fund book must never block playing, and must never block filling itself again.
            return [];
        }
    }

    private static void Save(string? to = null)
    {
        using var file = Godot.FileAccess.Open(to ?? Path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(System.Text.Json.JsonSerializer.Serialize(Found.Order().ToArray()));
    }

    // ⚠ A ROBOT DOES NOT FILL A PLAYER'S ARCHIVE. `tools/simulate.sh` runs a hundred seeded walks eight at a
    // time and `--smoke-marathon` walks a whole game: between them they would hand the player a finished
    // archive they never earned, and the parallel ones would race each other for this one file. Every probe
    // still RECORDS — in memory, so a probe can read exactly what its walk met — it just does not write it
    // down. The one exception is the archive probe itself, which calls Discover/Reset by hand and says so.
    private static bool? _robot;

    private static bool Robot => _robot ??= Godot.OS.GetCmdlineUserArgs().Any(a =>
        a.StartsWith("--sim", StringComparison.Ordinal) || a.StartsWith("--smoke", StringComparison.Ordinal));

    public static bool Seen(ArchiveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Load();
        return Found.Contains($"{Slot(entry.Kind)}:{entry.Id}");
    }

    public static int SeenCount(ArchiveKind kind)
    {
        Load();
        return Entries(kind).Count(Seen);
    }

    // Everything found so far, thrown away. Both files: the fund book AND the engine's own cross-run profile,
    // because "reset the meta progression" means the whole of it and the profile is the other half.
    public static void Reset()
    {
        Found.Clear();
        _loaded = true;
        foreach (var file in new[] { Path, "user://metastate.json" })
            if (Godot.FileAccess.FileExists(file))
                Godot.DirAccess.RemoveAbsolute(Godot.ProjectSettings.GlobalizePath(file));
    }

    // ── what a run teaches ───────────────────────────────────────────────────────

    // Called from every redraw of the run screen. Cheap on purpose: a fight redraws hundreds of times and
    // this walks a deck of twenty and a shelf of ten each time, adding to a set — the FILE is only touched
    // when the set actually grew, which across a whole run is a few dozen times.
    public static void Observe(InteractiveRunSession? session, RunPlayback? play)
    {
        if (session is null)
            return;
        Load();
        var before = Found.Count;

        // WHO IS IN THE ROOM. The definition id is the enemy's own id — the same one that names its picture.
        if (play?.CombatDriver?.Current is { } fight)
        {
            foreach (var combatant in fight.State.Combatants.Where(c => c.TeamId == StandardCombatIds.EnemyTeam))
                Found.Add($"enemy:{combatant.DefinitionId.value}");
            // A card that is not in the deck and never will be — a Citation forced into the hand, a token a
            // god hands out — is still a card the player has MET, and is often the most memorable one.
            foreach (var card in fight.Hand)
                Found.Add($"card:{card.DefinitionId.value.TrimEnd('+')}");
        }

        // WHAT THE PARTY CARRIES.
        foreach (var member in session.Run.Party)
        {
            // An improved card is filed under the card it improves — the shelf holds one of each (BuildCards),
            // so the fund book has to speak the same id or an upgraded starter would be a find nobody can see.
            foreach (var card in member.Deck)
                Found.Add($"card:{card.DefinitionId.value.TrimEnd('+')}");
            foreach (var relic in member.Relics)
                Found.Add($"relic:{relic.Id.Value}");
        }

        // AND WHAT IT WAS OFFERED. Meeting a thing is seeing it, not owning it — the rare relic you could
        // not afford is exactly the one worth being able to look up later.
        if (session.IsAwaitingEntities && session.PendingEntities is { } offer)
            for (var i = 0; i < offer.Displays.Count; i++)
                Note(offer.ArtAt(i));
        if (session.PendingShopShelf is { } shelf)
            foreach (var slot in shelf.Slots)
                Note(RunEntityLabeler.ArtForGrant(slot.Entry.Payload));

        if (Found.Count != before && !Robot)
            Save();
    }

    private static void Note(EntityArt? art)
    {
        switch (art)
        {
            case EntityArt { Kind: EntityArt.Card } card:
                Found.Add($"card:{card.Id.TrimEnd('+')}");
                break;
            case EntityArt { Kind: EntityArt.Relic } relic:
                Found.Add($"relic:{relic.Id}");
                break;
        }
    }

    // For the probes: mark one thing found by hand, so a picture of the screen can show both states without
    // anybody having to play an act first.
    internal static void Discover(ArchiveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Load();
        if (Found.Add($"{Slot(entry.Kind)}:{entry.Id}"))
            Save();
    }

    // Everything on one shelf that has been met — for the probe that has to say what a walk actually taught
    // the archive, by name and not by count.
    internal static IReadOnlyList<ArchiveEntry> FoundIn(ArchiveKind kind) => [.. Entries(kind).Where(Seen)];

    // ⚠⚠ THE ONE PATH NO PROBE WALKS IS THE PLAYER'S. Every probe suppresses the write (Robot), so "Observe
    // recorded it" and "a record reaches the disk" have only ever been true separately — and separately is
    // exactly how a ValueTuple once ate a whole combat snapshot: nine pairs that round-tripped perfectly in
    // memory and arrived on disk as `{}`. So the recording probe writes what its walk found through the REAL
    // Save, to a file of its own, and reads it back through the REAL reader. Returns how many came back.
    internal static (int Wrote, int Read) ProveWrite(string to)
    {
        Save(to);
        return (Found.Count, Read(to).Count);
    }

    // An empty fund book that is NOT the file's — a probe measures what ITS walk met, and a machine that has
    // played before would otherwise hand it a hundred finds it did not make.
    internal static void StartClean()
    {
        Found.Clear();
        _loaded = true;
    }

    // Drop what is in memory so the next question is answered by the FILE. The one thing a fund book has to
    // get right is surviving the process it was written in, and an in-memory round trip cannot show that.
    internal static void Forget()
    {
        Found.Clear();
        _loaded = false;
    }

    // ── odds and ends ────────────────────────────────────────────────────────────

    private static SortedSet<int> Pool(Dictionary<string, SortedSet<int>> map, string key)
    {
        if (!map.TryGetValue(key, out var set))
            map[key] = set = [];
        return set;
    }

    private static void AddOnce(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        if (!list.Contains(value, StringComparer.Ordinal))
            list.Add(value);
    }

    // "an_underscore_is_a_word_break" ⇒ "An underscore is a word break" — the same reading of an id the
    // relic shelf uses when a thing has no authored name.
    public static string Humanized(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var words = id.Replace('_', ' ').Replace('.', ' ').Trim();
        return words.Length == 0 ? id : char.ToUpperInvariant(words[0]) + words[1..];
    }

    // "Act I: The Old City Offices" ⇒ "Act I". The full title is the act's own heading; in a fact row beside
    // four others it is the number that is being read.
    private static string? Short(string? actName) =>
        string.IsNullOrWhiteSpace(actName) ? null : actName.Split(':')[0].Trim();
}
