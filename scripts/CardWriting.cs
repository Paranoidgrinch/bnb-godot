using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace BnbGodot;

// WHAT THE CLERK WROTE ON THE CARD: the cost into the stamp, the name on its rule, the kind of card on the
// type line, and the rules on the form's ruled lines — all in the one hand the game is written in (Clerk of
// Boston). Drawn rather than set in Labels, because a clerk does three things a Label cannot:
//   - underlines the words that are terms (every glossary term the text uses)
//   - puts a number right when the card was improved: the old one struck through, the new one after it in
//     blue-black, so an improved card says what changed (user, 2026-11-09)
//   - never writes two lines quite level: each line sits a hair higher or lower, and starts a hair further in
// The hand is the card's own (seeded from its id), so the same card is always written the same way.
//
// ⚠ IT REPORTS NO MINIMUM SIZE. It fills the card it is laid on and measures everything from its own Size; the
// card face's rule (nothing on a card may push the card out of shape) holds for it like for every other field.
public partial class CardWriting : Control
{
    public string Cost { get; init; } = "";
    public string Title { get; init; } = "";
    public Color TitleInk { get; init; } = CardForm.Ink;
    public float TitleEnd { get; init; } = 0.86f;
    public string Kind { get; init; } = "";
    public string Rules { get; init; } = "";
    public string? Was { get; init; }
    public CardForm.Look Look { get; init; }
    public string Seed { get; init; } = "";

    private static Font TypeFace => MoonvineTheme.Font ?? ThemeDB.Singleton.FallbackFont;

    public CardWriting()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var w = Size.X;
        var h = Size.Y;
        var u = w / CardVisuals.CardW; // one design unit, in pixels
        var rng = new Random((int)CardForm.Hash(Seed, 9));

        // ── the cost, in the stamp ───────────────────────────────────────────────
        if (Cost.Length > 0)
        {
            var c = CardForm.StampCentre * new Vector2(w, h);
            var r = CardForm.StampR * w;
            var size = Fit(Cost, 1.25f * r, 21 * u, 9 * u);
            var width = TypeFace.GetStringSize(Cost, HorizontalAlignment.Left, -1, size).X;
            DrawString(TypeFace, new Vector2(c.X + 0.6f * u - width / 2, c.Y + 0.28f * size), Cost,
                HorizontalAlignment.Left, -1, size, CardForm.Ink);
        }

        // ── the name, on its rule ────────────────────────────────────────────────
        {
            var x0 = CardForm.TitleX0 * w;
            var room = (TitleEnd - CardForm.TitleX0) * w;
            var size = Fit(Title, room * 0.96f, 13 * u, 7 * u);
            var width = TypeFace.GetStringSize(Title, HorizontalAlignment.Left, -1, size).X;
            // A name still too long at the smallest size is written narrower, not cut: the clerk squeezed it in.
            var squeeze = Math.Min(1f, room * 0.96f / Math.Max(1f, width));
            var at = new Vector2(x0 + (room - width * squeeze) / 2, CardForm.TitleBaseline * h);
            DrawSetTransform(at, 0, new Vector2(squeeze, 1));
            DrawString(TypeFace, Vector2.Zero, Title, HorizontalAlignment.Left, -1, size, TitleInk);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        // ── the kind of paper, on the type line ──────────────────────────────────
        if (Kind.Length > 0)
        {
            var size = Px(8.5f * u);
            var width = TypeFace.GetStringSize(Kind, HorizontalAlignment.Left, -1, size).X;
            DrawString(TypeFace, new Vector2(w / 2 - width / 2, CardForm.TypeLineY * h + 2.6f * u), Kind,
                HorizontalAlignment.Left, -1, size, CardForm.Ink);
        }

        // ── the rules, on the ruled lines ────────────────────────────────────────
        var box = CardForm.TextBox;
        Text(new Rect2(box.Position * new Vector2(w, h), box.Size * new Vector2(w, h)), u, rng);
    }

