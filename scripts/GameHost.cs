using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

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
    public void StartNewRun(int seed, string? characterId = null, int? health = null, string? mapGenerator = null)
    {
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
        Play?.Dispose();
        Play = new RunPlayback(OnPlayChanged, _metaStore);
        Play.Resume(Blueprint, save, interactive: true);
        if (Play.Error is null)
            RunLog.Resume(Play, save.RandomSeed);
        EmitChanged();
        return Play.Error is null;
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
    private void OnPlayChanged() => CallDeferred(nameof(EmitChanged));

    private void EmitChanged()
    {
        // The history first: RunLog.Observe lets go of the recording the history takes its room count from.
        RunHistory.Observe(Play, RunLog.Current);
        RunLog.Observe(this);
        EmitSignal(SignalName.StateChanged);
    }
}
