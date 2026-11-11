using Clipper2Lib;
using Godot;

namespace BnbGodot.MapArt;

// Act II, The Endless Archives: the vaults under them, drawn as the eighteenth-century plans of souterrains are
// (Mont-Saint-Michel, Bouchain) — the paths are corridors, the rooms chambers in grey-washed masonry with
// pilasters; a fight's chamber has its cross vault dashed in red, a rest room is round with a spiral stair, an
// elite or the boss a hall of piers, the shop a room of shelves. In the margins, reached by passages: stacks,
// cells and a rotunda (after Wolfenbuettel). The furniture: a scroll cartouche with the act's name, a legend, a
// north arrow, a scale in toises, the surveyor's note (the section through the vaults went out, user 2026-11-11).
public sealed class VaultPlan(Plan.Input input) : Plan(input)
{
    private static readonly Color Grey = C(150, 146, 138);
    private static readonly Color Red = C(150, 40, 34);

    protected override void Draw()
    {
        var lines = PathLines(0.5);
        var corridors = lines.Select(l => Geo.Thicken(l, 6, round: false, JoinType.Miter)).ToList();
        var chambers = new List<PathsD>();
        var kinds = new Dictionary<string, (string Kind, PathsD Shape)>();
        foreach (var (id, (x, y)) in Centre)
        {
            PathsD ch;
            string kind;
            if (Role[id] == "rest")
                (ch, kind) = (Geo.Circle(x, y, R + 5), "round");
            else if (Role[id] is "boss" or "elite")
            {
                var s = R + (Role[id] == "boss" ? 16 : 10);
                (ch, kind) = (Geo.Box(x - s * 1.2, y - s, x + s * 1.2, y + s), "hall");
            }
            else
            {
                var s = R + Rng.Uniform(4, 8);
                ch = Geo.Rotate(Geo.Box(x - s, y - s * Rng.Uniform(0.8, 1), x + s, y + s * Rng.Uniform(0.8, 1)), Rng.Uniform(-8, 8), x, y);
                kind = Role[id] == "shop" ? "shelves" : "vault";
            }
            kinds[id] = (kind, ch);
            chambers.Add(ch);
        }
        // dead-end cells off the corridors, the stairs down at the entrance
        var cells = new List<PathsD>();
        foreach (var l in lines)
        {
            if (!(Rng.Next() < 0.35) || Geo.Length(l) <= 40)
                continue;
            var q = Geo.At(l, Geo.Length(l) * Rng.Uniform(0.3, 0.7));
            var (a, b) = (l[0], l[^1]);
            double nx = -(b.y - a.y), ny = b.x - a.x;
            var n = Math.Max(1e-9, Math.Sqrt(nx * nx + ny * ny));
            var side = Rng.Choice([-1.0, 1.0]);
            var cx = q.x + nx / n * 15 * side;
            var cy = q.y + ny / n * 15 * side;
            cells.Add(Geo.Union(Geo.Box(cx - 6, cy - 6, cx + 6, cy + 6), Geo.Thicken(Geo.Line((q.x, q.y), (cx, cy)), 2.5)));
        }
        var ymin = Centre.Values.Min(c => c.Y);
        var stairs = Centre.Values.Where(c => c.Y < ymin + 5).Select(c => Geo.Box(c.X - 7, c.Y - R - 20, c.X + 7, c.Y - R + 2)).ToList();

        // room kept for the legend, then the halls in the margins
        var deco = new Rng(unchecked(In.Seed * 977 + 2));
        var legLeft = deco.Next() < 0.5;
        var lb = legLeft ? Geo.Box(22, H - Foot - 150, 158, H - Foot - 38) : Geo.Box(W - 158, H - Foot - 150, W - 22, H - Foot - 38);
        var lbClear = Geo.Buffer(lb, 24);
        var halls = new List<(string Kind, PathsD Box)>();
        for (var side = 0; side < 2; side++)
        {
            var y = Top + Rng.Uniform(20, 120);
            while (y < H - Foot - 120)
            {
                var hw = Rng.Uniform(70, 110);
                var hh = Rng.Uniform(80, 150);
                var x0 = side == 0 ? Rng.Uniform(26, Side - hw - 30) : Rng.Uniform(W - Side + 30, W - 26 - hw);
                if (y + hh > H - Foot - 20)
                    break;
                var kind = Rng.Choices(["stacks", "rotunda", "cells"], [0.6, 0.15, 0.25]);
                var hb = Geo.Box(x0, y, x0 + hw, y + hh);
                if (!Geo.Intersects(hb, lbClear))
                    halls.Add((kind, hb));
                y += hh + Rng.Uniform(40, 120);
            }
        }
        var allRoom = Geo.Union(chambers.Concat(corridors));
        var passages = new List<PathsD>();
        foreach (var (_, hb) in halls)
        {
            var (p1, p2) = Geo.Nearest(hb, allRoom);
            passages.Add(Geo.Thicken(Geo.Line((p1.x, p1.y), (p2.x, p1.y), (p2.x, p2.y)), 4.5, round: false, JoinType.Miter));
        }
        var open = Geo.Union(corridors.Concat(chambers).Concat(cells).Concat(stairs).Concat(halls.Select(h => h.Box)).Concat(passages));
        var outer = Geo.Buffer(open, 9, JoinType.Miter);
        var walls = Geo.Difference(outer, open);
        // pilasters on the outside of the wall
        var pil = new List<PathsD>();
        foreach (var ring in Geo.Rings(outer))
        {
            var L = Geo.Length(ring);
            for (var t = Rng.Uniform(0, 20); t < L; t += Rng.Uniform(18, 26))
            {
                var q0 = Geo.At(ring, t);
                var (dx, dy) = Geo.Dir(ring, t);
                double nx = dy, ny = -dx;
                if (Geo.Contains(outer, q0.x + nx * 2, q0.y + ny * 2))
                    (nx, ny) = (-nx, -ny);
                pil.Add(Geo.Polygon([(q0.x - dx * 3, q0.y - dy * 3), (q0.x + dx * 3, q0.y + dy * 3),
                    (q0.x + dx * 3 + nx * 4, q0.y + dy * 3 + ny * 4), (q0.x - dx * 3 + nx * 4, q0.y - dy * 3 + ny * 4)]));
            }
        }
        var mass = Geo.Intersect(Geo.Union([walls, .. pil]), Geo.Buffer(Frame, -4));
        Sk.Outline(mass, Rng, 0, fill: Grey, amp: 0.4);
        Sk.Outline(mass, Rng, 2, Ink, amp: 0);

        // what is in each room
        foreach (var (id, (kind, ch)) in kinds)
        {
            var (x, y) = Centre[id];
            var b = Geo.Bounds(ch);
            switch (kind)
            {
                case "vault":
                    var inner = Geo.Buffer(ch, -1);
                    foreach (var diag in Geo.ClipLines([Geo.Line((b.left, b.top), (b.right, b.bottom)), Geo.Line((b.right, b.top), (b.left, b.bottom))], inner))
                    {
                        var L = Geo.Length(diag);
                        for (var t = 0.0; t < L; t += 5)
                        {
                            var p0 = Geo.At(diag, t);
                            var p1 = Geo.At(diag, Math.Min(L, t + 3));
                            Sk.Line(C(150, 40, 34, 200), 1, (p0.x, p0.y), (p1.x, p1.y));
                        }
                    }
                    break;
                case "round":
                    Sk.Ellipse(x - R - 1, y - R - 1, x + R + 1, y + R + 1, outline: Ink);
                    for (var i = 0; i < 14; i++)
                    {
                        var a = i * 2 * Math.PI / 14;
                        Sk.Line(Soft, 1, (x + 6 * Math.Cos(a), y + 6 * Math.Sin(a)), (x + (R + 1) * Math.Cos(a), y + (R + 1) * Math.Sin(a)));
                    }
                    Sk.Ellipse(x - 6, y - 6, x + 6, y + 6, outline: Ink);
                    break;
                case "hall":
                    for (var i = 0; i < 4; i++)
                        for (var j = 0; j < 3; j++)
                        {
                            var px = b.left + 8 + (b.right - b.left - 16) * i / 3;
                            var py = b.top + 8 + (b.bottom - b.top - 16) * j / 2;
                            Sk.Rect(px - 2.2, py - 2.2, px + 2.2, py + 2.2, fill: Grey, outline: Ink);
                        }
                    break;
                case "shelves":
                    for (var yy = b.top + 4; yy < b.bottom - 2; yy += 5)
                        Sk.Line(Ink, 2, (b.left + 3, yy), (b.right - 3, yy));
                    break;
            }
        }
        foreach (var st in stairs)
        {
            var b = Geo.Bounds(st);
            for (var yy = b.top + 1; yy < b.bottom; yy += 2.6)
                Sk.Line(Ink, 1, (b.left, yy), (b.right, yy));
        }
        var rotunda = false;
        foreach (var (kind, hb) in halls)
        {
            var b = Geo.Bounds(hb);
            if (kind == "stacks")
            {
                if (Rng.Next() < 0.5)
                    for (var xx = b.left + 7; xx < b.right - 4; xx += 9)
                        Sk.Rect(xx - 1.8, b.top + 8, xx + 1.8, b.bottom - 8, fill: Grey, outline: Ink);
                else
                    for (var yy = b.top + 7; yy < b.bottom - 4; yy += 9)
                        Sk.Rect(b.left + 8, yy - 1.8, b.right - 8, yy + 1.8, fill: Grey, outline: Ink);
                Sk.Text((b.left + b.right) / 2, b.top + 4.5, Rng.Choice(["Stacks", "Old Ledgers", "Dead Files", "Registers"]), 5.5, Red);
            }
            else if (kind == "rotunda" && !rotunda)
            {
                rotunda = true;
                double cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
                double rx = (b.right - b.left) / 2 - 10, ry = (b.bottom - b.top) / 2 - 10;
                for (var i = 0; i < 10; i++)
                {
                    var a = i * 2 * Math.PI / 10;
                    var px = cx + rx * 0.7 * Math.Cos(a);
                    var py = cy + ry * 0.7 * Math.Sin(a);
                    Sk.Rect(px - 3, py - 3, px + 3, py + 3, fill: Ink);
                }
                Sk.Arc(cx, cy, rx, ry, 0, 360, Ink, 4);
                Sk.Text(cx, cy, "Rotunda", 6, Red);
            }
            else
            {
                var n = Rng.RandInt(2, 4);
                for (var i = 0; i < n; i++)
                {
                    var xx = b.left + (i + 0.5) * (b.right - b.left) / n;
                    Sk.Line(Ink, 3, (xx, b.top + 10), (xx, b.bottom - 10));
                }
                Sk.Text((b.left + b.right) / 2, (b.top + b.bottom) / 2, "Cells", 6, Red);
            }
        }
        var letters = "ABCDEFGHIKLMN";
        var labelled = Rng.Sample(Centre.Keys.ToList(), Math.Min(9, Centre.Count));
        for (var i = 0; i < labelled.Count; i++)
        {
            var (x, y) = Centre[labelled[i]];
            Sk.Text(x + R + 6, y - R - 2, letters[i].ToString(), 8, Red);
        }

        // the plate's furniture
        ScrollTitle("The Endless Archives", 22);
        (string K, string E)[] entries =
        [
            ("A", "Stair down from the Reading Room"), ("B", "Passage to the old Ledgers"), ("C", "Cell of the Clerk Abandoned"),
            ("D", "Vault under the Registry"), ("E", "Door walled up in 1776"), ("F", "Well of the Spilt Ink"),
            ("G", "Stacks of the Dead Files"), ("H", "Rotunda of the Registers"),
        ];
        Legend(Geo.Bounds(lb), "Legend", entries);
        var taken = Geo.Union(Geo.Buffer(open, 14), Geo.Buffer(lb, 8));
        if (FreeSpot(taken, 30, 64, deco) is { } nb)
            NorthArrow((nb.left + nb.right) / 2, (nb.top + nb.bottom) / 2 + 4);
        ToiseScale(W / 2.0 - 50, H - 36, 140, "Scale of twenty Toises");
        Sk.Text(W - 120, H - 22, "Surveyed in the Year 1776 by the Clerk of Works", 5, Ink);
    }

