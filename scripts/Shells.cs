using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace BnbGodot;

// ── SHELLS AND RUN MODIFIERS (user, 2026-11-17; bnb-content/META_PROGRESSION_PLAN.md §3) ─────────────────────
// Shells are the meta currency, and what they buy are RUN MODIFIERS, in the manner of Risk of Rain 2's artifacts:
// bought once, then switched on or off before every run. A modifier is bad, different or good, and its value
// moves what a won run pays:
//
//   a win pays   clamp(1 + the active modifiers' values, 0, 6)  +  the streak bonus
//   streak       every further win in a row with the same character pays 1 more (the 2nd +1, the 3rd +2, …);
//                a lost or abandoned run ends it
//   a loss pays  nothing
//
// Nothing is open at first: the first won run pays the first shell and opens the compartment (M7).
//
// Only a normal run counts: no tutorial, trial, table, probe — and today's run is played without modifiers, so
// that everybody plays the same one.
//
// How a run carries its modifiers: right before a run starts, the chosen ones are set as profile flags
// ("bnb.mod.run.<id>"); the engine mirrors the profile into the run as "meta.bnb.mod.run.<id>" run flags, which
// travel in the run's save, and the profile flags are taken out again at once. What a modifier DOES lives in the
// content, as a relic "mod_<id>" the run starts with (ForRun) — a modifier whose relic the game document does not
// define yet is shown, but cannot be bought.
public enum ModifierKind { Bad, Different, Good }

public sealed record RunModifier(string Id, string Name, ModifierKind Kind, int Value, int Price, string Rule)
{
    public string RelicId => "mod_" + Id;
}

public sealed record ShellPayout(int Base, int Modifiers, int Capped, int Streak)
{
    public int Total => Capped + Streak;
}

public static class Shells
{
    public const string BalanceCounter = "bnb.shells";
    public const string EarnedCounter = "bnb.shells.earned";
    public const string OpenFlag = "bnb.shells.open";
    private const string OwnedPrefix = "bnb.mod.owned.";
    private const string ChosenPrefix = "bnb.mod.chosen.";
    private const string RunPrefix = "bnb.mod.run.";
    private const string StreakPrefix = "bnb.shells.streak.";
    public const int Cap = 6;

    // The fifteen (Audit Season was cut, 2026-11-17: an elite rule on every normal fight is hardly buildable).
    public static readonly IReadOnlyList<RunModifier> Catalog =
    [
        new("overtime", "Overtime", ModifierKind.Bad, 2, 3, "Enemies start every fight with 1 Strength per act."),
        new("statute_of_limitations", "Statute of Limitations", ModifierKind.Bad, 1, 2, "From round 8 of a fight, enemies grow angry: +3 Strength each round."),
        new("spite_filing", "Spite Filing", ModifierKind.Bad, 1, 2, "Every enemy that falls deals 5 damage to you."),
        new("strict_filing", "Strict Filing", ModifierKind.Bad, 1, 1, "Card rewards offer 2 cards instead of 3."),
        new("inherited_debt", "Inherited Debt", ModifierKind.Bad, 1, 1, "You start with two curses in your deck."),
        new("glass_seal", "Glass Seal", ModifierKind.Different, 0, 2, "You deal double damage, but have half your max HP."),
        new("single_department", "Single Department", ModifierKind.Different, 0, 1, "Each act draws its normal fights from one enemy family only."),
        new("mixed_departments", "Mixed Departments", ModifierKind.Different, 0, 2, "Normal fights come from every act up to this one, raised to this act's strength."),
        new("paper_trail", "Paper Trail", ModifierKind.Different, 0, 1, "Double gold, double shop prices; removing a card in a shop costs nothing extra."),
        new("clerical_error", "Clerical Error", ModifierKind.Different, 0, 2, "Each turn one random card in your hand costs 0 and another costs 1 more."),
        new("sealed_envelopes", "Sealed Envelopes", ModifierKind.Different, 0, 2, "Card rewards are face down, but offer 4 instead of 3."),
        new("bureaucratic_sacrifice", "Bureaucratic Sacrifice", ModifierKind.Different, 0, 3, "No card rewards after fights; instead each enemy drops a card with a 25 % chance."),
        new("requisition_form", "Requisition Form", ModifierKind.Good, -1, 3, "Once per act, take a card of your choice from your pool."),
        new("second_opinion", "Second Opinion", ModifierKind.Good, -1, 2, "Every card reward can be drawn anew once."),
        new("signing_bonus", "Signing Bonus", ModifierKind.Good, -1, 2, "You start with a random rare relic."),
    ];

