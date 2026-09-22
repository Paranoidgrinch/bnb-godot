using Godot;

namespace BnbGodot;

// WHO IS PLAYING ON THIS MACHINE — a name the player gives once, the first time the game starts, and a random
// id drawn at the same moment. The name is what a person recognises in the run archive on GitHub; the id is
// what keeps two players who both typed "Max" apart, and what keeps one player one player after a rename.
//
// Its own file, not settings.cfg: the settings are something a player may throw away to fix a broken window,
// and doing that must not turn them into somebody else.
public static class PlayerIdentity
{
    private const string Path = "user://player.cfg";
    private const string Section = "player";
    public const int MaxNameLength = 24;

    private static bool _loaded;
    private static string? _name;
    private static string? _id;

    // Null until the player has given a name.
    public static string? Name
    {
        get
        {
            Load();
            return _name;
        }
    }

    public static string Id
    {
        get
        {
            Load();
            if (_id is null)
            {
                _id = Guid.NewGuid().ToString("N")[..12];
                Save();
            }
            return _id;
        }
    }

    public static bool HasName => !string.IsNullOrEmpty(Name);

    // What the player typed, made into a name: trimmed, at most MaxNameLength, and only characters that are
    // safe in a file path and a Discord message. Null when nothing usable is left.
    public static string? Clean(string? typed)
    {
        if (typed is null)
            return null;
        var kept = new string([.. typed.Trim().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.')]);
        kept = string.Join(' ', kept.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (kept.Length > MaxNameLength)
            kept = kept[..MaxNameLength].TrimEnd();
        return kept.Length == 0 ? null : kept;
    }

    public static bool SetName(string typed)
    {
        if (Clean(typed) is not { } name)
            return false;
        Load();
        _name = name;
        _id ??= Guid.NewGuid().ToString("N")[..12];
        Save();
        return true;
    }

    private static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        var file = new ConfigFile();
        if (file.Load(Path) != Error.Ok)
            return;
        _name = Clean(file.GetValue(Section, "name", "").AsString());
        var id = file.GetValue(Section, "id", "").AsString();
        _id = string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private static void Save()
    {
        var file = new ConfigFile();
        file.Load(Path);
        if (_name is not null)
            file.SetValue(Section, "name", _name);
        if (_id is not null)
            file.SetValue(Section, "id", _id);
        file.Save(Path);
    }
}