    private RectD? FreeSpot(PathsD taken, double w, double h, Rng rnd)
    {
        var inner = Geo.Buffer(Frame, -6);
        for (var i = 0; i < 400; i++)
        {
            var left = rnd.Next() < 0.5;
            var x = left ? rnd.Uniform(20, Side - w + 10) : rnd.Uniform(W - Side - 10, W - 20 - w);
            var y = rnd.Uniform(Top, H - Foot - 10 - h);
            var b = Geo.Box(x, y, x + w, y + h);
            if (!Geo.Intersects(b, taken) && Geo.Covers(inner, b))
                return Geo.Bounds(b);
        }
        return null;
    }

    // The act's name in a scroll cartouche: a double-ruled tablet with rolled ends and swashes.
    private void ScrollTitle(string name, double size)
    {
        double x0 = W / 2.0 - 160, x1 = W / 2.0 + 160, y0 = 12, y1 = 76;
        Sk.Rect(x0, y0, x1, y1, outline: Ink, width: 3);
        Sk.Rect(x0 + 4, y0 + 4, x1 - 4, y1 - 4, outline: Ink);
        foreach (var (x, d) in new[] { (x0, -1.0), (x1, 1.0) })
        {
            for (var k = 0; k < 4; k++)
            {
                var r = 4 + k * 3.2;
                Sk.Arc(x + d * 8, (y0 + y1) / 2, r, r, 0, k == 0 ? 360 : 300, Ink, k == 3 ? 2 : 1);
            }
            Sk.Line(Ink, 2, (x, y0), (x + d * 14, y0 - 4), (x + d * 20, y0 + 6));
            Sk.Line(Ink, 2, (x, y1), (x + d * 14, y1 + 4), (x + d * 20, y1 - 6));
        }
        Sk.Text(W / 2.0, 40, name, size, Ink);
        foreach (var sgn in new[] { -1.0, 1.0 })
            Sk.Line(Geo.Line([.. Enumerable.Range(0, 60).Select(t => (W / 2.0 + sgn * (40 + t * 1.8), 66 + 3 * Math.Sin(t / 8.0)))]), Ink, 1);
        Sk.Ellipse(W / 2.0 - 2, 64, W / 2.0 + 2, 68, fill: Ink);
    }

