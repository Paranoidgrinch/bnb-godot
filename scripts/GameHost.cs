using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace BnbGodot;

// The autoload that owns the game: loads the shipped blueprint once, holds the engine's RunPlayback
// (the reference host object — the frontend renders its state and forwards input), and turns its
// synchronous onChanged callback into a deferred Godot signal so one player answer coalesces into one
// redraw. Everything runs on the main thread; the engine's replay model has no background work.
public partial class GameHost : Godot.Node
{
    public static GameHost Instance { get; private set; } = null!;

    [Signal]
    public delegate void StateChangedEventHandler();

    public RunBlueprint Blueprint { get; private set; } = null!;
    public RunPlayback? Play { get; private set; }
    public string? HostError { get; private set; }
    // A short hash of the shipped content document — what a run recording names as the game it was played on.
    public string? ContentHash { get; private set; }

    private readonly GodotMetaStore _metaStore = new();
    private const string SavePath = "user://run-save.json";

    // The current cross-run profile (unlock flags, meta counters) — the title screen reads it to gate the
    // character roster. Freshly loaded each call so it reflects the last run's meta writes.
    public MetaState Meta => _metaStore.Load();

    public IReadOnlyList<RunCharacter> AvailableCharacters =>
        MetaProgression.AvailableCharacters(Blueprint, Meta);

    public void SaveMeta(MetaState meta) => _metaStore.Save(meta);

