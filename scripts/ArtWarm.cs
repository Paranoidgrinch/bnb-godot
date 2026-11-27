using Godot;

namespace BnbGodot;

// ── THE PICTURES, LOADED BEFORE THEY ARE NEEDED (user, 2026-11-27: "das Spiel flüssiger") ─────────────────────
// A picture used to be read from disk the moment a screen first showed it: the first fight against a new enemy,
// the first reward with a new card, each act's first room — and that screen's redraw took 150-450 ms instead of
// the usual 12 (measured with BNB_REBUILD_MS=1). Every picture of the game is small (cards, enemies, relics and
// the fight rooms together are a few MB), so all of them are asked for at start, on Godot's loader threads,
// and held here so the resource cache keeps them. A screen that wants one before its load has finished simply
// waits for that one, as it did before.
public static class ArtWarm
{
    private static readonly List<string> Pending = [];
    private static readonly List<Resource> Held = [];
    private static bool _started;

    public static void Start()
    {
        if (_started || DisplayServer.GetName().Contains("headless", StringComparison.OrdinalIgnoreCase))
            return;
        _started = true;
        foreach (var folder in new[] { "cards", "enemies", "relics", "backgrounds", "characters" })
            Request($"res://assets/art/{folder}");
    }

    private static void Request(string folder)
    {
        using var dir = DirAccess.Open(folder);
        if (dir is null)
            return;
        foreach (var file in dir.GetFiles())
        {
            // Only what Godot imported is a picture it can load (a folder of sources marked .gdignore is not); the
            // .import file is there in the editor and in an exported game alike.
            if (!file.EndsWith(".png.import", StringComparison.Ordinal))
                continue;
            var name = file[..^".import".Length];
            var path = $"{folder}/{name}";
            if (Pending.Contains(path) || ResourceLoader.HasCached(path))
                continue;
            if (ResourceLoader.LoadThreadedRequest(path, "Texture2D") == Error.Ok)
                Pending.Add(path);
        }
        foreach (var sub in dir.GetDirectories())
            Request($"{folder}/{sub}");
    }

    // Called every frame by the host until nothing is pending: what has arrived is taken in and held.
    public static void Collect()
    {
        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            var status = ResourceLoader.LoadThreadedGetStatus(Pending[i]);
            if (status == ResourceLoader.ThreadLoadStatus.InProgress)
                continue;
            if (status == ResourceLoader.ThreadLoadStatus.Loaded && ResourceLoader.LoadThreadedGet(Pending[i]) is { } picture)
                Held.Add(picture);
            Pending.RemoveAt(i);
        }
    }

    public static bool Busy => Pending.Count > 0;

    // At quit: what is held is let go, so the engine does not report it as still in use.
    public static void Release()
    {
        Held.Clear();
        Pending.Clear();
    }
}
