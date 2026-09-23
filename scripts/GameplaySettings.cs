using Godot;

namespace BnbGodot;

// HOW THE GAME HELPS WHILE THE PLAYER PLAYS — the switches on Settings ▸ Gameplay. Each is a help a player may
// want gone once they know the game: the question before ending a turn with cards still playable, the damage
// calculator (what a card would do, what the enemies' turn would cost), and the key names over the hand. All on
// by default: a new player needs them, and a practised one turns them off in one place.
//
// Stored in `user://settings.cfg`, section `[gameplay]`. ⚠ Load before Save — see DisplaySettings.Save.
public static class GameplaySettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "gameplay";

    private static bool _loaded;
    private static readonly Dictionary<string, bool> Values = new(StringComparer.Ordinal);

    public static bool ConfirmEndTurn
    {
        get => Get("confirm_end_turn");
        set => Set("confirm_end_turn", value);
    }

    // The card preview on the health bars and the "Incoming" line under the hero.
    public static bool DamageCalculator
    {
        get => Get("damage_calculator");
        set => Set("damage_calculator", value);
    }

    // The key name over each card in hand.
    public static bool KeyHints
    {
        get => Get("key_hints");
        set => Set("key_hints", value);
    }

    private static bool Get(string key)
    {
        Load();
        return Values.GetValueOrDefault(key, true);
    }

    private static void Set(string key, bool value)
    {
        Load();
        Values[key] = value;
        var file = new ConfigFile();
        file.Load(Path);
        file.SetValue(Section, key, value);
        file.Save(Path);
    }

    private static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        var file = new ConfigFile();
        if (file.Load(Path) != Error.Ok || !file.HasSection(Section))
            return;
        foreach (var key in file.GetSectionKeys(Section))
            Values[key] = (bool)file.GetValue(Section, key, true);
    }
}
