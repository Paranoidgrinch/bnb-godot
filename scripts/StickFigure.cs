using Godot;

namespace BnbGodot;

// A placeholder combatant sprite: a plain stick figure drawn with lines. Deliberately crude — real art
// comes later (via the presentation manifest). `facing` flips it so the hero faces right, the enemy left.
public partial class StickFigure : Control
{
    private readonly Color _color;
    private readonly int _facing; // +1 faces right, -1 faces left
    private readonly bool _dead;

    public StickFigure(Color color, int facing = 1, bool dead = false)
    {
        _color = color;
        _facing = facing;
        _dead = dead;
        CustomMinimumSize = new Vector2(90, 150);
    }

    public override void _Draw()
    {
        var w = Size.X;
        var h = Size.Y;
        var cx = w / 2f;
        var color = _dead ? new Color(_color, 0.35f) : _color;
        const float thickness = 3f;

        var headR = 18f;
        var headCy = h * 0.16f;
        DrawArc(new Vector2(cx, headCy), headR, 0, Mathf.Tau, 24, color, thickness);

        var neck = headCy + headR;
        var hip = h * 0.62f;
        DrawLine(new Vector2(cx, neck), new Vector2(cx, hip), color, thickness); // spine

        var shoulder = neck + 12f;
        // Arms reach toward the opponent (facing side) and slightly down on the other.
        DrawLine(new Vector2(cx, shoulder), new Vector2(cx + 34f * _facing, shoulder + 20f), color, thickness);
        DrawLine(new Vector2(cx, shoulder), new Vector2(cx - 24f * _facing, shoulder + 26f), color, thickness);

        // Legs.
        DrawLine(new Vector2(cx, hip), new Vector2(cx - 22f, h * 0.92f), color, thickness);
        DrawLine(new Vector2(cx, hip), new Vector2(cx + 22f, h * 0.92f), color, thickness);

        if (_dead)
        {
            // Simple X eyes to read as down.
            DrawLine(new Vector2(cx - 8, headCy - 4), new Vector2(cx - 2, headCy + 2), color, 2f);
            DrawLine(new Vector2(cx - 2, headCy - 4), new Vector2(cx - 8, headCy + 2), color, 2f);
            DrawLine(new Vector2(cx + 2, headCy - 4), new Vector2(cx + 8, headCy + 2), color, 2f);
            DrawLine(new Vector2(cx + 8, headCy - 4), new Vector2(cx + 2, headCy + 2), color, 2f);
        }
    }
}