    // One written word: what it says, whether it is (part of) a term, a correction inside it, and its ink.
    private sealed record Word(string Text, int Term, (int At, string Old, string New)? Fix, Color Ink, bool Break);

    private void Text(Rect2 box, float u, Random rng)
    {
        var words = Words();
        if (words.Count == 0)
            return;

        int size = Px(11 * u);
        List<List<Word>> lines;
        while (true)
        {
            lines = Lay(words, size, box.Size.X);
            if (lines.Count * size * 1.2f <= box.Size.Y || size <= Px(6.5f * u))
                break;
            size--;
        }
        var lh = size * 1.2f;
        var space = TypeFace.GetStringSize(" ", HorizontalAlignment.Left, -1, size).X;
        var top = box.Position.Y + Math.Max(0, (box.Size.Y - lines.Count * lh) * 0.35f);

        // The form's own ruled lines, printed whatever is written on them.
        var rule = new Color(CardForm.PrintInk(Look), 0.3f);
        for (var k = 0; ; k++)
        {
            var y = top + lh * (k + 0.78f) + 0.16f * size;
            if (y > box.End.Y + 0.2f * lh)
                break;
            DrawLine(new Vector2(box.Position.X - 0.02f * box.Size.X, y), new Vector2(box.End.X + 0.02f * box.Size.X, y),
                rule, Math.Max(1f, 0.35f * u));
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var baseline = top + lh * (i + 0.78f) + (float)(rng.NextDouble() - 0.5) * 0.5f * u;
            var x = box.Position.X + (float)rng.NextDouble() * 0.8f * u;
            var lastTerm = -1;
            foreach (var word in lines[i])
            {
                var width = Width(word, size);
                if (word.Fix is { } fix)
                {
                    var before = word.Text[..fix.At];
                    var after = word.Text[(fix.At + fix.New.Length)..];
                    var p = x;
                    DrawString(TypeFace, new Vector2(p, baseline), before, HorizontalAlignment.Left, -1, size, word.Ink);
                    p += Measure(before, size);
                    var old = Measure(fix.Old, size);
                    DrawString(TypeFace, new Vector2(p, baseline), fix.Old, HorizontalAlignment.Left, -1, size, word.Ink);
                    DrawLine(new Vector2(p - 0.12f * size, baseline - 0.18f * size), new Vector2(p + old + 0.12f * size, baseline - 0.40f * size),
                        CardForm.Correction, Math.Max(1f, 0.07f * size), true);
                    p += old + space * 0.6f;
                    DrawString(TypeFace, new Vector2(p, baseline), fix.New, HorizontalAlignment.Left, -1, size, CardForm.Correction);
                    p += Measure(fix.New, size);
                    DrawString(TypeFace, new Vector2(p, baseline), after, HorizontalAlignment.Left, -1, size, word.Ink);
                }
                else
                {
                    DrawString(TypeFace, new Vector2(x, baseline), word.Text, HorizontalAlignment.Left, -1, size, word.Ink);
                }

                if (word.Term >= 0)
                {
                    // One stroke under a whole term, even when it is two words; a trailing full stop is not
                    // underlined.
                    var trimmed = word.Text.TrimEnd('.', ',', ':', ';', ')');
                    var under = trimmed.Length == word.Text.Length ? width : width - Measure(word.Text[trimmed.Length..], size);
                    var from = word.Term == lastTerm ? x - space : x;
                    Underline(from + 0.05f * size, x + under - 0.05f * size, baseline + 0.2f * size, size, rng);
                }
                lastTerm = word.Term;
                x += width + space;
            }
        }
    }

    // The words of the rules, with the wand's part (if the card has one) after a break in its own ink.
    private List<Word> Words()
    {
        var (play, wand) = CardVisuals.SplitWand(Rules);
        var wasPlay = Was is null ? null : CardVisuals.SplitWand(Was).Play;
        var words = Split(play, wasPlay, CardForm.Ink, 0);
        if (wand is not null)
        {
            var wandWords = Split("Wand: " + wand, null, CardForm.WandInk, 1000);
            if (wandWords.Count > 0)
                wandWords[0] = wandWords[0] with { Break = true };
            words.AddRange(wandWords);
        }
        return words;
    }