    private void Legend(RectD b, string heading, (string K, string E)[] entries)
    {
        Sk.Rect(b.left, b.top, b.right, b.bottom, outline: Ink, width: 2);
        Sk.Rect(b.left + 3, b.top + 3, b.right - 3, b.bottom - 3, outline: Ink);
        var mid = (b.left + b.right) / 2;
        Sk.Text(mid, b.top + 12, heading, 10, Ink);
        Sk.Line(Geo.Line([.. Enumerable.Range(0, 69).Select(t => (mid - 34 + t, b.top + 21 + 1.5 * Math.Sin(t / 4.0)))]), Ink, 1);
        for (var i = 0; i < entries.Length; i++)
        {
            var y = b.top + 32 + i * 10;
            Sk.Text(b.left + 12, y, entries[i].K, 7, Red);
            Sk.TextLeft(b.left + 19, y, entries[i].E, 5, Ink);
            for (var t = b.left + 21 + Sketch.TextWidth(entries[i].E, 5); t < b.right - 10; t += 2.2)
                Sk.Disc(t, y + 1.5, 0.35, Ink);
        }
    }

    // A needle with a fleur-de-lis head and a feathered tail.
    private void NorthArrow(double cx, double cy, double L = 34)
    {
        Sk.Line(Ink, 2, (cx, cy + L / 2), (cx, cy - L / 2));
        var tip = cy - L / 2;
        Sk.Poly([(cx, tip - 8), (cx - 3, tip), (cx + 3, tip)], fill: Ink);
        Sk.Arc(cx - 3, tip - 1, 5, 5, 180, 270, Ink, 2);
        Sk.Arc(cx + 3, tip - 1, 5, 5, 270, 360, Ink, 2);
        Sk.Line(Ink, 2, (cx - 4, tip + 4), (cx + 4, tip + 4));
        for (var k = 0; k < 3; k++)
        {
            var yy = cy + L / 2 - k * 3;
            Sk.Line(Ink, 1, (cx, yy - 3), (cx - 4, yy + 1));
            Sk.Line(Ink, 1, (cx, yy - 3), (cx + 4, yy + 1));
        }
        Sk.Text(cx, tip - 16, "N", 9, Ink);
    }

