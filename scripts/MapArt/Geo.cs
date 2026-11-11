using Clipper2Lib;

namespace BnbGodot.MapArt;

// The geometry the map art is made of, on Clipper2 (Boost licence): a Shape is a set of rings filled by the
// non-zero rule, the way every boolean result comes back from Clipper; a polyline is a PathD left open. This is
// the C# side of the prototype's shapely calls (bnb-mapwork/mapgen/plans.py), one helper for each one it used,
// in map units (the map's own pixels, before the art's 2x render scale).
public static class Geo
{
    private const int Precision = 3;

    public static PathsD Empty => [];

    // ── making shapes ────────────────────────────────────────────────────────────

    public static PathsD Box(double x0, double y0, double x1, double y1) =>
        [[new PointD(x0, y0), new PointD(x1, y0), new PointD(x1, y1), new PointD(x0, y1)]];

    public static PathsD Circle(double x, double y, double r, int n = 40)
    {
        var p = new PathD(n);
        for (var i = 0; i < n; i++)
            p.Add(new PointD(x + r * Math.Cos(i * 2 * Math.PI / n), y + r * Math.Sin(i * 2 * Math.PI / n)));
        return [p];
    }

    public static PathsD Polygon(IEnumerable<(double X, double Y)> points) =>
        [new PathD(points.Select(p => new PointD(p.X, p.Y)))];

    public static PathD Line(params (double X, double Y)[] points) => new(points.Select(p => new PointD(p.X, p.Y)));

    // ── booleans and buffers ─────────────────────────────────────────────────────

    public static PathsD Union(params PathsD[] shapes)
    {
        var all = new PathsD();
        foreach (var s in shapes)
            all.AddRange(s);
        return Clipper.Union(all, FillRule.NonZero);
    }

    public static PathsD Union(IEnumerable<PathsD> shapes) => Union(shapes.ToArray());

    public static PathsD Difference(PathsD a, PathsD b) => Clipper.Difference(a, b, FillRule.NonZero, Precision);

    public static PathsD Intersect(PathsD a, PathsD b) => Clipper.Intersect(a, b, FillRule.NonZero, Precision);

    // shapely's buffer: positive grows, negative shrinks
    public static PathsD Buffer(PathsD s, double d, JoinType join = JoinType.Round, double miter = 2.0) =>
        s.Count == 0 ? [] : Clipper.InflatePaths(s, d, join, EndType.Polygon, miter, Precision);

    // a polyline thickened to a band d either side
    public static PathsD Thicken(PathD line, double d, bool round = true, JoinType join = JoinType.Round) =>
        Clipper.InflatePaths([line], d, join, round ? EndType.Round : EndType.Butt, 2.0, Precision);

    public static PathsD Thicken(IEnumerable<PathD> lines, double d, bool round = true, JoinType join = JoinType.Round) =>
        Clipper.InflatePaths(new PathsD(lines), d, join, round ? EndType.Round : EndType.Butt, 2.0, Precision);

    // ── reading shapes ───────────────────────────────────────────────────────────

    public static double Area(PathsD s) => Math.Abs(Clipper.Area(s));

    public static bool IsEmpty(PathsD s) => s.Count == 0 || Area(s) < 1e-6;

    public static RectD Bounds(PathsD s) => Clipper.GetBounds(s);

    // Inside by the even-odd count of rings, which after a boolean (rings never cross) is the non-zero rule.
    public static bool Contains(PathsD s, double x, double y)
    {
        var pt = new PointD(x, y);
        var inside = 0;
        foreach (var ring in s)
            if (Clipper.PointInPolygon(pt, ring, Precision) == PointInPolygonResult.IsInside)
                inside++;
        return inside % 2 == 1;
    }

    // `b` lies wholly in `a`
    public static bool Covers(PathsD a, PathsD b) => Area(Difference(b, a)) < 1e-3;

    public static bool Intersects(PathsD a, PathsD b) =>
        a.Count > 0 && b.Count > 0 && Bounds(a).Intersects(Bounds(b)) && Area(Intersect(a, b)) > 1e-3;

