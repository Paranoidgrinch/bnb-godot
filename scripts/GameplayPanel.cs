using System;
using Godot;

namespace BnbGodot;

// SETTINGS ▸ GAMEPLAY: the helps a player can switch off (GameplaySettings). Like Controls, it takes the settings
// panel's place in the same dialog and "Back" gives it back — reached from the title screen and from Esc alike.
// (Animation speed belongs here once the game has animations worth timing.)
public partial class GameplayPanel : PanelContainer
{
    private readonly Action _onBack;

    public GameplayPanel(Action onBack) => _onBack = onBack;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(560, 0);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);

        var title = new Label { Text = "Gameplay", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(title);

        column.AddChild(Switch("Ask before ending a turn with cards still playable",
            "End turn asks first while you still have Energy and a card you could play.",
            GameplaySettings.ConfirmEndTurn, on => GameplaySettings.ConfirmEndTurn = on));
        column.AddChild(Switch("Damage calculator",
            "Pointing at a card shows what it would do on the health bars, and the hero shows how much of the "
                + "enemies' turn would get through.",
            GameplaySettings.DamageCalculator, on => GameplaySettings.DamageCalculator = on));
        column.AddChild(Switch("Key names over the cards in hand",
            "The small number over each card: the key that picks it (Settings ▸ Controls).",
            GameplaySettings.KeyHints, on => GameplaySettings.KeyHints = on));

        var back = new Button { Text = "Back", CustomMinimumSize = new Vector2(120, 40),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        back.Pressed += () => _onBack();
        column.AddChild(back);
    }

    private static Control Switch(string label, string explanation, bool on, Action<bool> set)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        var check = new CheckBox { Text = label, ButtonPressed = on, TooltipText = explanation };
        check.Toggled += value => set(value);
        box.AddChild(check);
        var words = new Label { Text = explanation, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        words.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        words.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(words);
        return box;
    }
}
