using Clipper2Lib;

namespace BnbGodot.MapArt;

// Act I, The Old City Offices: a walled town after the Price/Bonner plans of Boston. The paths are its streets,
// the rooms its squares, the blocks between them rows of houses round gardens and yards; a wall with bastions
// and a moat closes it; roads leave by the gates into fields, orchards and woods, a river winds by, a windmill
// or two stands in the country (user: a town, not a harbour, 2026-11-11).
public sealed class TownPlan(Plan.Input input) : Plan(input)
{
    protected override void Draw()
    {
        var lines = PathLines(3);
        var streets = lines.Select(l => Geo.Thicken(l, 5.0)).ToList();
        var squares = new List<PathsD>();
        foreach (var (id, (x, y)) in Centre)
        {
            var rot = Rng.Uniform(0, Math.PI);
            var r = R + 3 + (Role[id] is "boss" or "shop" ? 5 : 0);
            squares.Add(Geo.Polygon(Enumerable.Range(0, 8).Select(i =>
                (x + r * Math.Cos(rot + i * Math.PI / 4) * Rng.Uniform(0.92, 1.08), y + r * Math.Sin(rot + i * Math.PI / 4) * Rng.Uniform(0.92, 1.08)))));
        }
        var open = Geo.Union(streets.Concat(squares));
        var town = Geo.Buffer(Geo.Buffer(Geo.Thicken(lines, Rng.Uniform(44, 56)), -16), 6);
        town = Geo.Wobble(town, 4, 10, Rng);

        // the wall: the town's outline straightened, a bastion at every outward corner, a moat outside
        var hull = Geo.Simplify(Geo.Buffer(town, 10, JoinType.Miter), Rng.Uniform(16, 26));
        hull = Geo.Parts(hull).OrderByDescending(Geo.Area).Take(1).SelectMany(p => p).Take(1).ToList() is { Count: > 0 } h ? [h[0]] : hull;
        var ring = hull[0];
        var bastions = new List<PathsD>();
        for (var i = 0; i < ring.Count; i++)
        {
            var v = ring[i];
            var a = ring[(i - 1 + ring.Count) % ring.Count];
            var b = ring[(i + 1) % ring.Count];
            var (e1x, e1y) = Unit(a.x - v.x, a.y - v.y);
            var (e2x, e2y) = Unit(b.x - v.x, b.y - v.y);
            var nx = -(e1x + e2x);
            var ny = -(e1y + e2y);
            var nl = Math.Sqrt(nx * nx + ny * ny);
            if (nl < 0.3)
                continue;
            nx /= nl;
            ny /= nl;
            if (!Geo.Contains(hull, v.x - nx * 3, v.y - ny * 3))
                continue;  // a re-entrant corner: no bastion
            var s = Rng.Uniform(13, 18);
            bastions.Add(Geo.Polygon([
                (v.x + e1x * 14, v.y + e1y * 14), (v.x + e1x * 6 + nx * s * 0.9, v.y + e1y * 6 + ny * s * 0.9), (v.x + nx * s * 1.4, v.y + ny * s * 1.4),
                (v.x + e2x * 6 + nx * s * 0.9, v.y + e2y * 6 + ny * s * 0.9), (v.x + e2x * 14, v.y + e2y * 14)]));
        }
        var walled = Geo.Union([hull, .. bastions]);
        var wall = Geo.Difference(walled, Geo.Buffer(walled, -5, JoinType.Miter));
        var moat = Geo.Difference(Geo.Buffer(walled, 12, JoinType.Miter), Geo.Buffer(walled, 1));

        // the ways out: from the rooms nearest the sides, roads to the frame's sides, and from the boss to the foot
        var bySide = Centre.Values.OrderBy(c => c.X).ToList();
        var outs = new List<PathD>();
        foreach (var (x, y) in bySide.Take(2).Concat(bySide.TakeLast(2)))
        {
            var left = x < W / 2.0;
            outs.Add(Geo.Line((x, y), (left ? x - 40 : x + 40, y + Rng.Uniform(-10, 10)), (left ? 16 : W - 16, y + Rng.Uniform(-60, 60))));
        }
        var boss = Boss;
        outs.Add(Geo.Line(boss, (boss.X + Rng.Uniform(-15, 15), boss.Y + 40), (boss.X + Rng.Uniform(-60, 60), H - 16)));
        var roads = outs.Select(l => Geo.Wobble(Geo.Segmentize(l, 8), 2, 8, Rng, false)).ToList();
        var roadArea = Geo.Thicken(roads, 3.5);

        // a river down one side of the country
        var sideLeft = Rng.Next() < 0.5;
        var rx0 = sideLeft ? Side * Rng.Uniform(0.3, 0.5) : W - Side * Rng.Uniform(0.3, 0.5);
        var ph = Rng.Uniform(0, 10);
        var keepOut = Geo.Buffer(walled, 26);
        var riverLine = new PathD();
        for (var y = 78; y < H - 16; y += 6)
        {
            var x = rx0 + 22 * Math.Sin(y * 0.011 + ph) + 9 * Math.Sin(y * 0.037 + ph * 2);
            if (!Geo.Contains(keepOut, x, y))
                riverLine.Add(new PointD(x, y));
        }
        var river = riverLine.Count > 1 ? Geo.Difference(Geo.Thicken(riverLine, Rng.Uniform(5, 7), round: false), Geo.Buffer(walled, 24)) : Geo.Empty;

        var cxC = sideLeft ? W - 62 : 62;
        var cyC = H - Foot - 40;
        var sx = sideLeft ? W - 200 : 30;
        var reserve = Geo.Union(Geo.Box(W / 2.0 - 170, 16, W / 2.0 + 170, 78), Geo.Circle(cxC, cyC, 42), Geo.Box(sx - 6, H - 52, sx + 176, H - 18));

        // fields round the walls
        var land = Geo.Difference(Geo.Difference(Geo.Difference(Geo.Difference(Frame, Geo.Buffer(walled, 16)), Geo.Buffer(river, 4)), Geo.Buffer(roadArea, 2)), reserve);
        foreach (var p in Geo.SplitParcels(land, Rng.Uniform(900, 1600), 1.2, Rng))
        {
            var q = Geo.Buffer(p, -1.2);
            if (Geo.Area(q) < 60)
                continue;
            var kind = Rng.Choices(["plough", "meadow", "orchard", "wood", "garden"], [0.42, 0.22, 0.14, 0.12, 0.1]);
            var b = Geo.Bounds(q);
            if (kind == "plough")
                Sk.Hatch(q, Rng.Uniform(2.6, 3.4), Rng.Uniform(0, 180), 1, C(52, 36, 24, 110));
            else if (kind == "meadow")
            {
                for (var k = 0; k < (int)(Geo.Area(q) / 45); k++)
                {
                    var x = Rng.Uniform(b.left, b.right);
                    var y = Rng.Uniform(b.top, b.bottom);
                    if (Geo.Contains(q, x, y))
                        Sk.Line(Soft, 1, (x - 1.2, y - 1.5), (x, y), (x + 1.2, y - 1.5));
                }
            }
            else if (kind is "orchard" or "wood")
            {
                var step = kind == "orchard" ? 5.5 : 4.2;
                var inner = Geo.Buffer(q, -2);
                for (var yy = b.top + 3; yy < b.bottom; yy += step)
                    for (var xx = b.left + 3 + (kind == "wood" ? Rng.Uniform(0, 2) : 0); xx < b.right; xx += step)
                    {
                        var (jx, jy) = kind == "orchard" ? (0.0, 0.0) : (Rng.Uniform(-1.5, 1.5), Rng.Uniform(-1.5, 1.5));
                        if (Geo.Contains(inner, xx + jx, yy + jy))
                            TreeDot(xx + jx, yy + jy, kind == "orchard" ? 1.4 : Rng.Uniform(1.3, 2.2));
                    }
            }
            else
            {
                Sk.Hatch(q, 4, Rng.Choice([0.0, 90.0]), 1, C(52, 36, 24, 90));
                Sk.Hatch(q, 4, Rng.Choice([45.0, 135.0]), 1, C(52, 36, 24, 50));
            }
            Sk.Outline(p, Rng, 1, C(52, 36, 24, 170), amp: 0.5);
        }
        // farmhouses along the roads, a windmill or two in the country
        foreach (var l in roads)
            for (var k = Rng.RandInt(1, 3); k > 0; k--)
            {
                var q = Geo.At(l, Geo.Length(l) * Rng.Uniform(0.2, 0.9));
                var hb = Geo.Rotate(Geo.Box(q.x + 6, q.y - 3, q.x + 14, q.y + 2), Rng.Uniform(0, 180), q.x, q.y);
                if (Geo.Covers(land, hb))
                    Sk.Outline(hb, Rng, 1, fill: Ink, amp: 0);
            }
        var landIn = Geo.Buffer(land, -4);
        for (var m = Rng.RandInt(1, 2); m > 0; m--)
            for (var k = 0; k < 80; k++)
            {
                var x = Rng.Uniform(20, W - 20);
                var y = Rng.Uniform(90, H - 30);
                if (Geo.Covers(landIn, Geo.Box(x - 16, y - 34, x + 16, y + 6)))
                {
                    PostMill(x, y, Rng.Uniform(-25, 25));
                    break;
                }
            }

        // the river: banks, engraved water lines, a bridge where a road crosses
        foreach (var p in Geo.Parts(river))
        {
            Sk.Hatch(p, 2.2, 0, 1, C(70, 62, 52, 120));
            Sk.Outline(p, Rng, 2, amp: 0.6);
        }
        foreach (var seg in Geo.ClipLines(roads, Geo.Buffer(river, 3)))
            if (Geo.Length(seg) > 1)
                foreach (var off in new[] { -4.0, 4.0 })
                    Sk.Line(Geo.Offset(seg, off), Ink, 2);
        // the roads: double lines, an avenue of trees on some
        var noRoad = Geo.Union(Geo.Buffer(walled, 2), river);
        foreach (var l in roads)
        {
            foreach (var off in new[] { -3.5, 3.5 })
                Sk.Lines(Geo.ClipLines([Geo.Offset(l, off)], noRoad, keep: false), Ink);
            if (Rng.Next() < 0.6)
                for (var t = 10.0; t < Geo.Length(l); t += 7)
                    foreach (var off in new[] { -7.0, 7.0 })
                    {
                        var q = Geo.At(Geo.Offset(l, off), t);
                        if (Geo.Contains(land, q.x, q.y))
                            Sk.Ellipse(q.x - 1.6, q.y - 1.6, q.x + 1.6, q.y + 1.6, outline: Ink);
                    }
        }
        // moat and wall, the gates where the roads pass
        foreach (var p in Geo.Parts(Geo.Difference(moat, roadArea)))
        {
            Sk.Hatch(p, 2.0, 0, 1, C(70, 62, 52, 110));
            Sk.Outline(p, Rng, 1, amp: 0.4);
        }
        foreach (var p in Geo.Parts(Geo.Difference(wall, Geo.Buffer(roadArea, 2))))
        {
            Sk.Hatch(p, 1.4, 45, 1, Ink);
            Sk.Outline(p, Rng, 2, amp: 0);
        }
        foreach (var gate in Geo.Parts(Geo.Intersect(Geo.Buffer(roadArea, 1), Geo.Difference(Geo.Buffer(walled, 2), Geo.Buffer(walled, -2)))))
        {
            var c = Geo.Inside(gate);
            foreach (var off in new[] { -7.0, 7.0 })
                Sk.Rect(c.x - 3 + off * 0.7, c.y - 3 + off * 0.7, c.x + 3 + off * 0.7, c.y + 3 + off * 0.7, fill: Ink, outline: Ink);
        }

        // inside the walls: blocks of houses round the streets
        var inside = Geo.Buffer(walled, -7);
        var blocks = new List<PathsD>();
        foreach (var p in Geo.Parts(Geo.Difference(inside, Geo.Buffer(open, 0.5))))
        {
            if (Geo.Area(p) < 80)
                continue;
            var pieces = new List<PathsD> { p };
            for (var pass = 0; pass < 2; pass++)
            {
                var next = new List<PathsD>();
                foreach (var q in pieces)
                {
                    if (Geo.Area(q) > 3000)
                    {
                        var c = Geo.Centroid(q[0]);
                        var ang = Rng.Uniform(0, Math.PI);
                        var alley = Geo.Thicken(Geo.Line((c.x - 400 * Math.Cos(ang), c.y - 400 * Math.Sin(ang)), (c.x + 400 * Math.Cos(ang), c.y + 400 * Math.Sin(ang))), 2.4);
                        next.AddRange(Geo.Parts(Geo.Difference(q, alley)).Where(r => Geo.Area(r) > 60));
                    }
                    else
                        next.Add(q);
                }
                pieces = next;
            }
            blocks.AddRange(pieces);
        }
        var churches = Rng.Sample(Enumerable.Range(0, blocks.Count).ToList(), Math.Min(2, blocks.Count)).ToHashSet();
        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            const double band = 6.5;
            var inner = Geo.Buffer(b, -band);
            var kind = Rng.Choices(["garden", "yard", "dense"], [0.4, 0.35, 0.25]);
            if (kind == "dense" && Geo.Area(b) > 1400)
                kind = "garden";
            if (kind == "dense" || Geo.IsEmpty(inner))
                Sk.Hatch(b, 1.8, Rng.Choice([40.0, 50.0, -40.0]), 1);
            else
            {
                Sk.Hatch(Geo.Difference(b, inner), 1.8, Rng.Choice([40.0, 50.0, -40.0]), 1);
                // the house fronts: a short cut across the band every few steps
                var ringL = new PathD(b[0]) { b[0][0] };
                var L = Geo.Length(ringL);
                for (var t = 0.0; t < L; t += Rng.Uniform(4, 8))
                {
                    var p0 = Geo.At(ringL, t);
                    var (dx, dy) = Geo.Dir(ringL, t);
                    double nx = -dy, ny = dx;
                    if (!Geo.Contains(b, p0.x + nx * band, p0.y + ny * band))
                        (nx, ny) = (-nx, -ny);
                    Sk.Line(Ink, 1, (p0.x, p0.y), (p0.x + nx * band, p0.y + ny * band));
                }
                foreach (var p in Geo.Parts(inner))
                {
                    Sk.Outline(p, Rng, 1, Soft);
                    if (kind != "garden")
                        continue;
                    var bb = Geo.Bounds(p);
                    var pin = Geo.Buffer(p, -2.5);
                    for (var yy = bb.top + 4; yy < bb.bottom - 3; yy += Rng.Uniform(4.8, 6))
                        for (var xx = bb.left + 4 + Rng.Uniform(0, 3); xx < bb.right - 3; xx += Rng.Uniform(4.8, 6))
                            if (Geo.Contains(pin, xx, yy))
                                Sk.Ellipse(xx - 1.3, yy - 1.3, xx + 1.3, yy + 1.3, outline: Ink);
                }
            }
            if (churches.Contains(i) && Geo.Area(b) > 500)
            {
                var c = Geo.Inside(b);
                var cross = Geo.Rotate(Geo.Union(Geo.Box(c.x - 12, c.y - 3.5, c.x + 12, c.y + 3.5), Geo.Box(c.x - 4, c.y - 8, c.x + 2, c.y + 8)), Rng.Uniform(0, 180), c.x, c.y);
                if (Geo.Covers(Geo.Buffer(b, -1), cross))
                    Sk.Outline(cross, Rng, 1, fill: Ink, amp: 0);
            }
            Sk.Outline(b, Rng, 2);
        }
        foreach (var b in blocks.OrderByDescending(Geo.Area).Take(8))
        {
            var c = Geo.Inside(b);
            Sk.Text(c.x, c.y, $"No {Rng.RandInt(1, 12)}", 7, Ink);
        }
        string[] names = ["Ledger Lane", "Docket Row", "Stamp Street", "Quill Alley", "Seal Lane", "Ink Street", "Form Street"];
        foreach (var (name, l) in names.Zip(lines.OrderByDescending(Geo.Length).Take(5)))
            TextAlong(l, name, 5, Ink);
        if (riverLine.Count > 1 && !Geo.IsEmpty(river))
        {
            var c = Geo.At(riverLine, Geo.Length(riverLine) * 0.35);
            Sk.Text(c.x + (sideLeft ? 14 : -14), c.y, "The Quill", 7, Ink, 90);
        }

        Title("The Old City Offices");
        Compass(cxC, cyC);
        ScaleBar(sx, H - 44, 160, "Scale of half a Mile");
    }

    private static (double X, double Y) Unit(double x, double y)
    {
        var n = Math.Sqrt(x * x + y * y);
        return n < 1e-9 ? (0, 0) : (x / n, y / n);
    }
}
