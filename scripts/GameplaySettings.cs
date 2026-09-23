using Godot;

namespace BnbGodot;

// HOW THE GAME TALKS TO THE PLAYER WHILE THEY PLAY — today one question: whether ending a turn with cards still
// playable asks first. On by default: the turn thrown away by a stray click on End turn is the most common
// avoidable loss in the genre, and a player who finds the question in their way turns it off in one place —
// the settings, or the question's own "don't ask again".
//
// Stored in `user://settings.cfg`, section `[gameplay]`. ⚠ Load before Save — see DisplaySettings.Save.
public static class GameplaySettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "gameplay";

    private static bool _loaded;
    private static bool _confirmEndTurn = true;

    public static bool ConfirmEndTurn
    {
        get
        {
            Load();
            return _confirmEndTurn;
        }
        set
        {
            Load();
            _confirmEndTurn = value;
            var file = new ConfigFile();
            file.Load(Path);
            file.SetValue(Section, "confirm_end_turn", value);
            file.Save(Path);
        }
    }

    private static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        var file = new ConfigFile();
        if (file.Load(Path) == Error.Ok)
            _confirmEndTurn = (bool)file.GetValue(Section, "confirm_end_turn", true);
    }
}
