using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BnbGodot;

// THE KEYS, and the one place they are decided. Every shortcut in the game is a Godot input ACTION named here,
// with a default key; the player can put any of them on another key in Settings ▸ Controls, and the choice is
// kept in `user://settings.cfg` (section `[controls]`) beside the window and the volume. A screen never asks
// "was M pressed" — it asks "was the map asked for", so a rebinding reaches every screen without any of them
// knowing it happened.
//
// ONE KEY PER ACTION, on purpose. A second key per action doubles the dialog for a feature almost nobody uses,
// and "which of my two keys did I just overwrite" is a question the dialog would then have to answer.
// Putting an action on a key another action already has SWAPS them, so no action is ever left without a key
// and no key ever does two things.
//
// ⚠ Esc and the right mouse button are NOT here: Esc is how every dialog in the game — this one included — is
// closed, and a key that could be rebound away from that is a way to lock yourself into a menu. They are
// listed in the dialog as fixed so the player still reads them.
//
// ⚠ Both writers of settings.cfg Load() before they Save() — see DisplaySettings.Save.
public static class Controls
{
    public sealed record Binding(string Action, string Label, Key Default);

    public const string EndTurn = "bnb_end_turn";
    public const string TargetPrevious = "bnb_target_previous";
    public const string TargetNext = "bnb_target_next";
    public const string Confirm = "bnb_confirm";
    public const string ViewDraw = "bnb_view_draw";
    public const string ViewDiscard = "bnb_view_discard";
    public const string ViewExhaust = "bnb_view_exhaust";
    public const string ViewDeck = "bnb_view_deck";
    public const string Map = "bnb_map";
    public const string Log = "bnb_log";

    public const int CardKeys = 10;
    public static string Card(int index) => $"bnb_card_{index + 1}";
    public static bool IsCardKey(string action) => action.StartsWith("bnb_card_", StringComparison.Ordinal);

    private static readonly Key[] CardDefaults =
        [Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7, Key.Key8, Key.Key9, Key.Key0];

    // In the order the dialog lists them: the fight first, because that is where the keys earn their keep.
    public static readonly IReadOnlyList<Binding> All =
    [
        .. Enumerable.Range(0, CardKeys).Select(i => new Binding(Card(i), $"Pick card {i + 1} in your hand", CardDefaults[i])),
        new(TargetPrevious, "Aim at the enemy to the left", Key.Left),
        new(TargetNext, "Aim at the enemy to the right", Key.Right),
        new(Confirm, "Play the picked card at the aimed enemy", Key.Space),
        new(EndTurn, "End turn", Key.E),
        new(ViewDraw, "Look at the draw pile", Key.A),
        new(ViewDiscard, "Look at the discard pile", Key.S),
        new(ViewExhaust, "Look at the exhausted cards", Key.X),
        new(ViewDeck, "Look at your deck", Key.D),
        new(Map, "Show the map", Key.M),
        new(Log, "Show the run's log", Key.L),
    ];

    // Shown in the dialog, never rebindable (see above).
    public static readonly IReadOnlyList<(string Key, string Label)> Fixed =
    [
        ("Esc", "Menu · close what is open · put a picked card back"),
        ("Right mouse", "Put a picked card back"),
    ];

    private const string Path = "user://settings.cfg";
    private const string Section = "controls";

    private static readonly Dictionary<string, Key> Current = new(StringComparer.Ordinal);
    private static bool _installed;

    // Registers every action with its key. Called once, by the autoload, before the first screen exists.
    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;
        foreach (var binding in All)
            Current[binding.Action] = binding.Default;

        var file = new ConfigFile();
        if (file.Load(Path) == Error.Ok)
            foreach (var binding in All)
            {
                var stored = (long)file.GetValue(Section, binding.Action, (long)binding.Default);
                // A key nothing else is on, or it stays at its default: a file edited by hand must never leave
                // two actions on one key.
                if (Enum.IsDefined(typeof(Key), stored) && stored != (long)Key.None)
                    Current[binding.Action] = (Key)stored;
            }
        if (Current.Values.Distinct().Count() != Current.Count)
            foreach (var binding in All)
                Current[binding.Action] = binding.Default;

        foreach (var binding in All)
            Apply(binding.Action);
    }

    public static Key KeyOf(string action)
    {
        Install();
        return Current[action];
    }

    public static string KeyName(string action) => KeyName(KeyOf(action));

    public static string KeyName(Key key) => key switch
    {
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        _ => OS.GetKeycodeString(key),
    };

    // Puts `action` on `key`. If another action had that key, it takes this action's old one.
    public static void Rebind(string action, Key key)
    {
        Install();
        var old = Current[action];
        if (old == key)
            return;
        var holder = Current.FirstOrDefault(pair => pair.Value == key).Key;
        Current[action] = key;
        Apply(action);
        if (holder is not null)
        {
            Current[holder] = old;
            Apply(holder);
        }
        Save();
    }

    public static void ResetAll()
    {
        Install();
        foreach (var binding in All)
        {
            Current[binding.Action] = binding.Default;
            Apply(binding.Action);
        }
        Save();
    }

    // Whether this key may be bound at all: Esc is the way out of every dialog, and a bare modifier is not a key.
    public static bool Bindable(Key key) =>
        key is not (Key.None or Key.Escape or Key.Shift or Key.Ctrl or Key.Alt or Key.Meta or Key.Capslock);

    private static void Apply(string action)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);
        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, new InputEventKey { Keycode = Current[action] });
    }

    private static void Save()
    {
        var file = new ConfigFile();
        file.Load(Path);
        foreach (var binding in All)
            file.SetValue(Section, binding.Action, (long)Current[binding.Action]);
        file.Save(Path);
    }
}
