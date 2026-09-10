using Godot;

namespace BnbGodot;

// THE GROUND THE WHOLE GAME IS PRINTED ON. One file, one palette, one typeface — every screen reads its
// colours from here and no screen anywhere may invent one of its own.
//
// The look is a ledger kept in a dark room: a near-black page that is not neutral but bled slightly red,
// with antique gold for everything the player can touch. It replaces the Moonvine Forge slate/green tokens
// that were mirrored from the Studio's studio.css — those were a TOOL's colours, and this is a game.
//
// ⚠ RED IS NOT A WARNING HERE. On a red ground a red alert sinks into the panel behind it, so the colour
// that means "pay attention" is AMBER (`Signal`) and red keeps a narrower job: flesh, damage, the
// opposition (`Harm`). Anything that wants to shout must shout in amber.
public static class MoonvineTheme
{
    // ── the base ramp: near-black, biased red ────────────────────────────────────
    // Six steps, darkest first. A surface is chosen by how far it should sit off the page, never by taste.
    public static readonly Color Bg = new("080406");            // the page itself
    public static readonly Color BgPanelStrong = new("0c0507"); // a panel that is spent, visited or out of reach
    public static readonly Color BgPanel = new("120709");       // an ordinary panel
    public static readonly Color BgRaised = new("1b0a0e");      // something lifted off the panel
    public static readonly Color BgControl = new("2a1014");     // something you can press
    public static readonly Color Hairline = new("3b1419");      // the quiet line between two things
    public static readonly Color Blood = new("5c1219");         // a heavier divider; the ground of a filled track

    // The ground a CARD is printed on — a shade under the page, so a card reads as an object lying on it
    // rather than a hole cut in it. D1 prints the master frame over this.
    public static readonly Color CardGround = new("0a0507");

    // ── type ─────────────────────────────────────────────────────────────────────
    // Warm-biased neutrals. A pure grey on a red ground looks like a grey that was never chosen.
    public static readonly Color Text = new("f3e9e1");
    public static readonly Color TextSoft = new("d6c4ba");
    public static readonly Color TextMuted = new("9b877f");

    // ── the accent: antique gold ─────────────────────────────────────────────────
    // Gold means AGENCY: a button, a border you may cross, a buff you were given, the shop that will trade
    // with you. Deep and dull on purpose — see Signal for why that matters.
    public static readonly Color Accent = new("c9a227");
    public static readonly Color AccentLight = new("e9d18a");
    public static readonly Color AccentDark = new("7d6414");

    // ── the signal: amber ────────────────────────────────────────────────────────
    // Amber means ATTENTION: an attack coming in, a cost, an energy purse, a refused play, a toast.
    // It shares gold's hue on purpose and separates from it by value and saturation — gold is brass you
    // look AT, amber is a lamp that is lit. Never use one where the other is meant.
    public static readonly Color Signal = new("ffd166");

    // ── the rest of the vocabulary ───────────────────────────────────────────────
    public static readonly Color Harm = new("d93b45");   // flesh and damage: the enemy, a debuff, an error, a loss
    public static readonly Color Steel = new("7fa8ce");  // cold and safe: block, defence, a campfire
    public static readonly Color Arcane = new("b98fd0"); // the unknown: a door, a hex, a rare find
    public static readonly Color Copper = new("b87333"); // worked metal: the bench

    // ── THE ONE PLACE THE TYPEFACE IS CHOSEN ─────────────────────────────────────
    // The game runs on Godot's built-in face today, at the user's call. To change it for the WHOLE game:
    // drop a .ttf/.otf into `theme/` and put its res:// path in FontPath below. Build() writes it into the
    // Theme's default font, which every Label, Button, RichTextLabel and card face inherits — so the change
    // is this one line and nothing else. ⚠ No screen may load a font of its own; the moment one does,
    // "change the font later" stops being one edit and becomes twenty.
    // (a field, not a const: a const null would fold the lookup below away at compile time)
    private static readonly string? FontPath = null; // e.g. "res://theme/bnb.ttf"