    private void ToiseScale(double x, double y, double length, string caption, int units = 4)
    {
        var seg = length / units;
        Sk.Line(Ink, 1, (x - seg, y - 1), (x + length, y - 1));
        Sk.Line(Ink, 1, (x - seg, y + 5), (x + length, y + 5));
        for (var i = 0; i < 10; i += 2)   // the first unit, in its ten parts, left of naught
        {
            var xx = x - seg + i * seg / 10;
            Sk.Rect(xx, y - 1, xx + seg / 10, y + 5, fill: Ink);
        }
        for (var i = 0; i < units; i++)
        {
            var x0 = x + i * seg;
            if (i % 2 == 1)
                Sk.Rect(x0, y - 1, x0 + seg, y + 5, fill: Ink);
            Sk.Line(Ink, 1, (x0, y - 4), (x0, y + 8));
            Sk.Text(x0, y + 13, (i * 5).ToString(), 5.5, Ink);
        }
        Sk.Line(Ink, 1, (x + length, y - 4), (x + length, y + 8));
        Sk.Text(x + length + 10, y + 13, $"{units * 5} Toises", 5.5, Ink);
        Sk.Text(x + length / 2 - seg / 2, y - 11, caption, 10, Ink);
        Sk.Arc(x - seg - 6, y + 2, 4, 4, 90, 270, Ink);
        Sk.Arc(x + length + 6, y + 2, 4, 4, -90, 90, Ink);
    }
}
