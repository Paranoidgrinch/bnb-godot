using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace BnbGodot;

// THE PAPER CARD'S PARTS, AND WHERE THEY GO (2026-11-09). A card is a sheet of yellowed paper with a form
// printed on it, a cost stamp struck in its corner, a wax seal when it was improved, and a clerk's handwriting
// over all of it. Every part is a picture made by a generator that lives next to it:
//
//     assets/cards/paper/make_paper.py   paper_NN.png       the sheets
//     assets/cards/form/make_form.py     form_<look>_<n>    the printed form, its title rule, the stamps,
//                                        layout.json        SERVED, and where every one of them goes
//     assets/cards/seal/make_seal.py     seal_NN.png        the wax
//     assets/cards/back/make_back.py     card_back.png      the back, the same for every card
//
// ⚠ THE POSITIONS ARE THE GENERATOR'S, NOT THIS FILE'S. layout.json is written by make_form.py from the same
// numbers it drew with; this reads it, so the pictures and the code cannot disagree about where a field is.
//
// Which sheet, which form, which stamp and which seal a card gets is picked from its id, so a card looks the
// same every time it is drawn and two cards next to each other rarely look alike. An improved copy is the
// same sheet as the card it improves: the improvement is a seal pressed on it, not a new card.
public static class CardForm
{
    private const string Folder = "res://assets/cards";

    // The forms a card can be printed on. Junk, curses and what a fight forces into the hand come as the
    // office's red notice; every card the player chose is the office's own form.
    public enum Look { Office, Notice }

    private static JsonElement? _layout;

    private static JsonElement Layout => _layout ??= JsonDocument.Parse(
        Godot.FileAccess.GetFileAsString($"{Folder}/form/layout.json")).RootElement;

    private static float F(params string[] path)
    {
        var at = Layout;
        foreach (var key in path)
            at = at.GetProperty(key);
        return at.GetSingle();
    }

    private static Rect2 Box(JsonElement four) =>
        new(four[0].GetSingle(), four[1].GetSingle(), four[2].GetSingle() - four[0].GetSingle(), four[3].GetSingle() - four[1].GetSingle());

    private static string Key(Look look) => look == Look.Notice ? "notice" : "office";

    // ── the layout, as fractions of the card ─────────────────────────────────────
    public static Color NoticeTint
    {
        get
        {
            var t = Layout.GetProperty("notice_paper_tint");
            return new Color(t[0].GetSingle(), t[1].GetSingle(), t[2].GetSingle());
        }
    }

    public static float JitterDegrees => F("form_jitter", "degrees");
    public static float JitterUnits => F("form_jitter", "units");
    public static float TitleRuleY => F("title_rule_y");
    public static float TitleStripHeight => F("title_strip_height");
    public static float TitleX0 => F("title_box", "x0");
    public static float TitleEnd(Look look, bool sealed_) => sealed_ ? F("title_box", "end_sealed") : F("title_box", "end", Key(look));
    public static float TitleBaseline => F("title_box", "baseline");
    public static Rect2 ArtBox(Look look) => Box(Layout.GetProperty("art_box").GetProperty(Key(look)));
    public static float TypeLineY => F("type_line_y");
    public static Rect2 TextBox => Box(Layout.GetProperty("text_box"));
    public static Vector2 StampCentre => new(F("stamp", "cx"), F("stamp", "cy"));
    public static float StampR => F("stamp", "r");
    public static float StampImageR => F("stamp", "image_r");
    public static Vector2 ServedCentre => new(F("served", "cx"), F("served", "cy"));
    public static float ServedSize => F("served", "size");
    public static Vector2 SealCentre => new(F("seal", "cx"), F("seal", "cy"));
    public static float SealR => F("seal", "r");

    // make_seal.py pads the wax by 1.9 R on every side, so the shadow it throws fits in the picture.
    public const float SealImageR = 1.9f;

    // make_form.py's inner rule sits 0.013 of the width in from the box, and the hatching (where a picture
    // goes) one rule further in.
    public const float ArtInset = 2 * 0.013f;

