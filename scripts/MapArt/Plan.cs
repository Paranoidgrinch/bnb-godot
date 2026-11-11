using Clipper2Lib;
using Godot;

namespace BnbGodot.MapArt;

// An act's map drawn as a plan in the manner of its historical model, built from the run's own map: the paths
// and rooms are not laid over the plan, they ARE its streets and squares, corridors and chambers, forest roads and
// clearings, the processional way and its chapels. Around them each plan generates its country (fields and a
// river, stacks and cells, parcels of forest, sphinxes), and a few pieces of the model's own furniture finish the
// sheet (a title, a compass, a scale). Act V lays its three bosses on clay fragments instead.
//
// Ported from the prototype the user signed off (bnb-mapwork/mapgen/plans.py, 2026-11-11), which drew act I with
// seed 1, II with 3, III with 3 and IV with 9. The run's seed takes the place of those: every run its own sheet.
public abstract class Plan
{
    public const int Side = 170;   // the country either side of the map's own column
    public const int Top = 96;     // the title band
    public const int Foot = 70;    // the scale and the frieze

    public static readonly Color Ink = C(52, 36, 24);
    public static readonly Color Soft = C(52, 36, 24, 140);

    public static Color C(int r, int g, int b, int a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    public sealed record Room(string Id, string Role, double X, double Y);
    public sealed record Input(int Act, int Seed, int MapW, int MapH, double NodeD, List<Room> Rooms, List<(string From, string To)> Edges);
    public sealed record Result(Sketch Sketch, int Width, int Height, string? Ground, Color GroundColor);

    protected readonly Input In;
    protected readonly Rng Rng;
    protected readonly Sketch Sk = new();
    protected readonly int W, H;
    protected readonly double R;
    protected readonly Dictionary<string, (double X, double Y)> Centre = [];
    protected readonly Dictionary<string, string> Role = [];
    protected readonly PathsD Frame;

    protected Plan(Input input)
    {
        In = input;
        Rng = new Rng(unchecked(input.Seed * 7919 + input.Act * 104729));
        R = input.NodeD / 2;
        W = input.MapW + 2 * Side;
        H = input.MapH + Top + Foot;
        foreach (var r in input.Rooms)
        {
            Centre[r.Id] = (r.X + R + Side, r.Y + R + Top);
            Role[r.Id] = r.Role;
        }
        Frame = Geo.Box(16, 16, W - 16, H - 16);
    }

    public static Result Make(Input input)
    {
        Plan p = input.Act switch
        {
            1 => new TownPlan(input),
            2 => new VaultPlan(input),
            3 => new ForestPlan(input),
            4 => new PapyrusPlan(input),
            _ => new ShardPlan(input),
        };
        p.Draw();
        var v = new Rng(unchecked(input.Seed * 31 + input.Act)).RandInt(1, 5);
        return new Result(p.Sk, p.W, p.H, p.GroundPath(v), C(10, 6, 6));
    }

    protected abstract void Draw();

    protected virtual string? GroundPath(int variant) => $"res://assets/map/grounds/ground_{In.Act}_{variant}.jpg";

    // ── shared pieces ────────────────────────────────────────────────────────────

    // The map's edges as slightly bent centre lines (more points and some wander for a road or a wadi).
    protected List<PathD> PathLines(double bend = 3, double wander = 0)
    {
        var outs = new List<PathD>();
        foreach (var (a, b) in In.Edges)
        {
            if (!Centre.TryGetValue(a, out var p1) || !Centre.TryGetValue(b, out var p2))
                continue;
            var nx = -(p2.Y - p1.Y);
            var ny = p2.X - p1.X;
            var ln = Math.Sqrt(nx * nx + ny * ny);
            if (ln < 1e-9)
                ln = 1;
            var k = wander > 0 ? 5 : 2;
            var l = new PathD { new PointD(p1.X, p1.Y) };
            for (var i = 1; i < k; i++)
            {
                var t = (double)i / k;
                var off = Rng.Uniform(-bend, bend) + wander * Math.Sin(t * Math.PI) * Rng.Uniform(-1, 1);
                l.Add(new PointD(p1.X + (p2.X - p1.X) * t + nx / ln * off, p1.Y + (p2.Y - p1.Y) * t + ny / ln * off));
            }
            l.Add(new PointD(p2.X, p2.Y));
            outs.Add(l);
        }
        return outs;
    }

    protected void Flourish(double x0, double x1, double y, double amp = 4)
    {
        for (var k = 0; k < 2; k++)
        {
            var l = new PathD();
            for (var t = 0; t <= 60; t++)
                l.Add(new PointD(x0 + (x1 - x0) * t / 60.0,
                    y + k * 3 + amp * Math.Sin(t / 60.0 * Math.PI * 3 + k) * Math.Sin(t / 60.0 * Math.PI)));
            Sk.Line(l, Ink, k == 0 ? 2 : 1);
        }
    }

    // The act's name and nothing else (user, 2026-11-11), a flourish under it.
    protected void Title(string name, double size = 24)
    {
        Sk.Text(W / 2.0, 40, name, size, Ink);
        Flourish(W / 2.0 - 130, W / 2.0 + 130, 66);
    }

    protected void Compass(double cx, double cy, double r = 26)
    {
        Sk.Ellipse(cx - r, cy - r, cx + r, cy + r, outline: Ink, width: 2);
        Sk.Ellipse(cx - r + 4, cy - r + 4, cx + r - 4, cy + r - 4, outline: Soft);
        for (var i = 0; i < 16; i++)
        {
            var a = i * Math.PI / 8;
            Sk.Line(Ink, 1, (cx + (r - 4) * Math.Cos(a), cy + (r - 4) * Math.Sin(a)), (cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
        }
        for (var i = 0; i < 8; i++)
        {
            var a = -Math.PI / 2 + i * Math.PI / 4;
            var L = i % 2 == 0 ? r - 2 : (r - 2) * 0.55;
            var w = i % 2 == 0 ? 4.5 : 3;
            var tip = (cx + L * Math.Cos(a), cy + L * Math.Sin(a));
            var l = (cx + w * Math.Cos(a - Math.PI / 2), cy + w * Math.Sin(a - Math.PI / 2));
            var rr = (cx + w * Math.Cos(a + Math.PI / 2), cy + w * Math.Sin(a + Math.PI / 2));
            Sk.Poly([(cx, cy), tip, l], fill: Ink);
            Sk.Poly([(cx, cy), tip, rr], outline: Ink);
        }
        Sk.Text(cx, cy - r - 9, "N", 10, Ink);
    }

    protected void ScaleBar(double x, double y, double length, string caption, int units = 6)
    {
        var seg = length / units;
        for (var i = 0; i < units; i++)
        {
            var x0 = x + i * seg;
            Sk.Rect(x0, y, x0 + seg, y + 4, fill: i % 2 == 0 ? Ink : null, outline: Ink);
            Sk.Text(x0, y + 11, (i * 10).ToString(), 6, Ink);
        }
        Sk.Text(x + length, y + 11, (units * 10).ToString(), 6, Ink);
        Sk.Text(x + length / 2, y - 9, caption, 10, Ink);
    }

    protected void TextAlong(PathD l, string s, double size, Color c)
    {
        var a = l[0];
        var b = l[^1];
        var ang = Math.Atan2(b.y - a.y, b.x - a.x) * 180 / Math.PI;
        if (ang > 90) ang -= 180;
        if (ang < -90) ang += 180;
        var m = Geo.At(l, Geo.Length(l) / 2);
        Sk.Text(m.x, m.y, s, size, c, ang);
    }

    // A tree as the old plans dot them: a ring and a dot of shade on its lower right.
    protected void TreeDot(double x, double y, double r)
    {
        Sk.Ellipse(x - r, y - r, x + r, y + r, outline: Ink);
        Sk.Ellipse(x + r * 0.2, y + r * 0.2, x + r * 0.7, y + r * 0.7, fill: Ink);
    }

    // A post mill standing on the land: a hatched mound, the trestle, the boarded body under a gabled roof with
    // its door and ladder, the tail pole, four latticed sails (user asked for more detail, 2026-11-11).
    protected void PostMill(double x, double y, double sailAngle)
    {
        void L(float w, params (double X, double Y)[] p) => Sk.Line(Geo.Line([.. p.Select(q => (x + q.X, y + q.Y))]), Ink, w);
        var mound = Geo.Intersect(Geo.Circle(x, y + 2, 10), Geo.Box(x - 11, y - 2, x + 11, y + 3));
        Sk.Hatch(mound, 1.6, 0, 1, Soft);
        L(1, (-11, 2), (11, 2));
        L(1, (-6, 2), (0, -6), (6, 2));
        L(2, (0, 2), (0, -8));
        double bx0 = -5, bx1 = 5, by0 = -22, by1 = -8;
        Sk.Hatch(Geo.Box(x + 1.5, y + by0, x + bx1, y + by1), 1.2, 90, 1, Ink);
        for (var yy = by0 + 2.5; yy < by1; yy += 2.5)
            L(1, (bx0, yy), (1.5, yy));
        Sk.Rect(x + bx0, y + by0, x + bx1, y + by1, outline: Ink, width: 2);
        Sk.Poly([(x + bx0 - 1, y + by0), (x, y + by0 - 6), (x + bx1 + 1, y + by0)], fill: Ink, outline: Ink);
        Sk.Rect(x - 1.5, y + by1 - 4.5, x + 0.5, y + by1, fill: Ink);
        L(1, (-2, -8), (-8, 3));
        L(1, (0, -8), (-6, 3));
        foreach (var t in new[] { 0.3, 0.55, 0.8 })
            L(1, (-2 - 6 * t, -8 + 11 * t), (-6 * t, -8 + 11 * t));
        L(1, (5, -10), (13, 2));
        double hx = x, hy = y + by0 + 4;
        for (var k = 0; k < 4; k++)
        {
            var a = (sailAngle + 45 + k * 90) * Math.PI / 180;
            double ux = Math.Cos(a), uy = Math.Sin(a), vx = -uy, vy = ux;
            double r0 = 2.5, r1 = 15, w = 3.4;
            Sk.Line(Ink, 2, (hx + ux, hy + uy), (hx + ux * r1, hy + uy * r1));
            Sk.Poly([(hx + ux * r0 + vx * 0.5, hy + uy * r0 + vy * 0.5), (hx + ux * r1 + vx * 0.5, hy + uy * r1 + vy * 0.5),
                     (hx + ux * r1 + vx * w, hy + uy * r1 + vy * w), (hx + ux * r0 + vx * w, hy + uy * r0 + vy * w)], outline: Ink);
            for (var i = 0; i < 6; i++)
            {
                var t = r0 + (r1 - r0) * i / 5;
                Sk.Line(Ink, 1, (hx + ux * t + vx * 0.5, hy + uy * t + vy * 0.5), (hx + ux * t + vx * w, hy + uy * t + vy * w));
            }
            Sk.Line(Ink, 1, (hx + ux * r0 + vx * w * 0.55, hy + uy * r0 + vy * w * 0.55), (hx + ux * r1 + vx * w * 0.55, hy + uy * r1 + vy * w * 0.55));
        }
        Sk.Disc(hx, hy, 1.6, Ink);
    }

    protected (double X, double Y) Boss => Centre.Values.MaxBy(c => c.Y);
}
