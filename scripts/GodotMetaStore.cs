using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace BnbGodot;

// The engine's cross-run profile (permanent unlocks, recipe discoveries, meta counters), persisted in
// Godot's per-user data dir — the Godot sibling of the Studio's FileMetaStore/BrowserMetaStore.
public sealed class GodotMetaStore : IMetaStore
{
    private const string Path = "user://metastate.json";

    public MetaState Load()
    {
        if (!Godot.FileAccess.FileExists(Path))
            return new MetaState();
        try
        {
            return MetaJson.FromJson(Godot.FileAccess.GetFileAsString(Path));
        }
        catch
        {
            return new MetaState(); // an unreadable profile must never block playing
        }
    }

    public void Save(MetaState state)
    {
        using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(MetaJson.ToJson(state));
    }
}
