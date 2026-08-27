using RogueDeck.Run;

namespace BnbGodot;

// What every named thing in the game MEANS, in one lookup.
//
// A player meets a word — "Paperwork", "Ratified", "Still in Force", "Gate: Half-Raised" — on a card, on an
// enemy's chip, in an intent, in a shop. The name alone is a riddle, and the explanation exists (statuses carry
// a DescriptionKey, cards and relics carry their rules text in the presentation manifest); it just was not
// reaching the hover. This is the layer that puts it there: ask it about an id, or hand it any piece of text
// and it names the terms that text uses.
//
// Built once per game document, off the blueprint rather than off a live fight, so it answers the same
// question on the map, in a shop and in combat.
public static class Glossary
{
    private static readonly Dictionary<string, string> ById = new(StringComparer.Ordinal);

    // Every term the glossary can spot inside a sentence, longest name first — so "Seal Intact" is matched
    // before the "Seal" inside it and the player is told the more specific thing.
    private static readonly List<(string Name, string Explanation)> Terms = [];

    private static readonly Dictionary<string, IReadOnlyList<string>> Memo = new(StringComparer.Ordinal);

    private static string _builtFor = "";

    public static void Build(RunBlueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var stamp = $"{blueprint.Statuses.Count}/{blueprint.Cards.Count}/{blueprint.Relics.Count}";
        if (_builtFor == stamp)
            return;
        _builtFor = stamp;
        ById.Clear();
        Terms.Clear();
        Memo.Clear();

        foreach (var status in blueprint.Statuses)
        {
            var name = Trimmed(status.NameKey) ?? status.Id;
            var rules = Trimmed(status.DescriptionKey);
            if (rules is null)
                continue;
            ById[status.Id] = $"{name} — {rules}";
            Terms.Add((name, $"{name} — {rules}"));
        }

        // Cards and relics explain themselves through the presentation manifest, which is where this game keeps
        // its rules text. A card named in an event ("gain a Citation") then hovers as its own rules.
        foreach (var (id, entry) in blueprint.Presentation.Cards)
            Note(id, blueprint.Cards.FirstOrDefault(c => c.Id == id)?.NameKey, entry.FlavorText);
        foreach (var (id, entry) in blueprint.Presentation.Relics)
            Note(id, blueprint.Relics.FirstOrDefault(r => r.Id == id)?.DisplayName, entry.FlavorText);

        Terms.Sort((a, b) => b.Name.Length.CompareTo(a.Name.Length));
    }

    private static void Note(string id, string? name, string? rules)
    {
        var text = Trimmed(rules);
        var title = Trimmed(name);
        if (text is null || title is null)
            return;
        ById.TryAdd(id, $"{title} — {text}");
        Terms.Add((title, $"{title} — {text}"));
    }

    // What one named thing is, by its id ("paperwork" ⇒ "Paperwork — At the end of its turn…"). Null if the
    // document never said.
    public static string? Of(string id) => ById.GetValueOrDefault(id);

    // Every term this piece of text USES, as explanation lines — the glossary for a card's rules, an intent, an
    // event's offer. Capped, because a tooltip nobody finishes reading explains nothing; the longest names win,
    // which is also the most specific reading of the sentence.
    public static IReadOnlyList<string> In(string? text, int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        // The same card, the same status and the same intent are looked up again on every redraw, and a lookup
        // reads every term the document knows. Remember the answer instead.
        var key = $"{limit}|{text}";
        if (Memo.TryGetValue(key, out var remembered))
            return remembered;
        var found = new List<string>();
        var claimed = new List<string>();
        foreach (var (name, explanation) in Terms)
        {
            if (found.Count >= limit)
                break;
            if (name.Length < 4 || !Mentions(text, name))
                continue;
            // A longer term already covering this one has spoken for it ("Seal Intact" swallows "Seal").
            if (claimed.Any(c => c.Contains(name, StringComparison.OrdinalIgnoreCase)))
                continue;
            claimed.Add(name);
            if (!found.Contains(explanation, StringComparer.Ordinal))
                found.Add(explanation);
        }
        Memo[key] = found;
        return found;
    }

    // A whole-word match, so "Seal" is not found inside "Sealed" and "Doubt" not inside "Doubtful".
    private static bool Mentions(string text, string name)
    {
        var from = 0;
        while (from < text.Length)
        {
            var at = text.IndexOf(name, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return false;
            var before = at == 0 || !char.IsLetter(text[at - 1]);
            var afterAt = at + name.Length;
            var after = afterAt >= text.Length || !char.IsLetter(text[afterAt]);
            if (before && after)
                return true;
            from = at + 1;
        }
        return false;
    }

    // One tooltip: what the thing itself says, then what the words in it mean.
    public static string Explain(string? own, string? termsFrom = null)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(own))
            lines.Add(own!.Trim());
        var terms = In(termsFrom ?? own);
        if (terms.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add("");
            lines.AddRange(terms);
        }
        return string.Join("\n", lines);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