    // Each polygon of a shape with its own holes, as shapely's `.geoms` gives them.
    public static List<PathsD> Parts(PathsD s)
    {
        var tree = new PolyTreeD();
        Clipper.BooleanOp(ClipType.Union, s, null, tree, FillRule.NonZero, Precision);
        var outs = new List<PathsD>();
        void Walk(PolyPathD node)
        {
            for (var i = 0; i < node.Count; i++)
            {
                var outer = node[i];
                var part = new PathsD { outer.Polygon! };
                for (var j = 0; j < outer.Count; j++)
                {
                    part.Add(outer[j].Polygon!);
                    Walk(outer[j]);   // islands inside a hole
                }
                outs.Add(part);
            }
        }
        Walk(tree);
        return outs;
    }

    // The rings as closed polylines (the boundary).
    public static IEnumerable<PathD> Rings(PathsD s)
    {
        foreach (var r in s)
        {
            if (r.Count < 2)
                continue;
            var c = new PathD(r) { r[0] };
            yield return c;
        }
    }

    public static PointD Centroid(PathD ring)
    {
        double a = 0, cx = 0, cy = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var p = ring[i];
            var q = ring[(i + 1) % ring.Count];
            var f = p.x * q.y - q.x * p.y;
            a += f;
            cx += (p.x + q.x) * f;
            cy += (p.y + q.y) * f;
        }
        if (Math.Abs(a) < 1e-9)
            return ring.Count > 0 ? ring[0] : new PointD(0, 0);
        return new PointD(cx / (3 * a), cy / (3 * a));
    }

    // A point surely inside (shapely's representative_point): the centroid if it is in, else the middle of the
    // widest run of a horizontal line through the middle.
    public static PointD Inside(PathsD s)
    {
        var parts = Parts(s);
        if (parts.Count == 0)
            return new PointD(0, 0);
        var big = parts.OrderByDescending(Area).First();
        var c = Centroid(big[0]);
        if (Contains(big, c.x, c.y))
            return c;
        var b = Bounds(big);
        var y = (b.top + b.bottom) / 2;
        var best = (Len: 0.0, Mid: c);
        foreach (var seg in ClipLines([Line((b.left - 1, y), (b.right + 1, y))], big))
        {
            var len = Length(seg);
            if (len > best.Len)
                best = (len, At(seg, len / 2));
        }
        return best.Mid;
    }

    // ── open lines ───────────────────────────────────────────────────────────────

    // The parts of open lines inside (keep) or outside (!keep) a shape.
    public static PathsD ClipLines(IEnumerable<PathD> lines, PathsD shape, bool keep = true)
    {
        var c = new ClipperD(Precision);
        c.AddOpenSubject(new PathsD(lines));
        c.AddClip(shape);
        var closed = new PathsD();
        var open = new PathsD();
        c.Execute(keep ? ClipType.Intersection : ClipType.Difference, FillRule.NonZero, closed, open);
        return open;
    }

    public static double Length(PathD l)
    {
        double s = 0;
        for (var i = 1; i < l.Count; i++)
            s += Dist(l[i - 1], l[i]);
        return s;
    }

    public static double Dist(PointD a, PointD b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

    // The point at distance t along a line (shapely's interpolate).
    public static PointD At(PathD l, double t)
    {
        if (l.Count == 0)
            return new PointD(0, 0);
        if (t <= 0)
            return l[0];
        for (var i = 1; i < l.Count; i++)
        {
            var d = Dist(l[i - 1], l[i]);
            if (t <= d && d > 0)
            {
                var k = t / d;
                return new PointD(l[i - 1].x + (l[i].x - l[i - 1].x) * k, l[i - 1].y + (l[i].y - l[i - 1].y) * k);
            }
            t -= d;
        }
        return l[^1];
    }

    // The unit direction of the line at distance t.
    public static (double X, double Y) Dir(PathD l, double t)
    {
        var a = At(l, Math.Max(0, t - 0.5));
        var b = At(l, t + 0.5);
        var d = Dist(a, b);
        return d < 1e-9 ? (1, 0) : ((b.x - a.x) / d, (b.y - a.y) / d);
    }

    // Extra points every `step` (shapely's segmentize).
    public static PathD Segmentize(PathD l, double step)
    {
        var o = new PathD();
        for (var i = 0; i < l.Count; i++)
        {
            if (i > 0)
            {
                var d = Dist(l[i - 1], l[i]);
                var n = (int)Math.Ceiling(d / step);
                for (var k = 1; k < n; k++)
                    o.Add(new PointD(l[i - 1].x + (l[i].x - l[i - 1].x) * k / n, l[i - 1].y + (l[i].y - l[i - 1].y) * k / n));
            }
            o.Add(l[i]);
        }
        return o;
    }

    // A line offset sideways (shapely's parallel_offset; positive to the left of the direction of travel).
    public static PathD Offset(PathD l, double d)
    {
        var o = new PathD(l.Count);
        for (var i = 0; i < l.Count; i++)
        {
            var a = l[Math.Max(0, i - 1)];
            var b = l[Math.Min(l.Count - 1, i + 1)];
            var dx = b.x - a.x;
            var dy = b.y - a.y;
            var n = Math.Sqrt(dx * dx + dy * dy);
            if (n < 1e-9)
            {
                o.Add(l[i]);
                continue;
            }
            o.Add(new PointD(l[i].x - dy / n * d, l[i].y + dx / n * d));
        }
        return o;
    }

    public static double DistToLines(IEnumerable<PathD> lines, double x, double y)
    {
        var best = double.MaxValue;
        foreach (var l in lines)
            for (var i = 1; i < l.Count; i++)
                best = Math.Min(best, SegDist(l[i - 1], l[i], x, y));
        return best;
    }

    public static double SegDist(PointD a, PointD b, double x, double y)
    {
        var dx = b.x - a.x;
        var dy = b.y - a.y;
        var L = dx * dx + dy * dy;
        var t = L < 1e-12 ? 0 : Math.Clamp(((x - a.x) * dx + (y - a.y) * dy) / L, 0, 1);
        var px = a.x + t * dx - x;
        var py = a.y + t * dy - y;
        return Math.Sqrt(px * px + py * py);
    }

    // The nearest pair of points between a box-like shape and another shape (by their outlines).
    public static (PointD A, PointD B) Nearest(PathsD a, PathsD b)
    {
        var best = (D: double.MaxValue, A: new PointD(0, 0), B: new PointD(0, 0));
        var ringsB = Rings(b).Select(r => Segmentize(r, 3)).ToList();
        foreach (var ra in Rings(a).Select(r => Segmentize(r, 2)))
            foreach (var p in ra)
                foreach (var rb in ringsB)
                    for (var i = 1; i < rb.Count; i++)
                    {
                        var d = SegDist(rb[i - 1], rb[i], p.x, p.y);
                        if (d < best.D)
                        {
                            var q = Closest(rb[i - 1], rb[i], p.x, p.y);
                            best = (d, p, q);
                        }
                    }
        return (best.A, best.B);
    }

    private static PointD Closest(PointD a, PointD b, double x, double y)
    {
        var dx = b.x - a.x;
        var dy = b.y - a.y;
        var L = dx * dx + dy * dy;
        var t = L < 1e-12 ? 0 : Math.Clamp(((x - a.x) * dx + (y - a.y) * dy) / L, 0, 1);
        return new PointD(a.x + t * dx, a.y + t * dy);
    }

    // ── transforms ───────────────────────────────────────────────────────────────

    public static PathsD Rotate(PathsD s, double degrees, double ox, double oy)
    {
        var a = degrees * Math.PI / 180;
        var c = Math.Cos(a);
        var si = Math.Sin(a);
        return new PathsD(s.Select(r => new PathD(r.Select(p =>
            new PointD(ox + (p.x - ox) * c - (p.y - oy) * si, oy + (p.x - ox) * si + (p.y - oy) * c)))));
    }

    public static PathsD Translate(PathsD s, double dx, double dy) =>
        new(s.Select(r => new PathD(r.Select(p => new PointD(p.x + dx, p.y + dy)))));

    public static PathsD Simplify(PathsD s, double tolerance) => Clipper.RamerDouglasPeucker(s, tolerance);

    // Hand-drawn: rings and lines given extra points and pushed by slow sines, as the prototype's wobbly().
    public static PathD Wobble(PathD l, double amp, double step, Rng rng, bool closed)
    {
        var src = closed && l.Count > 0 ? new PathD(l) { l[0] } : l;
        var seg = Segmentize(src, step);
        var ph = rng.Uniform(0, 100);
        var o = new PathD(seg.Count);
        foreach (var p in seg)
            o.Add(new PointD(
                p.x + amp * (Math.Sin(p.x * 0.11 + ph) + 0.5 * Math.Sin(p.y * 0.23 + ph * 1.7)),
                p.y + amp * (Math.Cos(p.y * 0.13 + ph) + 0.5 * Math.Cos(p.x * 0.19 + ph * 0.6))));
        if (closed && o.Count > 1)
            o.RemoveAt(o.Count - 1);
        return o;
    }

    public static PathsD Wobble(PathsD s, double amp, double step, Rng rng) =>
        s.Count == 0 ? s : Clipper.Union(new PathsD(s.Select(r => Wobble(r, amp, step, rng, true))), FillRule.NonZero);

    // A region cut into parcels of about `target` area by straight cuts across its long side (split_parcels).
    public static List<PathsD> SplitParcels(PathsD region, double target, double cutWidth, Rng rng)
    {
        var outs = new List<PathsD>();
        var todo = Parts(region).Where(p => Area(p) > 40).ToList();
        var guard = 0;
        while (todo.Count > 0 && guard++ < 5000)
        {
            var p = todo[^1];
            todo.RemoveAt(todo.Count - 1);
            if (Area(p) < target * rng.Uniform(0.7, 1.4))
            {
                outs.Add(p);
                continue;
            }
            var b = Bounds(p);
            var c = Inside(p);
            var ang = (b.right - b.left < b.bottom - b.top ? 0 : Math.PI / 2) + rng.Uniform(-0.35, 0.35);
            const double L = 2000;
            var cut = Line((c.x - L * Math.Cos(ang), c.y - L * Math.Sin(ang)), (c.x + L * Math.Cos(ang), c.y + L * Math.Sin(ang)));
            var parts = Parts(Difference(p, Thicken(cut, Math.Max(0.05, cutWidth), round: false)));
            if (parts.Count < 2)
            {
                outs.Add(p);
                continue;
            }
            todo.AddRange(parts.Where(q => Area(q) > 40));
        }
        return outs;
    }
}