    public static RunModifier? Find(string id) => Catalog.FirstOrDefault(m => m.Id == id);

    // ── reading the profile ───────────────────────────────────────────────────────────────────────────────

    public static bool IsOpen(MetaState meta) => meta.HasFlag(OpenFlag);
    public static int Balance(MetaState meta) => meta.GetCounter(BalanceCounter);
    public static bool Owns(MetaState meta, RunModifier modifier) => meta.HasFlag(OwnedPrefix + modifier.Id);
    public static bool IsChosen(MetaState meta, RunModifier modifier) => meta.HasFlag(ChosenPrefix + modifier.Id);
    public static int Streak(MetaState meta, string character) => meta.GetCounter(StreakPrefix + character);

    // Built: the game document defines the modifier's relic.
    public static bool IsReady(RunBlueprint blueprint, RunModifier modifier) =>
        blueprint.Relics.Any(r => r.Id == modifier.RelicId);

    // What the next run would play with: owned, chosen and built.
    public static IReadOnlyList<RunModifier> Chosen(MetaState meta, RunBlueprint blueprint) =>
        [.. Catalog.Where(m => Owns(meta, m) && IsChosen(meta, m) && IsReady(blueprint, m))];

    public static ShellPayout Payout(IEnumerable<RunModifier> active, int streakBefore)
    {
        var modifiers = active.Sum(m => m.Value);
        return new ShellPayout(1, modifiers, Math.Clamp(1 + modifiers, 0, Cap), Math.Max(streakBefore, 0));
    }

    // ── the shop and the switches ─────────────────────────────────────────────────────────────────────────

    public static bool CanBuy(MetaState meta, RunBlueprint blueprint, RunModifier modifier) =>
        IsOpen(meta) && !Owns(meta, modifier) && IsReady(blueprint, modifier) && Balance(meta) >= modifier.Price;

    public static bool Buy(GameHost host, RunModifier modifier)
    {
        var meta = host.Meta;
        if (!CanBuy(meta, host.Blueprint, modifier))
            return false;
        meta.AddCounter(BalanceCounter, -modifier.Price);
        meta.SetFlag(OwnedPrefix + modifier.Id);
        host.SaveMeta(meta);
        ShellStatement.Add($"Bought {modifier.Name}", -modifier.Price);
        return true;
    }

    public static void Choose(GameHost host, RunModifier modifier, bool on)
    {
        var meta = host.Meta;
        if (on)
            meta.SetFlag(ChosenPrefix + modifier.Id);
        else
            meta.ClearFlag(ChosenPrefix + modifier.Id);
        host.SaveMeta(meta);
    }

    // ── a run with its modifiers ──────────────────────────────────────────────────────────────────────────

    // Right before the run starts: the modifiers it plays with, as profile flags the engine mirrors into it.
    // Today's run plays without any.
    public static IReadOnlyList<RunModifier> Arm(GameHost host, bool daily)
    {
        var meta = host.Meta;
        ClearRunFlags(meta);
        IReadOnlyList<RunModifier> armed = daily || !IsOpen(meta) ? [] : Chosen(meta, host.Blueprint);
        foreach (var modifier in armed)
            meta.SetFlag(RunPrefix + modifier.Id);
        host.SaveMeta(meta);
        return armed;
    }

    // Right after it started: the run holds them now, the profile must not hand them to the next one.
    public static void Disarm(GameHost host)
    {
        var meta = host.Meta;
        if (ClearRunFlags(meta))
            host.SaveMeta(meta);
    }

    private static bool ClearRunFlags(MetaState meta)
    {
        var armed = meta.Flags.Where(f => f.StartsWith(RunPrefix, StringComparison.Ordinal)).ToList();
        foreach (var flag in armed)
            meta.ClearFlag(flag);
        return armed.Count > 0;
    }