    public override void _Ready()
    {
        Instance = this;
        Controls.Install();
        // The window the player last chose, before anything is drawn in it. An interface scale applies even
        // under a probe (it is a canvas property); the window itself is left alone there — see DisplaySettings.
        DisplaySettings.Apply(GetTree(),
            skipWindow: OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--smoke", System.StringComparison.Ordinal)
                || a.StartsWith("--sim", System.StringComparison.Ordinal)));
        try
        {
            var json = Godot.FileAccess.GetFileAsString("res://content/game.roguedeck.json");
            Blueprint = RunJson.BlueprintFromJson(json, RunJson.CreateOptions());
            ContentHash = RunRecorder.ContentHash(json);
            // What every named thing in the document MEANS, ready before the first screen asks.
            Glossary.Build(Blueprint);
        }
        catch (System.Exception ex)
        {
            HostError = $"Could not load the game document: {ex.Message}";
            GD.PushError(HostError);
        }
        // Runs finished while offline are sent now.
        _ = RunLog.Flush(this);
    }

    public string GameTitle => Blueprint?.Presentation.Game?.FlavorText ?? "RogueDeck game";

    // `mapGenerator` is MapGenerators.RuleBased or .Strategic; null means the player's remembered preference.
    // It is passed HERE and nowhere else: from this moment it belongs to the run, travels in its save, and a
    // resume reads it back out of the save rather than out of the menu (see RunPreferences).
    // THE TUTORIAL: the game's own fixed walk (RunBlueprint.ForTutorial) — same content, six city rooms. It is a
    // lesson, not a run: never saved (it must not overwrite the player's real save), never recorded or uploaded,
    // never in the history. The coach reads IsTutorial to know it is on.
    public bool IsTutorial { get; private set; }

    public bool HasTutorial => Blueprint?.Tutorial is not null;

    // `characterId`: who is learning — a character with a tutorial of its own (the Hedge Witch) walks that one.
    public void StartTutorial(string? characterId = null)
    {
        Play?.Dispose();
        Play = new RunPlayback(OnPlayChanged, _metaStore);
        IsTutorial = true;
        IsTrial = false;
        IsMultiplayer = false;
        Play.Start(Blueprint.ForTutorial(characterId), seed: 1, interactive: true);
        EmitChanged();
    }

    // A RUN PLAYED AT A TABLE (multiplayer, NetSession): every machine starts it the same way — the host's seed,
    // the party in seat order, the design's own maps, and the HOST'S profile, because the profile becomes run
    // flags and two machines on two profiles would walk two runs. It touches nobody's own profile, it is not
    // the player's save, and it is not recorded: the table, not this machine, is where it lives.
    public bool IsMultiplayer { get; private set; }

    public void StartPartyRun(int seed, IReadOnlyList<string> party, MetaStateData meta)
    {
        IsTrial = false;
        IsTutorial = false;
        IsMultiplayer = true;
        Play?.Dispose();
        var profile = new MemoryMetaStore();
        profile.Save(MetaState.FromSnapshot(meta));
        Play = new RunPlayback(OnPlayChanged, profile);
        Play.Start(Blueprint, seed, interactive: true, party[0], MapGenerators.Strategic, party);
        EmitChanged();
    }

    // A table's run taken up again from the host's save of it (NetSession): every machine resumes the same save on
    // the same profile, so they stand together exactly where the table stopped.
    public void ResumePartyRun(string saveJson, MetaStateData meta)
    {
        IsTrial = false;
        IsTutorial = false;
        IsMultiplayer = true;
        Play?.Dispose();
        var profile = new MemoryMetaStore();
        profile.Save(MetaState.FromSnapshot(meta));
        Play = new RunPlayback(OnPlayChanged, profile);
        Play.Resume(Blueprint, RunSaveJson.FromJson(saveJson), interactive: true);
        EmitChanged();
    }

    // ONE FIGHT, FOR A PICTURE (`--smoke-encounter=<id>`): the named encounter as the only room of a lesson-shaped
    // map, on a body that survives it. The room is realised from the act that fields the encounter, so it carries
    // the act's own rules; a probe and never a run — nothing is recorded or saved.
    public void StartSingleFight(string encounterId)
    {
        var act = Blueprint.Acts?.FirstOrDefault(a => a.MapGeneration?.Encounters?.ByRole.Values
            .Any(pool => pool.Any(e => e.Encounter.Value == encounterId)) == true);
        var spec = act?.MapGeneration ?? Blueprint.MapGeneration
            ?? throw new System.InvalidOperationException("no act fields " + encounterId);
        var kind = spec.Encounters!.ByRole.First(pair => pair.Value.Any(e => e.Encounter.Value == encounterId)).Key;
        var content = MapNodeRealizer.Realize(spec, kind, new EncounterId(encounterId));
        var map = new RunMap([new RogueDeck.Run.Node(new NodeId("probe"), content.Type, content.Payload, content.Tags)]);
        var start = Blueprint.Start with { MaxHealth = 400, StartingHealth = 400 };
        IsProbe = true;
        IsTrial = false;
        IsTutorial = false;
        IsMultiplayer = false;
        Play?.Dispose();
        Play = new RunPlayback(OnPlayChanged, _metaStore);
        Play.Start((Blueprint with { Tutorial = new RunTutorial(map, start) }).ForTutorial(), seed: 7, interactive: true);
        EmitChanged();
    }

    // ── THE ENCOUNTER TRIAL (bnb-content EncounterTrial, user 2026-10-31) ────────────────────────────────────
    // Every fight of an act in stage order, the Lieutenant at full health before each, the bosses last — a fixed
    // walk the game document carries as Presentation.Game.Extra["trial:<act>"]. It is an instrument, not a run:
    // it saves to a slot of its own (the player's run stays where it is), plays on a copy of the profile (a
    // trial won clears nothing), is never recorded, and writes its own notebook instead (TrialLog).
    public bool IsTrial { get; private set; }
    public int TrialAct { get; private set; } = 1;

    // ONE FIGHT OF A TRIAL ON ITS OWN (user, 2026-11-05): the encounter a single-encounter trial walks to, or null
    // for the full act. Its walk is Extra["trial.encounters:<act>"] — the packages the full trial hands out before
    // that fight's stage, then the fight — and it keeps a save slot, a retry and a notebook of its own
    // (TrialLog), so it never touches a full trial in progress.
    public string? TrialEncounter { get; private set; }

    public bool IsTrialEncounter => IsTrial && TrialEncounter is not null;

    // One entry of Extra["trial.encounters:<act>"] (bnb-content EncounterTrial.Single). `Kind`: normal, multi,
    // elite or boss; `Stage`: the act's stage, the bosses' the one after the last.
    public sealed record TrialSingle(string Encounter, string Name, int Stage, string Kind, RunTutorial Walk);

    private static string TrialSavePath(int act, bool single = false) => TrialLog.SavePath(act, single);

    public bool HasTrial(int act) => Blueprint?.Presentation.Game?.Extra.ContainsKey($"trial:{act}") == true;

    public bool HasTrialSave(int act, bool single = false) => Godot.FileAccess.FileExists(TrialSavePath(act, single));

    public bool HasTrialRetry(int act, bool single = false) => Godot.FileAccess.FileExists(TrialLog.PrefightPath(act, single));

    // Read once per act and game document: Act IV's list is a few hundred kilobytes.
    private readonly Dictionary<int, (RunBlueprint Of, IReadOnlyList<TrialSingle> Singles)> _trialSingles = [];

    public IReadOnlyList<TrialSingle> TrialSingles(int act)
    {
        if (_trialSingles.TryGetValue(act, out var known) && ReferenceEquals(known.Of, Blueprint))
            return known.Singles;
        IReadOnlyList<TrialSingle> singles = Blueprint?.Presentation.Game?.Extra.GetValueOrDefault($"trial.encounters:{act}") is { } json
            ? System.Text.Json.JsonSerializer.Deserialize<List<TrialSingle>>(json, RunJson.CreateOptions())!
            : [];
        _trialSingles[act] = (Blueprint!, singles);
        return singles;
    }

    // The encounter the single-encounter slot of an act holds, if it holds one.
    public TrialSingle? SavedTrialSingle(int act) =>
        HasTrialSave(act, single: true) && Godot.FileAccess.FileExists(TrialLog.EncounterOfSavePath(act))
            && Godot.FileAccess.GetFileAsString(TrialLog.EncounterOfSavePath(act)).Trim() is { Length: > 0 } id
            ? TrialSingles(act).FirstOrDefault(s => s.Encounter == id)
            : null;

    // The walk in place of the acts, the Lieutenant alone (RunBlueprint.ForTutorial does the same for a lesson).
    private RunBlueprint TrialBlueprint(int act, string? encounter = null)
    {
        var trial = encounter is null
            ? System.Text.Json.JsonSerializer.Deserialize<RunTutorial>(
                Blueprint.Presentation.Game!.Extra[$"trial:{act}"], RunJson.CreateOptions())!
            : TrialSingles(act).First(s => s.Encounter == encounter).Walk;
        return (Blueprint with
        {
            Map = trial.Map,
            Acts = null,
            MapGeneration = null,
            StrategicMapGeneration = null,
            Tutorial = null,
        }).AsLoneFighter(trial.Start!, TrialCharacter);
    }

    public const string TrialCharacter = "lieutenant";

    private RunPlayback TrialPlayback()
    {
        var profile = new MemoryMetaStore();
        profile.Save(Meta);
        return new RunPlayback(OnPlayChanged, profile);
    }

    // `encounter`: one fight of the act on its own, after the packages before its stage; null walks the whole act.
    public void StartTrial(int act = 1, string? encounter = null)
    {
        IsTutorial = false;
        IsMultiplayer = false;
        IsProbe = false;
        IsTrial = true;
        TrialAct = act;
        TrialEncounter = encounter;
        Play?.Dispose();
        Play = TrialPlayback();
        Play.Start(TrialBlueprint(act, encounter), RunSeeds.Random(), interactive: true, TrialCharacter);
        Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TrialLog.PrefightPath(act, encounter is not null)));
        if (encounter is not null)
        {
            Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TrialSavePath(act, single: true)));
            using var which = Godot.FileAccess.Open(TrialLog.EncounterOfSavePath(act), Godot.FileAccess.ModeFlags.Write);
            which?.StoreString(encounter);
        }
        EmitChanged();
    }

    // Take the trial up again: from its own save, or — `retry` — from the first turn of the fight it was lost in.
    // `encounter`: the single-encounter trial of that fight, on its own slot.
    public bool ResumeTrial(int act = 1, bool retry = false, string? encounter = null)
    {
        var single = encounter is not null;
        var path = retry ? TrialLog.PrefightPath(act, single) : TrialSavePath(act, single);
        if (!Godot.FileAccess.FileExists(path) || (single && TrialSingles(act).All(s => s.Encounter != encounter)))
            return false;
        var save = RunSaveJson.FromJson(Godot.FileAccess.GetFileAsString(path));
        IsTutorial = false;
        IsMultiplayer = false;
        IsProbe = false;
        IsTrial = true;
        TrialAct = act;
        TrialEncounter = encounter;
        Play?.Dispose();
        Play = TrialPlayback();
        Play.Resume(TrialBlueprint(act, encounter), save, interactive: true);
        EmitChanged();
        return Play.Error is null;
    }

    // A run started with a probe's body (the marathon, the golden set) earns nothing for the player's profile:
    // `--count-unlocks` lets a probe check the unlocks on purpose.
    public bool IsProbe { get; private set; }

    public void StartNewRun(int seed, string? characterId = null, int? health = null, string? mapGenerator = null)
    {
        var args = OS.GetCmdlineUserArgs();
        IsProbe = (health is not null || args.Any(a => a.StartsWith("--sim", System.StringComparison.Ordinal)
                || a.StartsWith("--smoke", System.StringComparison.Ordinal)))
            && !args.Contains("--count-unlocks");
        IsTrial = false;
        IsTutorial = false;
        IsMultiplayer = false;
        Play?.Dispose();
        Play = new RunPlayback(OnPlayChanged, _metaStore);
        var generator = mapGenerator ?? RunPreferences.MapGenerator;
        // The profile as the run starts on it, read BEFORE the run does — unlocks become run flags, so a replay
        // needs exactly the profile this run saw.
        var meta = Meta;
        Play.Start(health is { } hp ? WithHealth(Blueprint, hp) : Blueprint, seed, interactive: true, characterId,
            generator);
        if (Play.Error is null && health is null)
            RunLog.Begin(Play, seed, characterId, generator, meta, ContentHash);
        EmitChanged();
    }

    // A body that survives the whole game, for the headless marathon check only: a walk that dies in the first
    // act never renders the second one, and what that check is for is proving the SCREENS hold up all the way
    // to the last boss. Never reachable from the title — it takes an argument no player passes.
    private static RunBlueprint WithHealth(RunBlueprint blueprint, int health)
    {
        RunStart Raise(RunStart start) => start with { MaxHealth = health, StartingHealth = health };
        return blueprint with
        {
            Start = Raise(blueprint.Start),
            Characters = [.. blueprint.Characters.Select(c => c with { Start = Raise(c.Start) })],
        };
    }

    // The seat this machine plays a fight from (CombatSeats): the solo driver, or this machine's member's seat at
    // a party fight — the same object for as long as the fight driver is, so a screen may hold on to it.
    private PartyCombatSeat? _partySeat;

    public ICombatSeat? SeatOf(RunPlayback play)
    {
        if (play.PartyCombatDriver is not { } party)
            return play.CombatDriver;
        var member = new RogueDeck.Core.Combat.CombatantId(
            NetSession.CombatantOf(System.Math.Max(0, NetSession.Instance.LocalSeat)));
        if (_partySeat is null || _partySeat.Member != member || !ReferenceEquals(_partyDriver, party))
        {
            _partySeat = new PartyCombatSeat(party, member, NetSession.Instance.Submit);
            _partyDriver = party;
        }
        return _partySeat;
    }

    private PartyInteractiveCombatDriver? _partyDriver;

    public bool HasSave => Godot.FileAccess.FileExists(SavePath);

    // Save the live run (valid only at a clean interlude — the engine guards this and reports why not).
    // THE RUN SAVES ITSELF. Called at every point the player has settled something — a turn handed over, an
    // option taken, a room chosen — because the engine can now capture a fight mid-flight, so there is no
    // longer a moment the game has to ask them to stop at. Failures are swallowed on purpose: an autosave is a
    // convenience, and a run must never be interrupted to be told one did not land. The manual Save run button
    // still reports, because that one was asked for.
    public void AutoSave() => SaveRun();

    public string? SaveRun()
    {
        if (IsTutorial || IsMultiplayer)
            return null; // a lesson is not a run, nor is the table's: the player's real save stays where it is
        if (IsTrial)
        {
            // The trial's own slot; a trial that has ended keeps nothing to continue.
            if (Play?.Session?.Run is { Result: not RunResult.Ongoing })
                return null;
            if (Play?.SaveJson() is { } trial)
                using (var file = Godot.FileAccess.Open(TrialSavePath(TrialAct, IsTrialEncounter), Godot.FileAccess.ModeFlags.Write))
                    file?.StoreString(trial);
            return null;
        }
        var json = Play?.SaveJson();
        if (json is null)
            return Play?.Error ?? "No run to save.";
        using (var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write))
            file?.StoreString(json);
        RunLog.Saved();
        return null;
    }

    public bool ResumeRun()
    {
        if (!HasSave)
            return false;
        var save = RunSaveJson.FromJson(Godot.FileAccess.GetFileAsString(SavePath));
        IsTrial = false;
        IsTutorial = false;
        IsMultiplayer = false;
        Play?.Dispose();
        Play = new RunPlayback(OnPlayChanged, _metaStore);
        Play.Resume(Blueprint, save, interactive: true);
        if (Play.Error is null)
            RunLog.Resume(Play, save.RandomSeed);
        EmitChanged();
        return Play.Error is null;
    }

    // Drops the run in play without a word to the run log or the history — for a probe that started one only to
    // look at it.
    public void AbandonRunInMemory()
    {
        Play?.Dispose();
        Play = null;
    }

    public void AbandonRun()
    {
        RunLog.Abandon();
        Play?.Dispose();
        Play = null;
        EmitChanged();
    }

    // The engine invokes onChanged inline (during our own input call) — defer so a burst of session +
    // driver notifications from one answer becomes redraws after the state has fully settled.
    private void OnPlayChanged()
    {
        RunTally.Observe(Play);
        if (IsTrial)
            TrialLog.Observe(this);
        CallDeferred(nameof(EmitChanged));
    }

    private void EmitChanged()
    {
        // The history first: RunLog.Observe lets go of the recording the history takes its room count from.
        // A tutorial is neither history nor a recording.
        if (!IsTrial && !IsTutorial && !IsMultiplayer)
        {
            RunHistory.Observe(Play, RunLog.Current);
            RunLog.Observe(this);
            CharacterUnlocks.Observe(this);
        }
        // A table's run is the host's to send when it ends (RunLog.BeginTable).
        if (IsMultiplayer)
            RunLog.ObserveTable(this);
        EmitSignal(SignalName.StateChanged);
    }
}
