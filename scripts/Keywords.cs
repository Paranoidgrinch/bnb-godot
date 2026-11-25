using Godot;

namespace BnbGodot;

// THE TERMS IN A SENTENCE, TINTED — AND EXPLAINED ONLY WHERE THE POINTER IS (playtest, 2026-11-25).
//
// A rules text used to carry every term it named spelled out underneath it ("Seal — At 3 Seal, …"), in the
// inventory's relic list, on every relic's hover and on a card's hover wherever the card was touched. The
// player asked for the opposite: the sentence alone, its terms picked out, and a term explained when the
// pointer is ON that term and at no other time. This is that, for every piece of rules text the game sets as
// text: the terms Glossary finds become tinted [url] spans in a RichTextLabel, and hovering one sets the
// label's TooltipText to the term's explanation — which the one tooltip (Tooltips) then shows like any other.
// A card's rules are DRAWN (CardWriting), not set; the card answers the same question through IHoverText.
public static class Keywords
{
    // The tint: the accent's light gold on a faint gold ground — the colour every name in a tooltip is already
    // set in, so a term reads as "a name you can ask about" everywhere.
    private static string Ink => MoonvineTheme.AccentLight.ToHtml(false);
    private static string Ground => new Color(MoonvineTheme.Accent, 0.16f).ToHtml(true);

    // BBCode-escaped text with every term the glossary knows wrapped in a hoverable, tinted span. `skip` is a
    // term not to mark (a status's own name inside its own explanation); `links` off tints without the hover
    // (text drawn where nothing can be hovered, like the tooltip's second-level panel).
    public static string Mark(string? text, string? skip = null, bool links = true)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var spans = Glossary.Spans(text);
        if (spans.Count == 0)
            return Escape(text);
        var marked = new System.Text.StringBuilder();
        var at = 0;
        foreach (var (start, length, name) in spans)
        {
            if (start < at)
                continue;
            marked.Append(Escape(text[at..start]));
            var word = Escape(text.Substring(start, length));
            if (skip is not null && string.Equals(name, skip, StringComparison.OrdinalIgnoreCase))
                marked.Append(word);
            else if (links)
                marked.Append($"[url={Uri.EscapeDataString(name)}][bgcolor=#{Ground}][color=#{Ink}]{word}[/color][/bgcolor][/url]");
            else
                marked.Append($"[color=#{Ink}]{word}[/color]");
            at = start + length;
        }
        marked.Append(Escape(text[at..]));
        return marked.ToString();
    }

    public static string Escape(string text) => text.Replace("[", "[lb]");

    // The term a [url] span stands for, back from its meta.
    public static string TermOf(Variant meta) => Uri.UnescapeDataString(meta.AsString());

    // A paragraph of rules text: wrapped, sized and coloured like the Label it replaces, its terms tinted, and
    // a term explained while the pointer is on it. `onClick` is for a label lying over a clickable row: the
    // label has to catch the pointer to know which word it is on, so it passes a left click on to the row.
    public static RichTextLabel Text(string text, int fontSize, Color color, Action? onClick = null, bool center = false)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MetaUnderlined = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Text = center ? $"[center]{Mark(text)}[/center]" : Mark(text),
        };
        label.AddThemeColorOverride("default_color", color);
        label.AddThemeFontSizeOverride("normal_font_size", fontSize);
        Hook(label, onClick);
        return label;
    }

    // Wires a RichTextLabel whose text came from Mark: on a term, its hover is that term; anywhere else, none.
    public static void Hook(RichTextLabel label, Action? onClick = null)
    {
        label.MetaHoverStarted += meta => label.TooltipText = Glossary.Term(TermOf(meta)) ?? "";
        label.MetaHoverEnded += _ => label.TooltipText = "";
        if (onClick is not null)
            label.GuiInput += input =>
            {
                if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    onClick();
                    label.AcceptEvent();
                }
            };
    }
}

// A control that knows what the pointer is on inside it — a card, whose rules are drawn rather than set and
// whose terms are therefore places on the card, not spans of a label. Tooltips asks it before TooltipText;
// an empty answer means "nothing to say here" and stops the search: hovering a card off its terms says nothing.
public interface IHoverText
{
    string HoverTextAt(Vector2 globalPoint);
}

// THE CARD'S ROOT. A plain Control (see CardVisuals: nothing on a card may report a minimum size) that answers
// the tooltip from the clerk's writing: the term under the pointer, the cost stamp's own words over the stamp,
// and an optional note anywhere else on the card.
public partial class CardFaceRoot : Control, IHoverText
{
    public CardWriting? Writing { get; set; }
    public string CostTip { get; set; } = "";
    public string Hint { get; set; } = "";

    public string HoverTextAt(Vector2 globalPoint)
    {
        if (Writing is { } writing && IsInstanceValid(writing))
        {
            var local = writing.GetGlobalTransform().AffineInverse() * globalPoint;
            if (writing.TermAt(local) is { } term)
                return Glossary.Term(term) ?? "";
            if (CostTip.Length > 0 && writing.OnStamp(local))
                return CostTip;
        }
        return Hint;
    }
}
