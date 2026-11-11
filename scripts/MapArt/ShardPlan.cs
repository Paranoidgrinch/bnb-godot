using Godot;

namespace BnbGodot.MapArt;

// Act V, The Divine Ledger: no sheet. Its three bosses each lie on a clay fragment drawn from a pool of museum
// photos (The Metropolitan Museum of Art, CC0; assets/map/grounds/make_grounds.py cuts them free), on the dark
// table, joined by straight lines from one to the next (user, 2026-11-11).
public sealed class ShardPlan(Plan.Input input) : Plan(input)
{
    // the pool, with each fragment's size as cut (make_grounds.py: at most 600 px on the long side)
    private static readonly (string Id, int W, int H)[] Pool =
    [
        ("01", 600, 337), ("04", 600, 443), ("05", 600, 297), ("06", 600, 366), ("07", 528, 600), ("08", 600, 430),
        ("09", 555, 600), ("10", 600, 442), ("12", 600, 584), ("13", 600, 482), ("14", 600, 527), ("16", 600, 423),
        ("18", 348, 600), ("19", 600, 499), ("20", 449, 600), ("26", 600, 343), ("28", 600, 441),
    ];
    private static readonly Color Rope = C(196, 168, 120);

    protected override string? GroundPath(int variant) => null;   // the bare table

    protected override void Draw()
    {
        var rooms = Centre.Values.OrderBy(c => c.Y).ToList();
        Sk.OnPaint();   // on the black table ink would vanish: the lines are laid on, under the fragments
        for (var i = 1; i < rooms.Count; i++)
            Sk.Line(Rope, 5, rooms[i - 1], rooms[i]);
        var tints = new[] { C(240, 214, 190), C(250, 226, 200), C(232, 204, 182) };
        var tint = Rng.Choice(tints);
        var picks = Rng.Sample(Pool, rooms.Count);
        for (var i = 0; i < rooms.Count && i < picks.Count; i++)
        {
            var (id, pw, ph) = picks[i];
            var size = Rng.Uniform(220, 270);
            var f = size / Math.Max(pw, ph);
            var (x, y) = rooms[i];
            var angle = Rng.Uniform(-30, 30);
            var flip = Rng.Next() < 0.5;
            var path = $"res://assets/map/grounds/shard_{id}.png";
            Sk.PictureAt(path, x + 6, y + 8, pw * f, ph * f, angle, flip, new Color(0, 0, 0, 0.55f));   // its shadow
            Sk.PictureAt(path, x, y, pw * f, ph * f, angle, flip, tint);
        }
    }
}