    // The blueprint a run starts from, with the armed modifiers' relics among everyone's starting relics.
    public static RunBlueprint ForRun(RunBlueprint blueprint, MetaState meta)
    {
        var relics = Catalog.Where(m => meta.HasFlag(RunPrefix + m.Id) && IsReady(blueprint, m)).Select(m => m.RelicId).ToList();
        if (relics.Count == 0)
            return blueprint;
        RunStart With(RunStart start) => start with { StartingRelics = [.. start.StartingRelics, .. relics] };
        return blueprint with
        {
            Start = With(blueprint.Start),
            Characters = [.. blueprint.Characters.Select(c => c with { Start = With(c.Start) })],
        };
    }

    // The modifiers a run plays with, read from the run itself.
    public static IReadOnlyList<RunModifier> OfRun(RunState run) =>
        [.. Catalog.Where(m => run.HasFlag(new RunFlagId("meta." + RunPrefix + m.Id)))];

    // ── the run's end ─────────────────────────────────────────────────────────────────────────────────────

    private static RunPlayback? _settled;

    public static ShellPayout? LastPayout { get; private set; }

    // Asked on every change (GameHost.EmitChanged), like CharacterUnlocks: the engine writes the profile it
    // loaded at the start of the run back when the run completes, so the payout is booked after that, once per run.
    public static void Observe(GameHost host)
    {
        if (host.IsTutorial || host.IsMultiplayer || host.IsProbe || host.IsTrial
            || host.Play is not { Session: { IsComplete: true } session } play || ReferenceEquals(_settled, play))
            return;
        _settled = play;
        var run = session.Run;
        Book(host, run.CharacterId ?? host.Blueprint.Characters.FirstOrDefault()?.Id ?? "", play.HeroName,
            run.Result == RunResult.Victory, OfRun(run));
    }

    // One finished run into the box: a win pays and lengthens the streak, anything else ends it.
    public static ShellPayout? Book(GameHost host, string character, string heroName, bool won, IReadOnlyList<RunModifier> modifiers)
    {
        var meta = host.Meta;
        ClearRunFlags(meta);
        if (!won)
        {
            LastPayout = null;
            var broken = Streak(meta, character);
            meta.SetCounter(StreakPrefix + character, 0);
            host.SaveMeta(meta);
            if (broken > 0)
                ShellStatement.Add($"Streak of {broken} ended ({heroName})", 0);
            return null;
        }
        var payout = Payout(modifiers, Streak(meta, character));
        meta.AddCounter(BalanceCounter, payout.Total);
        meta.AddCounter(EarnedCounter, payout.Total);
        meta.AddCounter(StreakPrefix + character, 1);
        var first = !IsOpen(meta);
        meta.SetFlag(OpenFlag);
        LastPayout = payout;
        host.SaveMeta(meta);
        var with = modifiers.Count == 0 ? "" : $" with {string.Join(", ", modifiers.Select(m => m.Name))}";
        var streak = payout.Streak > 0 ? $", streak of {Streak(meta, character)} (+{payout.Streak})" : "";
        ShellStatement.Add($"{(first ? "First won run" : "Won run")} as {heroName}{with}{streak}", payout.Total);
        return payout;
    }

    // A run given up ends the streak too.
    public static void Abandoned(GameHost host, string character)
    {
        var meta = host.Meta;
        var broken = Streak(meta, character);
        ClearRunFlags(meta);
        meta.SetCounter(StreakPrefix + character, 0);
        host.SaveMeta(meta);
        if (broken > 0)
            ShellStatement.Add($"Streak of {broken} ended: run given up", 0);
    }
}

// The statement of the shell compartment: what came in and what went out, newest first, the last 30.
public static class ShellStatement
{
    private const string Path = "user://shell-statement.json";
    private const int Keep = 30;

    public sealed record Entry(string Date, string Text, int Shells);

    public static IReadOnlyList<Entry> Read()
    {
        if (!Godot.FileAccess.FileExists(Path))
            return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Entry>>(Godot.FileAccess.GetFileAsString(Path)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Add(string text, int shells)
    {
        var entries = Read().ToList();
        entries.Insert(0, new Entry(DateTime.Now.ToString("dd.MM. HH:mm", System.Globalization.CultureInfo.InvariantCulture), text, shells));
        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(System.Text.Json.JsonSerializer.Serialize(entries.Take(Keep).ToList()));
    }
}
