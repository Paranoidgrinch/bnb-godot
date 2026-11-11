using System.Text.Json;
using Clipper2Lib;
using Godot;

namespace BnbGodot.MapArt;

// Act IV, The Licensing Labyrinth: a papyrus. The paths are a paved processional way with sphinxes along it, the
// rooms its chapels (the boss's with a red pylon, a rest room framed in blue). The margins are pieces of real
// Book of the Dead papyri, their own papyrus taken away (assets/map/art/make_art.py): mostly columns of black
// script, three or four painted figures among them, a band of script under the title, the frieze of gods in two
// halves beside the boss (user, 2026-11-11: less colour, more script).
public sealed class PapyrusPlan(Plan.Input input) : Plan(input)
{
    private static readonly Color Black = C(28, 22, 16);
    private static readonly Color Red = C(176, 52, 36);
    private static readonly Color Blue = C(52, 96, 150);
    private const double MaxW = 124;

    private sealed record Piece(string id, string label, string kind, double w, double h);

    protected override void Draw()
    {
        var pieces = JsonSerializer.Deserialize<List<Piece>>(Godot.FileAccess.GetFileAsString("res://assets/map/art/pieces.json")) ?? [];
        Sk.OnPaint();
        Avenue();
        Margins(pieces);
        Sk.OnInk();
        Title("The Licensing Labyrinth");
    }

    private void Avenue()
    {
        var lines = PathLines(0.4);
        var road = Geo.Thicken(lines, 4.5, round: false);
        var chapels = Centre.ToDictionary(kv => kv.Key, kv =>
        {
            var s = R + (Role[kv.Key] == "boss" ? 10 : 1);
            return Geo.Box(kv.Value.X - s, kv.Value.Y - s, kv.Value.X + s, kv.Value.Y + s);
        });
        var built = Geo.Union([road, .. chapels.Values]);
        Sk.FillShape(built, C(222, 202, 156, 250));
        Sk.Lines(Geo.Rings(built), Black, 2);
        var allChapels = Geo.Union(chapels.Values);
        foreach (var l in lines)
        {
            var L = Geo.Length(l);
            for (var t = 2.0; t < L; t += 3.2)
            {
                var q = Geo.At(l, t);
                if (Geo.Contains(allChapels, q.x, q.y))
                    continue;
                var (dx, dy) = Geo.Dir(l, t);
                Sk.Line(C(120, 96, 60, 160), 1, (q.x - dy * 5, q.y + dx * 5), (q.x + dy * 5, q.y - dx * 5));
            }
        }
        // sphinxes on either side, facing along the way
        var ok = Geo.Difference(Geo.Buffer(Frame, -20), Geo.Buffer(built, 1));
        var placed = new List<(double X, double Y)>();
        foreach (var l in lines)
        {
            var L = Geo.Length(l);
            for (var t = 4.0; t < L - 4; t += 10)
            {
                var q = Geo.At(l, t);
                var (dx, dy) = Geo.Dir(l, t);
                foreach (var side in new[] { -1.0, 1.0 })
                {
                    double sx = q.x - dy * 11 * side, sy = q.y + dx * 11 * side;
                    if (!Geo.Covers(ok, Geo.Circle(sx, sy, 3, 8)) || placed.Any(o => Math.Abs(o.X - sx) < 12 && Math.Abs(o.Y - sy) < 12 && Math.Sqrt((o.X - sx) * (o.X - sx) + (o.Y - sy) * (o.Y - sy)) <= 12))
                        continue;
                    placed.Add((sx, sy));
                    var faceLeft = Math.Abs(dx) > Math.Abs(dy) ? dx * side < 0 : side < 0;
                    Sk.Picture($"res://assets/map/art/sphinx_{(faceLeft ? "r" : "l")}.png", sx - 8, sy - 3.5, 16, 7);
                }
            }
        }
        foreach (var (id, (x, y)) in Centre)
        {
            var b = Geo.Bounds(chapels[id]);
            Sk.Rect(b.left, b.top, b.right, b.bottom, fill: C(236, 222, 186), outline: Black, width: 2);
            if (Role[id] == "boss")
            {
                Sk.Poly([(b.left - 6, b.top - 2), (b.left + 18, b.top - 2), (b.left + 16, b.top + 16), (b.left - 4, b.top + 16)], fill: Red, outline: Black);
                Sk.Poly([(b.right + 6, b.top - 2), (b.right - 18, b.top - 2), (b.right - 16, b.top + 16), (b.right + 4, b.top + 16)], fill: Red, outline: Black);
            }
            else if (Role[id] == "rest")
                Sk.Rect(b.left - 6, b.top - 6, b.right + 6, b.bottom + 6, outline: Blue, width: 4);
        }
    }

    private void Margins(List<Piece> pieces)
    {
        string Art(string id) => $"res://assets/map/art/egypt_{id}.png";
        foreach (var p in pieces)
        {
            if (p.kind == "frieze")
                Sk.Picture(Art(p.id), p.id.EndsWith('a') ? 26 : W - 166, H - Foot - 48, p.w, p.h);
            else if (p.kind == "band")
                Sk.Picture(Art(p.id), W / 2.0 - p.w / 2, 70, p.w, p.h);
        }
        var side = pieces.Where(p => p.kind is "script" or "figure").ToList();
        Rng.Shuffle(side);
        var figures = side.Where(p => p.kind == "figure").Take(Rng.RandInt(3, 4));
        var chosen = side.Where(p => p.kind == "script").Concat(figures).ToList();
        Rng.Shuffle(chosen);
        foreach (var left in new[] { true, false })
        {
            var y = Top + Rng.Uniform(0, 20);
            while (chosen.Count > 0)
            {
                var p = chosen[0];
                if (y + p.h > H - Foot - 62)
                    break;
                chosen.RemoveAt(0);
                var x = left ? 22 + (MaxW - p.w) * Rng.Uniform(0.2, 0.8) : W - 22 - p.w - (MaxW - p.w) * Rng.Uniform(0.2, 0.8);
                Sk.Picture(Art(p.id), x, y, p.w, p.h);
                y += p.h + Rng.Uniform(16, 40);
            }
        }
    }
}
