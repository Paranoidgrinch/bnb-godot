using Clipper2Lib;
using Godot;

namespace BnbGodot.MapArt;

// Act III, The Green Docket: a forest survey after the Hohenlohe maps of 1738/1799. The paths are brown forest
// roads, the rooms clearings; the forest round them is cut into parcels, each with its coloured rim, its letter,
// its numbered boundary stones and its trees (conifers, broadleaves, young growth, now and then a meadow); a
// brook runs to a pond. The eagle cartouche, the painted compass and the SCALA are the Weikersheim map's own
// (Landesarchiv Baden-Wuerttemberg, CC BY 4.0; assets/map/art/make_art.py).
public sealed class ForestPlan(Plan.Input input) : Plan(input)
{
    private static readonly Color Road = C(176, 120, 84, 200);
    private static readonly Color TreeG = C(96, 140, 118);
    private static readonly Color Coni = C(86, 128, 86);
    private static readonly Color Trunk = C(110, 80, 50);

    protected override void Draw()
    {
        var lines = PathLines(4, wander: 6);
        var roads = lines.Select(l => Geo.Wobble(Geo.Segmentize(l, 6), 1.2, 6, Rng, false)).ToList();
        var roadArea = Geo.Thicken(roads, 4);
        var clearings = Geo.Wobble(Geo.Union(Centre.Values.Select(c => Geo.Circle(c.X, c.Y, R + Rng.Uniform(6, 11)))), 2.5, 6, Rng);
        // a brook from high in one margin down to a pond
        var left = Rng.Next() < 0.5;
        var bx = left ? Side * Rng.Uniform(0.3, 0.6) : W - Side * Rng.Uniform(0.3, 0.6);
        var by0 = Rng.Uniform(100, 300);
        var by1 = Rng.Uniform(H * 0.45, H - 220);
        var ph = Rng.Uniform(0, 9);
        var brook = new PathD();
        for (var y = by0; y < by1; y += 5)
            brook.Add(new PointD(bx + 14 * Math.Sin(y * 0.02 + ph), y));
        var end = brook[^1].x;
        var pond = Geo.Wobble(Geo.Union(Geo.Circle(end, by1 + 18, Rng.Uniform(17, 25)), Geo.Circle(end + Rng.Uniform(-10, 10), by1 + 34, Rng.Uniform(10, 16))), 2, 5, Rng);
        // the survey's own pieces sit at fixed spots
        var cartLeft = Rng.Next() < 0.5;
        var cart = cartLeft ? (X: 20.0, Y: Top - 4.0) : (X: W - 160.0, Y: Top - 4.0);
        var comp = cartLeft ? (X: W - 118.0, Y: H - Foot - 118.0) : (X: 22.0, Y: H - Foot - 118.0);
        var reserve = Geo.Union(
            Geo.Box(W / 2.0 - 150, 16, W / 2.0 + 150, 78),
            Geo.Box(cart.X - 2, cart.Y - 2, cart.X + 142, cart.Y + 88),
            Geo.Box(comp.X - 2, comp.Y - 2, comp.X + 98, comp.Y + 92),
            Geo.Box(W / 2.0 - 100, H - 52, W / 2.0 + 100, H - 18));
        var forest = Geo.Difference(Geo.Difference(Geo.Difference(Geo.Difference(Geo.Difference(Frame,
            Geo.Buffer(roadArea, 1.5)), clearings), Geo.Buffer(pond, 3)), Geo.Thicken(brook, 4)), reserve);
        var letters = "ABCDEFGHIKLMNOPQRS";
        var nextLetter = 0;
        var frameIn = Geo.Buffer(Frame, -6);
        foreach (var p in Geo.SplitParcels(forest, Rng.Uniform(4500, 8000), 0.1, Rng))
        {
            var q = Geo.Buffer(p, -2);
            if (Geo.IsEmpty(q))
                continue;
            var kind = Rng.Choices(["high", "young", "mixed", "meadow"], [0.35, 0.25, 0.3, 0.1]);
            var rim = Rng.Choice([C(150, 190, 140, 150), C(220, 200, 120, 150), C(225, 170, 150, 150), C(160, 190, 200, 150)]);
            Sk.FillShape(Geo.Difference(q, Geo.Buffer(q, -5)), rim);   // the coloured rim along the parcel's edge
            if (kind == "meadow")
                Sk.FillShape(q, C(232, 222, 170, 140));
            var b = Geo.Bounds(q);
            var inner = Geo.Buffer(q, -5);
            var n = (int)(Geo.Area(q) / kind switch { "high" => 80.0, "young" => 22.0, "mixed" => 70.0, _ => 500.0 });
            var spots = Enumerable.Range(0, n).Select(_ => (X: Rng.Uniform(b.left, b.right), Y: Rng.Uniform(b.top, b.bottom))).OrderBy(c => c.Y).ToList();
            foreach (var (x, y) in spots)
            {
                if (!Geo.Contains(inner, x, y))
                    continue;
                if (kind == "young")
                    Sk.Line(Ink, 1, (x - 1.2, y + 1), (x, y - 1.6), (x + 1.2, y + 1));
                else if (kind == "high" || Rng.Next() >= 0.6)
                    Conifer(x, y, Rng.Uniform(3, 4.5));
                else
                    Broadleaf(x, y, Rng.Uniform(3, 4.5));
            }
            for (var k = 0; k < (int)(Geo.Area(q) / 300); k++)
            {
                var x = Rng.Uniform(b.left, b.right);
                var y = Rng.Uniform(b.top, b.bottom);
                if (Geo.Contains(inner, x, y))
                    Sk.Line(C(90, 130, 80, 200), 1, (x - 1.5, y), (x, y - 2), (x + 1.5, y));
            }
            foreach (var ring in Geo.Rings(p))
                Sk.Dotted(ring, 3.2, Ink, 0.45);
            if (Geo.Area(q) > 3500)
            {
                var c = Geo.Inside(q);
                Sk.Text(c.x, c.y, nextLetter < letters.Length ? letters[nextLetter++].ToString() : "Z", 12, Ink);
            }
            // boundary stones, some numbered
            foreach (var ring in Geo.Rings(p))
            {
                var L = Geo.Length(ring);
                for (var t = Rng.Uniform(0, 30); t < L; t += Rng.Uniform(50, 90))
                {
                    var s = Geo.At(ring, t);
                    if (!Geo.Contains(frameIn, s.x, s.y))
                        continue;
                    Sk.Rect(s.x - 1.6, s.y - 2.2, s.x + 1.6, s.y + 1.6, fill: Ink);
                    if (Rng.Next() < 0.5)
                        Sk.Text(s.x + 5, s.y - 3, Rng.RandInt(1, 64).ToString(), 4.5, Ink);
                }
            }
        }
        // the clearings: a light wash and a green edge
        Sk.FillShape(clearings, C(236, 226, 170, 120));
        Sk.Lines(Geo.Rings(clearings), C(110, 140, 90, 160), 1);
        // the roads: a brown band with dark edges
        foreach (var l in roads)
        {
            Sk.Line(l, Road, 6 * Sketch.Scale);
            foreach (var off in new[] { -3.2, 3.2 })
                Sk.Line(Geo.Offset(l, off), C(90, 60, 40, 200), 1);
        }
        // brook and pond
        Sk.Line(brook, C(110, 130, 150, 230), 3 * Sketch.Scale);
        Sk.Outline(pond, Rng, 2, C(70, 80, 95), C(140, 160, 175), amp: 0);
        Sk.Hatch(Geo.Buffer(pond, -2), 2.4, 0, 1, C(70, 80, 95, 120));
        var pc = Geo.Centroid(pond[0]);
        Sk.Text(pc.x, pc.y + 28, "The Docket Pond", 6, Ink);
        // the survey's own furniture
        Sk.Picture("res://assets/map/art/weik_cartouche.png", cart.X, cart.Y, 140, 85);
        Sk.Picture("res://assets/map/art/weik_compass.png", comp.X, comp.Y, 96, 89.5);
        Sk.Picture("res://assets/map/art/weik_scala.png", W / 2.0 - 90, H - 50, 180, 48);
        Title("The Green Docket");
    }

    private void Broadleaf(double x, double y, double r)
    {
        Sk.Line(Trunk, 2, (x, y + r * 0.6), (x, y + r * 1.8));
        Sk.Ellipse(x - r, y - r, x + r, y + r * 0.9, fill: TreeG, outline: Ink);
        for (var k = 0; k < 3; k++)
            Sk.Disc(x + Rng.Uniform(-r * 0.5, r * 0.5), y + Rng.Uniform(-r * 0.5, r * 0.4), 0.4, Ink);
    }

    private void Conifer(double x, double y, double r)
    {
        Sk.Line(Trunk, 1, (x, y + r * 0.2), (x, y + r * 1.6));
        for (var k = 0; k < 3; k++)
        {
            var yy = y - r + k * r * 0.6;
            var w = r * (0.45 + k * 0.25);
            PathD tri = [new(x, yy - r * 0.3), new(x - w, yy + r * 0.55), new(x + w, yy + r * 0.55)];
            Sk.FillConvex(tri, Coni);
            Sk.Line(new PathD(tri) { tri[0] }, Ink, 1);
        }
    }
}
