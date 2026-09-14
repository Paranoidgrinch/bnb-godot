using Godot;
using RogueDeck.Run;

namespace BnbGodot;

// WHAT THE PLAYER CHOSE LAST TIME THEY STARTED A RUN — today that is one thing, the map generator.
//
// It lives in `user://settings.cfg` beside the display settings rather than in the meta store, and the
// difference matters. The meta store is the ENGINE's cross-run profile: what this player has unlocked, what
// they have discovered, what the game has learned about them. Which generator a new run uses is none of those
// — it is a preference about the next run, like a window size, and putting it in the profile would mean the
// engine's own save model grew a field about a frontend dialog.
//
// AND IT IS ONLY A DEFAULT FOR THE NEXT RUN. The generator a run is actually laid out with belongs to the run
// and travels in its save (`RunSaveData.MapGenerator`), because a BnB map is regenerated on resume rather than
// stored. If this preference decided what a RESUMED run got, a player who closed the game standing in front of
// an elite would come back to a shop — which is the whole reason the choice is part of the run at all.
public static class RunPreferences
{
    private const string Path = "user://settings.cfg";
    private const string Section = "run";

    private static bool _loaded;
    private static string _mapGenerator = MapGenerators.Strategic;

    // The generator a NEW run starts on. v0.0.1 is the default because it is what a BnB act now is
    // (docs/bnb-act-map-specs.md); v0.0.0 stays offered, and stays what an old save resumes on.
    public static string MapGenerator
    {
        get
        {
            Load();
            return _mapGenerator;
        }
    }

    public static void SetMapGenerator(string generator)
    {
        Load();
        _mapGenerator = MapGenerators.IsStrategic(generator) ? MapGenerators.Strategic : MapGenerators.RuleBased;
        Save();
    }

    private static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        var file = new ConfigFile();
        if (file.Load(Path) != Error.Ok)
            return;
        var stored = file.GetValue(Section, "map_generator", MapGenerators.Strategic).AsString();
        // Anything unrecognised reads as the default rather than as itself: a settings file edited by hand (or
        // written by a future build) must never hand the engine a generator it has no rules for.
        _mapGenerator = stored == MapGenerators.RuleBased ? MapGenerators.RuleBased : MapGenerators.Strategic;
    }

    private static void Save()
    {
        var file = new ConfigFile();
        // Load first: the file is shared with DisplaySettings, and writing a fresh one would silently drop
        // the window the player chose.
        file.Load(Path);
        file.SetValue(Section, "map_generator", _mapGenerator);
        file.Save(Path);
    }

    // The two labels, in plain English. "v0.0.1" tells a playtester nothing whatever about what they are
    // choosing, and the one thing this dialog must not do is make the choice feel like a version number.
    public static string Title(string generator) => generator == MapGenerators.RuleBased
        ? "Guaranteed routes (v0.0.0)"
        : "Real choices (v0.0.1)";

    public static string Blurb(string generator) => generator == MapGenerators.RuleBased
        ? "Every way through an act holds the same rooms: the fights, the campfires and the shops are promised "
            + "to every route. Safe, even, and the way every run worked until now."
        : "Each way through an act is its own way. One side may hold three elites and no shop, the other two "
            + "campfires and a jar — and no walk through the act is a stroll.";
}
