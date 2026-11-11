using Clipper2Lib;
using Godot;

namespace BnbGodot.MapArt;

// What an act's plan draws, written down as a list of strokes, fills, words and pictures, so the geometry can be
// worked out on a worker thread and Godot only has to replay the list once, into a texture (Bake.cs). Two
// sheets, as the prototype has them: INK, drawn on white and multiplied onto the ground (so the paper shows
// through every line), and PAINT, laid over the result (the white of a temple, the green of a wood).
//
// Coordinates are map units; widths and sizes are in the art's own pixels, which are Scale× the map's (the
// prototype's PIL widths), so a `1` is a hairline.
public sealed class Sketch
{
    public const int Scale = 2;

    public abstract record Op;
    public sealed record Stroke(Vector2[] Points, Color Color, float Width) : Op;
    public sealed record Fill(Vector2[] Triangles, Color Color) : Op;
    public sealed record Word(string Text, Vector2 Centre, int Size, Color Color, float Angle, bool Left = false) : Op;
    public sealed record Pic(string Path, Rect2 Rect, float Angle = 0, bool FlipH = false, Color? Tint = null) : Op;

    public readonly List<Op> Ink = [];
    public readonly List<Op> Paint = [];
    private List<Op> _target;

    public Sketch() => _target = Ink;

    public void OnInk() => _target = Ink;
    public void OnPaint() => _target = Paint;

    private static Vector2 V(PointD p) => new((float)(p.x * Scale), (float)(p.y * Scale));
    private static Vector2 V(double x, double y) => new((float)(x * Scale), (float)(y * Scale));

    // ── strokes ──────────────────────────────────────────────────────────────────

    public void Line(PathD l, Color c, float width = 1)
    {
        if (l.Count >= 2)
            _target.Add(new Stroke([.. l.Select(V)], c, width));
    }

    public void Line(Color c, float width, params (double X, double Y)[] pts) => Line(Geo.Line(pts), c, width);

    public void Lines(IEnumerable<PathD> ls, Color c, float width = 1)
    {
        foreach (var l in ls)
            Line(l, c, width);
    }

    // A shape filled (holes left open), by Clipper's triangulation.
    public void FillShape(PathsD s, Color c)
    {
        if (s.Count == 0)
            return;
        foreach (var part in Geo.Parts(s))
        {
            if (Geo.Area(part) < 0.05)
                continue;
            var res = Clipper.Triangulate(part, 3, out var tris, false);
            if (res != TriangulateResult.success)
            {
                // a sliver Clipper will not triangulate: fill its outer ring the plain way
                var outer = part[0];
                if (outer.Count >= 3)
                {
                    var pts = outer.Select(V).ToArray();
                    var idx = Godot.Geometry2D.TriangulatePolygon(pts);
                    if (idx.Length > 0)
                        _target.Add(new Fill([.. idx.Select(i => pts[i])], c));
                }
                continue;
            }
            var flat = new List<Vector2>(tris.Count * 3);
            foreach (var t in tris)
                if (t.Count == 3)
                    flat.AddRange(t.Select(V));
            if (flat.Count > 0)
                _target.Add(new Fill([.. flat], c));
        }
    }

    // A convex outline filled as a fan, straight to triangles (trees, dots, boxes: thousands of them).
    public void FillConvex(IReadOnlyList<PointD> ring, Color c)
    {
        if (ring.Count < 3)
            return;
        var tris = new Vector2[(ring.Count - 2) * 3];
        var o = V(ring[0]);
        for (var i = 1; i < ring.Count - 1; i++)
        {
            tris[(i - 1) * 3] = o;
            tris[(i - 1) * 3 + 1] = V(ring[i]);
            tris[(i - 1) * 3 + 2] = V(ring[i + 1]);
        }
        _target.Add(new Fill(tris, c));
    }

    // The prototype's outline(): the shape made a little hand-drawn, filled, its rings stroked.
    public void Outline(PathsD s, Rng rng, float width = 2, Color? col = null, Color? fill = null, double amp = 0.7)
    {
        var g = amp > 0 ? Geo.Wobble(s, amp, 4, rng) : s;
        if (fill is { } f)
            FillShape(g, f);
        if (width > 0)
            foreach (var r in Geo.Rings(g))
                Line(r, col ?? Plan.Ink, width);
    }

    public void Hatch(PathsD area, double spacing = 2.2, double angle = 45, float width = 1, Color? col = null)
    {
        if (Geo.IsEmpty(area))
            return;
        var b = Geo.Bounds(area);
        var d = Math.Sqrt((b.right - b.left) * (b.right - b.left) + (b.bottom - b.top) * (b.bottom - b.top));
        var cx = (b.left + b.right) / 2;
        var cy = (b.top + b.bottom) / 2;
        var a = angle * Math.PI / 180;
        double ux = Math.Cos(a), uy = Math.Sin(a), vx = -uy, vy = ux;
        var lines = new PathsD();
        for (var t = -d / 2; t < d / 2; t += spacing)
            lines.Add(Geo.Line((cx + vx * t - ux * d, cy + vy * t - uy * d), (cx + vx * t + ux * d, cy + vy * t + uy * d)));
        Lines(Geo.ClipLines(lines, area), col ?? Plan.Soft, width);
    }