    private static Font? _font;
    private static bool _fontLooked;

    public static Font? Font
    {
        get
        {
            if (_fontLooked)
                return _font;
            _fontLooked = true;
            if (FontPath is { } path && ResourceLoader.Exists(path))
                _font = GD.Load<Font>(path);
            else if (FontPath is not null)
                GD.PushWarning($"MoonvineTheme: font '{FontPath}' not found — falling back to the Godot default.");
            return _font;
        }
    }

    // Intent-kind accents for enemy telegraphs. An ATTACK is the one intent that is aimed at the player, so
    // it — and only it — gets the signal colour.
    public static Color IntentColor(RogueDeck.Scenario.Authoring.IntentKind kind) => kind switch
    {
        RogueDeck.Scenario.Authoring.IntentKind.Attack => Signal,
        RogueDeck.Scenario.Authoring.IntentKind.Defend => Steel,
        RogueDeck.Scenario.Authoring.IntentKind.Buff => Accent,
        RogueDeck.Scenario.Authoring.IntentKind.Debuff => Arcane,
        _ => TextMuted,
    };

    // ⚠ RARITY IS DELIBERATELY NOT GOLD. Gold now means "you can touch this", and a rare card that wore the
    // button colour would read as the button. The ramp runs bone → pale violet → violet, and it runs QUIETLY:
    // most of a hand is common or uncommon, so those two steps stay near the text colour and only RARE is
    // allowed to be a colour. A ramp that painted half the hand in a cold hue would put the boldness of the
    // whole screen on the word "uncommon".
    public static Color RarityColor(string? rarity) => rarity switch
    {
        "uncommon" => new Color("c9b8d6"),
        "rare" => Arcane,
        "starter" => TextMuted,
        _ => TextSoft,
    };

    // ⚠ THE DEFAULT BORDER IS THE HAIRLINE, NOT THE ACCENT. Before, every panel in the game was outlined in
    // the accent at 30 %, which made the whole screen look equally clickable. A frame in gold now MEANS
    // something, so a caller that wants one passes it.
    public static StyleBoxFlat Panel(Color? bg = null, Color? border = null, int radius = 8) => new()
    {
        BgColor = bg ?? BgPanel,
        BorderColor = border ?? Hairline,
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

        if (Font is { } font)
            theme.DefaultFont = font;

        theme.SetStylebox("panel", "PanelContainer", Panel());
        theme.SetStylebox("panel", "Panel", Panel(BgPanelStrong));

        // A button is the one thing on screen that is unambiguously yours to press, so it is the one thing
        // that wears gold at rest.
        var button = Panel(BgControl, new Color(Accent, 0.45f), radius: 999);
        var buttonHover = Panel(new Color(Accent, 0.18f), AccentLight, 999);
        var buttonDisabled = Panel(BgPanelStrong, new Color(TextMuted, 0.18f), 999);
        theme.SetStylebox("normal", "Button", button);
        theme.SetStylebox("hover", "Button", buttonHover);
        theme.SetStylebox("pressed", "Button", buttonHover);
        theme.SetStylebox("disabled", "Button", buttonDisabled);
        theme.SetStylebox("focus", "Button", Panel(new Color(Accent, 0.10f), Signal, 999));
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", AccentLight);
        theme.SetColor("font_pressed_color", "Button", AccentLight);
        theme.SetColor("font_disabled_color", "Button", TextMuted);

        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("default_color", "RichTextLabel", TextSoft);

        // The only separator in the game (the inventory rule) — a blood line, not a grey one.
        var rule = new StyleBoxFlat { BgColor = Blood, ContentMarginTop = 1, ContentMarginBottom = 1 };
        theme.SetStylebox("separator", "HSeparator", rule);
        theme.SetStylebox("separator", "VSeparator", rule);

        _theme = theme;
        return theme;
    }
}