    public static string StampInk(string? rarity, Look look)
    {
        var inks = Layout.GetProperty("stamp").GetProperty("ink_by_rarity");
        var key = look == Look.Notice ? "notice" : rarity ?? "common";
        return inks.TryGetProperty(key, out var ink) ? ink.GetString()! : "brown";
    }

    // ── the inks the clerk writes with ───────────────────────────────────────────
    public static readonly Color Ink = Color.Color8(44, 28, 18);          // brown-black, everything by default
    public static readonly Color Correction = Color.Color8(30, 40, 78);   // blue-black: a number put right
    public static readonly Color WandInk = Color.Color8(96, 34, 132);     // the wand's part, in violet
    public static readonly Color StampRed = Color.Color8(150, 36, 28);

    // The rarity is in the ink of the TITLE (user, 2026-11-09): brown-black, verdigris, red.
    public static Color TitleInk(string? rarity, Look look) => look == Look.Notice
        ? Color.Color8(40, 24, 18)
        : rarity switch
        {
            "uncommon" => Color.Color8(34, 96, 64),
            "rare" => StampRed,
            _ => Ink,
        };

    // The form's own printing ink, for the ruled lines the clerk writes on.
    public static Color PrintInk(Look look) => look == Look.Notice ? Color.Color8(158, 52, 44) : Color.Color8(92, 72, 58);

    // ── the pictures ─────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, Texture2D[]> Sets = [];

    // Every numbered picture of one kind that exists, `prefix` 1, 2, … until the first gap (paper sheets the
    // user threw out leave gaps, so the paper is probed up to a fixed count instead).
    private static Texture2D[] Set(string prefix, string format, int probe = 0)
    {
        if (Sets.TryGetValue(prefix, out var known))
            return known;
        var found = new List<Texture2D>();
        for (var i = 1; i <= Math.Max(probe, 99); i++)
        {
            var path = $"{Folder}/{prefix}{i.ToString(format)}.png";
            if (ResourceLoader.Exists(path))
                found.Add(GD.Load<Texture2D>(path));
            else if (probe == 0 || i >= probe)
                break;
        }
        return Sets[prefix] = [.. found];
    }

    private static Texture2D Pick(Texture2D[] set, string id, uint salt) => set[(int)(Hash(id, salt) % (uint)set.Length)];

    public static Texture2D Paper(string id) => Pick(Set("paper/paper_", "00", probe: 40), id, 1);
    public static Texture2D Form(string id, Look look) => Pick(Set($"form/form_{Key(look)}_", "0"), id, 2);
    public static Texture2D TitleRule(Look look, bool sealed_) =>
        GD.Load<Texture2D>($"{Folder}/form/title_{Key(look)}_{(sealed_ ? "short" : "long")}.png");
    public static Texture2D Stamp(string id, string ink) => Pick(Set($"form/stamp_{ink}_", "0"), id, 3);
    public static Texture2D Served(string id) => Pick(Set("form/served_", "0"), id, 4);
    public static Texture2D Seal(string id) => Pick(Set("seal/seal_", "00"), id, 5);
    public static Texture2D Back => GD.Load<Texture2D>($"{Folder}/back/card_back.png");

    // The forme sat a little crooked in the press, and never twice the same way: a turn in degrees and a shift
    // in design units, fixed by the card.
    public static (float Degrees, Vector2 Units) Jitter(string id)
    {
        float Unit(uint salt) => Hash(id, salt) / (float)uint.MaxValue * 2f - 1f;
        return (Unit(6) * JitterDegrees, new Vector2(Unit(7), Unit(8)) * JitterUnits);
    }

    // FNV-1a over the card's own id (an improvement is the card it improves). string.GetHashCode is not used
    // on purpose: .NET seeds it per process, and a card must look the same tomorrow.
    public static uint Hash(string id, uint salt = 0)
    {
        var hash = 2166136261u ^ (salt * 16777619u);
        foreach (var c in id.TrimEnd('+'))
        {
            hash ^= c;
            hash *= 16777619u;
        }
        hash ^= hash >> 15;
        hash *= 2246822519u;
        hash ^= hash >> 13;
        return hash;
    }
}