    public void Dotted(PathD l, double step, Color c, double r)
    {
        var L = Geo.Length(l);
        for (var t = 0.0; t < L; t += step)
        {
            var q = Geo.At(l, t);
            Disc(q.x, q.y, r, c);
        }
    }

    // ── round things ─────────────────────────────────────────────────────────────

    public void Disc(double x, double y, double r, Color c)
    {
        var n = Math.Clamp((int)(r * Scale * 2.5), 6, 40);
        var pts = new Vector2[n];
        for (var i = 0; i < n; i++)
            pts[i] = V(x + r * Math.Cos(i * 2 * Math.PI / n), y + r * Math.Sin(i * 2 * Math.PI / n));
        var tris = new List<Vector2>();
        var centre = V(x, y);
        for (var i = 0; i < n; i++)
            tris.AddRange([centre, pts[i], pts[(i + 1) % n]]);
        _target.Add(new Fill([.. tris], c));
    }

    public void Ellipse(double x0, double y0, double x1, double y1, Color? fill = null, Color? outline = null, float width = 1)
    {
        var cx = (x0 + x1) / 2;
        var cy = (y0 + y1) / 2;
        var rx = Math.Abs(x1 - x0) / 2;
        var ry = Math.Abs(y1 - y0) / 2;
        var n = Math.Clamp((int)((rx + ry) * Scale * 1.5), 8, 64);
        var ring = new PathD(n);
        for (var i = 0; i < n; i++)
            ring.Add(new PointD(cx + rx * Math.Cos(i * 2 * Math.PI / n), cy + ry * Math.Sin(i * 2 * Math.PI / n)));
        if (fill is { } f)
            FillConvex(ring, f);
        if (outline is { } o)
            Line(new PathD(ring) { ring[0] }, o, width);
    }

    public void Arc(double cx, double cy, double rx, double ry, double fromDeg, double toDeg, Color c, float width = 1)
    {
        var n = Math.Clamp((int)(Math.Abs(toDeg - fromDeg) / 6), 4, 90);
        var l = new PathD(n + 1);
        for (var i = 0; i <= n; i++)
        {
            var a = (fromDeg + (toDeg - fromDeg) * i / n) * Math.PI / 180;
            l.Add(new PointD(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }
        Line(l, c, width);
    }

    public void Rect(double x0, double y0, double x1, double y1, Color? fill = null, Color? outline = null, float width = 1)
    {
        if (fill is { } f)
            FillConvex(Geo.Box(x0, y0, x1, y1)[0], f);
        if (outline is { } o)
            Line(Geo.Line((x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0)), o, width);
    }

    public void Poly(IEnumerable<(double X, double Y)> pts, Color? fill = null, Color? outline = null, float width = 1)
    {
        var p = pts.ToList();
        if (fill is { } f)
            FillShape(Geo.Polygon(p), f);   // may be concave: Clipper triangulates
        if (outline is { } o)
            Line(Geo.Line([.. p, p[0]]), o, width);
    }

    // ── words and pictures ───────────────────────────────────────────────────────

    // A word centred on (x, y), `size` as the prototype's font(size) (map units, drawn at Scale×), turned by `angle` degrees.
    public void Text(double x, double y, string s, double size, Color c, double angle = 0) =>
        _target.Add(new Word(s, V(x, y), (int)Math.Round(size * Scale), c, (float)(angle * Math.PI / 180)));

    // A word starting at (x, y), centred on y.
    public void TextLeft(double x, double y, string s, double size, Color c) =>
        _target.Add(new Word(s, V(x, y), (int)Math.Round(size * Scale), c, 0, true));

    // About how wide a word comes out in the clerk's hand (the geometry is worked out off the main thread, where
    // the font is not to be asked).
    public static double TextWidth(string s, double size) => s.Length * size * 0.42;

    // A picture from res://, its top-left at (x, y), w × h map units.
    public void Picture(string path, double x, double y, double w, double h) =>
        _target.Add(new Pic(path, new Rect2(V(x, y), new Vector2((float)(w * Scale), (float)(h * Scale)))));

    // A picture centred on (cx, cy), turned by `angle` degrees, mirrored if asked, its colours multiplied by `tint`.
    public void PictureAt(string path, double cx, double cy, double w, double h, double angle, bool flip, Color? tint = null) =>
        _target.Add(new Pic(path, new Rect2(V(cx, cy), new Vector2((float)(w * Scale), (float)(h * Scale))),
            (float)(angle * Math.PI / 180), flip, tint));
}
