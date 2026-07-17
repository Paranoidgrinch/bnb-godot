using Godot;

namespace BnbGodot;

// The Moonvine Forge design tokens (mirrored from the Studio's studio.css) and a programmatic Theme
// built from them — panels, buttons, labels share one look without hand-maintained .tres files.
// Screens grab StyleBox helpers for the few custom shapes (card frames, enemy panels).
public static class MoonvineTheme
{
    public static readonly Color Bg = new("06070b");
    public static readonly Color BgPanel = new("0d0f15");
    public static readonly Color BgPanelStrong = new("090a0f");
    public static readonly Color BgControl = new("141821");
    public static readonly Color Text = new("f1f0ec");
    public static readonly Color TextSoft = new("d8d6cf");
    public static readonly Color TextMuted = new("a9a79f");
    public static readonly Color Accent = new("a8c88a");
    public static readonly Color AccentLight = new("d7efc3");
    public static readonly Color AccentDark = new("76965f");
    public static readonly Color Danger = new("e08a8a");
    public static readonly Color Warning = new("e0c98a");

    // Intent-kind accents for enemy telegraphs.
    public static Color IntentColor(RogueDeck.Scenario.Authoring.IntentKind kind) => kind switch
    {
        RogueDeck.Scenario.Authoring.IntentKind.Attack => Danger,
        RogueDeck.Scenario.Authoring.IntentKind.Defend => new Color("8ab6e0"),
        RogueDeck.Scenario.Authoring.IntentKind.Buff => Accent,
        RogueDeck.Scenario.Authoring.IntentKind.Debuff => new Color("c79ae0"),
        _ => TextMuted,
    };

    public static Color RarityColor(string? rarity) => rarity switch
    {
        "uncommon" => new Color("8ab6e0"),
        "rare" => new Color("e0c98a"),
        "starter" => TextMuted,
        _ => TextSoft,
    };

    public static StyleBoxFlat Panel(Color? bg = null, Color? border = null, int radius = 8) => new()
    {
        BgColor = bg ?? BgPanel,
        BorderColor = border ?? new Color(Accent, 0.3f),
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius,
        ContentMarginLeft = 12,
        ContentMarginRight = 12,
        ContentMarginTop = 8,
        ContentMarginBottom = 8,
    };

    private static Theme? _theme;

    public static Theme Build()
    {
        if (_theme is not null)
            return _theme;
        var theme = new Theme();

        theme.SetStylebox("panel", "PanelContainer", Panel());
        theme.SetStylebox("panel", "Panel", Panel(BgPanelStrong));

        var button = Panel(BgControl, radius: 999);
        var buttonHover = Panel(new Color(Accent, 0.15f), Accent, 999);
        var buttonDisabled = Panel(BgPanelStrong, new Color(TextMuted, 0.2f), 999);
        theme.SetStylebox("normal", "Button", button);
        theme.SetStylebox("hover", "Button", buttonHover);
        theme.SetStylebox("pressed", "Button", buttonHover);
        theme.SetStylebox("disabled", "Button", buttonDisabled);
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", AccentLight);
        theme.SetColor("font_disabled_color", "Button", TextMuted);

        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("default_color", "RichTextLabel", TextSoft);

        _theme = theme;
        return theme;
    }
}
