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
        try
        {
            var json = Godot.FileAccess.GetFileAsString("res://content/game.roguedeck.json");
            Blueprint = RunJson.BlueprintFromJson(json, RunJson.CreateOptions());
            // What every named thing in the document MEANS, ready before the first screen asks.
            Glossary.Build(Blueprint);
        }
        catch (System.Exception ex)
        {
            HostError = $"Could not load the game document: {ex.Message}";
            GD.PushError(HostError);
        }
    }

    public string GameTitle => Blueprint?.Presentation.Game?.FlavorText ?? "RogueDeck game";

    public void StartNewRun(int seed, string? characterId = null, int? health = null)
    {
        Play?.Dispose();
        Play = new RunPlayback(OnPlayChanged, _metaStore);
        Play.Start(health is { } hp ? WithHealth(Blueprint, hp) : Blueprint, seed, interactive: true, characterId);
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
    public string? SaveRun()
    {
        var json = Play?.SaveJson();
        if (json is null)
            return Play?.Error ?? "No run to save.";
        using var file = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(json);
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
        EmitChanged();
        return Play.Error is null;
    }

    public void AbandonRun()
    {
        Play?.Dispose();
        Play = null;
        EmitChanged();
    }

    // The engine invokes onChanged inline (during our own input call) — defer so a burst of session +
    // driver notifications from one answer becomes redraws after the state has fully settled.
    private void OnPlayChanged() => CallDeferred(nameof(EmitChanged));

    private void EmitChanged() => EmitSignal(SignalName.StateChanged);
}