    private static readonly Regex Number = new(@"\d+", RegexOptions.Compiled);

    private static List<Word> Split(string text, string? was, Color ink, int termBase)
    {
        // Where the terms are: every glossary term the text uses, each occurrence a span of characters.
        var term = new int[text.Length];
        Array.Fill(term, -1);
        var index = 0;
        foreach (var name in Glossary.NamesIn(text))
        {
            foreach (Match m in Regex.Matches(text, $@"(?<![\p{{L}}]){Regex.Escape(name)}(?![\p{{L}}])", RegexOptions.IgnoreCase))
            {
                if (term[m.Index] >= 0)
                    continue;
                for (var k = m.Index; k < m.Index + m.Length; k++)
                    term[k] = termBase + index;
                index++;
            }
        }

        // What an improvement changed: the same sentence with other numbers in it. Anything more than that is
        // a different card and is simply written out.
        var fixes = new Dictionary<int, (string Old, string New)>();
        if (was is not null && Number.Replace(was, "#") == Number.Replace(text, "#"))
        {
            var olds = Number.Matches(was);
            var news = Number.Matches(text);
            for (var k = 0; k < news.Count; k++)
                if (olds[k].Value != news[k].Value)
                    fixes[news[k].Index] = (olds[k].Value, news[k].Value);
        }

        var words = new List<Word>();
        var lineBreak = false;
        foreach (Match m in Regex.Matches(text, @"\S+|\n"))
        {
            if (m.Value == "\n")
            {
                lineBreak = true;
                continue;
            }
            (int, string, string)? fix = null;
            foreach (var (at, (old, @new)) in fixes)
                if (at >= m.Index && at < m.Index + m.Length)
                {
                    fix = (at - m.Index, old, @new);
                    break;
                }
            words.Add(new Word(m.Value, term[m.Index], fix, ink, lineBreak));
            lineBreak = false;
        }
        return words;
    }

    private List<List<Word>> Lay(List<Word> words, int size, float room)
    {
        var space = Measure(" ", size);
        var lines = new List<List<Word>>();
        var line = new List<Word>();
        var x = 0f;
        foreach (var word in words)
        {
            var width = Width(word, size);
            if (line.Count > 0 && (word.Break || x + space + width > room))
            {
                lines.Add(line);
                line = [];
                x = 0;
            }
            x += (line.Count > 0 ? space : 0) + width;
            line.Add(word);
        }
        if (line.Count > 0)
            lines.Add(line);
        return lines;
    }

    private float Width(Word word, int size) => word.Fix is { } fix
        ? Measure(word.Text, size) + Measure(fix.Old, size) + Measure(" ", size) * 0.6f
        : Measure(word.Text, size);

    private static float Measure(string text, int size) =>
        text.Length == 0 ? 0 : TypeFace.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

    // A quick pen stroke under a word: a little slope, a little wave, never ruler-straight.
    private void Underline(float x0, float x1, float y, int size, Random rng)
    {
        const int Steps = 6;
        var slope = (float)(rng.NextDouble() * 0.07 - 0.04) * size;
        var phase = (float)rng.NextDouble() * 3;
        var points = new Vector2[Steps + 1];
        for (var i = 0; i <= Steps; i++)
            points[i] = new Vector2(x0 - 0.1f * size + (x1 - x0 + 0.25f * size) * i / Steps,
                y + slope * i / Steps + Mathf.Sin(i * 1.3f + phase) * 0.05f * size);
        DrawPolyline(points, CardForm.Ink, Math.Max(1f, 0.075f * size), true);
    }

    private static int Px(float size) => Math.Max(5, (int)Mathf.Round(size));

    private static int Fit(string text, float width, float max, float min)
    {
        for (var size = Px(max); size > Px(min); size--)
            if (Measure(text, size) <= width)
                return size;
        return Px(min);
    }
}