// The prototype's dice, with its calls' names: every map draws from one of these, seeded by the run and the act,
// so a run's map looks the same each time it is drawn, on resume too.
public sealed class Rng(int seed)
{
    private readonly Random _r = new(seed);

    public double Next() => _r.NextDouble();
    public double Uniform(double a, double b) => a + (b - a) * _r.NextDouble();
    public int RandInt(int a, int b) => _r.Next(a, b + 1);   // both ends in, as Python's randint
    public T Choice<T>(IReadOnlyList<T> xs) => xs[_r.Next(xs.Count)];
    public int Bits() => _r.Next();

    public T Choices<T>(IReadOnlyList<T> xs, IReadOnlyList<double> weights)
    {
        var total = weights.Sum();
        var x = _r.NextDouble() * total;
        for (var i = 0; i < xs.Count; i++)
        {
            x -= weights[i];
            if (x <= 0)
                return xs[i];
        }
        return xs[^1];
    }

    public double Gauss(double mu, double sigma)
    {
        var u1 = 1.0 - _r.NextDouble();
        var u2 = _r.NextDouble();
        return mu + sigma * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    public void Shuffle<T>(IList<T> xs)
    {
        for (var i = xs.Count - 1; i > 0; i--)
        {
            var j = _r.Next(i + 1);
            (xs[i], xs[j]) = (xs[j], xs[i]);
        }
    }

    public List<T> Sample<T>(IReadOnlyList<T> xs, int k)
    {
        var copy = xs.ToList();
        Shuffle(copy);
        return copy.Take(Math.Min(k, copy.Count)).ToList();
    }
}
