using Godot;

namespace BnbGodot;

// THE DOORWAY. One object, assembled from three materials, used everywhere the game says which act you are
// standing in: a black marble surround, an oxblood field set into it, a gold inscription on the field, and a
// tooth course of egg-and-dart along the top with a rosette at either end.
//
// ★ IT IS ASSEMBLED, NOT DRAWN. D8-0 built a picture of a lintel first and it was the best thing on the
// contact sheet — and still wrong: a nine-patch REPEATS its edge strips, so a course of ornament survives
// only at widths that happen to be whole multiples of its step and smears at every other one. The portal in
// the reference photographs is not one carved slab either; it is a surround, a field and a course laid on top
// of each other. So this composes the three and no ornament is ever stretched: the course TILES along its own
// axis, at its own size, and stops where the stone does.
//
// ★ THE GAME ALREADY SPOKE THIS. A run is five acts in roman numerals walked through doors; `SAAL II` over a
// museum doorway is not a mood board, it is the act title card with the frame around it already designed.
public static class Lintel
{
    // The numeral is the loudest thing on the field, the way it is on the stone: the act is a PLACE and the
    // number is its name. Five acts, so five numerals — anything else falls back to digits rather than
    // inventing a notation nobody asked for.
    public static string Roman(int act) => act switch
    {
        1 => "I",
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        _ => act.ToString(),
    };

    // Inscribed letters stand apart. Godot's Label has no letter spacing, and a thin space between characters
    // is what the stone does anyway — the gap is cut, not kerned.
    private static string Inscribed(string text) => string.Join(" ", text.ToCharArray());

    /// The doorway. `grand` is the title card that fills the screen when an act begins; without it this is the
    /// inline heading a way-screen carries at the top of every room.
    public static Control Make(int act, string title, string? under = null, bool grand = false)
    {
        var surround = new PanelContainer();
        surround.AddThemeStyleboxOverride("panel",
            MoonvineTheme.StoneFrame(padH: grand ? 26 : 16, padV: grand ? 20 : 12));

        var stack = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        stack.AddThemeConstantOverride("separation", grand ? 12 : 6);

        // ⚠ THE COURSE BELONGS TO THE BIG DOORWAY ONLY. On the way-screen's heading it was thirty eggs across
        // 900 px — a banner where a heading was wanted, and the loudest thing on a screen whose job is the
        // rooms below it. A cornice is what you get when you walk THROUGH the door, not every time you stand
        // in the corridor.
        if (grand && Course(grand) is { } course)
            stack.AddChild(course);

        var field = new PanelContainer();
        field.AddThemeStyleboxOverride("panel",
            MoonvineTheme.JasperField(padH: grand ? 28 : 14, padV: grand ? 18 : 8));

        // A grand card stacks its inscription; a heading sets it on ONE line, which is what the stone does —
        // `SAAL II` is a name and a numeral side by side, and a heading that stacks costs the rooms below it
        // a line of height every single room.
        BoxContainer inscription = grand ? new VBoxContainer() : new HBoxContainer();
        inscription.MouseFilter = Control.MouseFilterEnum.Ignore;
        inscription.Alignment = BoxContainer.AlignmentMode.Center;
        inscription.AddThemeConstantOverride("separation", grand ? 6 : 12);

        var numeral = new Label
        {
            Text = Inscribed(Roman(act)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        numeral.AddThemeFontSizeOverride("font_size", grand ? 46 : 19);
        numeral.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        inscription.AddChild(numeral);

        var name = new Label
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            // ⚠⚠ A HEADING DOES NOT WRAP, AND THAT IS NOT A PREFERENCE. The minimum width of a WRAPPING label
            // is ONE CHARACTER (D3 learned it in the sidebar, D1 in the card, D4 in the shelf — this is the
            // fourth door into the same room), and a heading asked to shrink to its content therefore shrank
            // to a column of letters running down the page. The grand card has a width of its own and may
            // wrap; a heading is one line or it is nothing.
            AutowrapMode = grand ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
        };
        name.AddThemeFontSizeOverride("font_size", grand ? 26 : 15);
        name.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
        inscription.AddChild(name);

        if (under is not null)
        {
            // A subtitle is a subtitle — D6's rule, kept: the roll call of an act's gods may not shout as
            // loudly as the act's own name, or the card has no first thing to read.
            var quiet = new Label
            {
                Text = under,
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            quiet.AddThemeFontSizeOverride("font_size", grand ? 20 : 13);
            quiet.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
            inscription.AddChild(quiet);
        }

        field.AddChild(inscription);
        stack.AddChild(field);
        surround.AddChild(stack);
        return surround;
    }

    // The cornice: a rosette at each end and the egg-and-dart between them, tiling at its own size. Returns
    // null when the material is not on disk, which is a normal state — a missing picture has never been an
    // error in this game, and the doorway is still a doorway without its course.
    private static Control? Course(bool grand)
    {
        if (MoonvineTheme.Material("moulding-gold") is not { } moulding)
            return null;

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        var height = grand ? moulding.GetHeight() : Mathf.Max(10, moulding.GetHeight() * 2 / 3);

        void Rosette()
        {
            if (!grand || MoonvineTheme.Material("rosette-gold") is not { } stud)
                return;
            row.AddChild(new TextureRect
            {
                Texture = stud,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
                CustomMinimumSize = new Vector2(height + 8, height + 8),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }

        Rosette();
        row.AddChild(new TextureRect
        {
            Texture = moulding,
            // ⚠ TILE, NEVER STRETCH. This is the one place in the game where an ornament meets an unknown
            // width, and a stretched egg is a smear. It repeats instead, which is what a course of moulding
            // does on a real cornice.
            StretchMode = TextureRect.StretchModeEnum.Tile,
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
            CustomMinimumSize = new Vector2(0, height),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        Rosette();
        return row;
    }
}
