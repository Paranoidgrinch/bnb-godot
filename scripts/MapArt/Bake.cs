using Godot;

namespace BnbGodot.MapArt;

// The sketch turned into one picture, once: the ink and the paint are each replayed into an offscreen viewport,
// then the ground, the ink (multiplied) and the paint (laid over) are put together by a small shader into a third,
// and that frame is kept as a texture. The map then shows a single picture, however often it is opened; a run's
// pictures are kept here for as long as the game runs, so going back to the map never draws it twice.
public static partial class Bake
{
    private static readonly Dictionary<string, Texture2D> Done = [];
    private static readonly Dictionary<string, Task<Texture2D>> Pending = [];
    private static FontFile? _font;

    public static Texture2D? Cached(string key) => Done.GetValueOrDefault(key);

    public static bool Busy => Pending.Count > 0;

    // `make` works the plan out (on a worker thread); `host` is any node in the tree, to hang the viewports on.
    public static Task<Texture2D> Get(string key, Node host, Func<Plan.Result> make)
    {
        if (Done.TryGetValue(key, out var t))
            return Task.FromResult(t);
        if (Pending.TryGetValue(key, out var p))
            return p;
        var task = Run(key, host, make);
        Pending[key] = task;
        return task;
    }

    private static async Task<Texture2D> Run(string key, Node host, Func<Plan.Result> make)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var plan = await Task.Run(make);
        var planMs = clock.ElapsedMilliseconds;
        var size = new Vector2I(plan.Width * Sketch.Scale, plan.Height * Sketch.Scale);
        var ink = await Replay(host, size, plan.Sketch.Ink, Colors.White);
        var paint = await Replay(host, size, plan.Sketch.Paint, null);
        var final = await Compose(host, size, plan.Ground, plan.GroundColor, ink, paint);
        final.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(final);
        Done[key] = tex;
        Pending.Remove(key);
        if (System.Environment.GetEnvironmentVariable("BNB_MAPART_DUMP") is { } dump)
        {
            final.SavePng(dump.Replace("{key}", key.Replace(':', '-')));
            GD.Print($"map art {key}: plan {planMs} ms, drawn {clock.ElapsedMilliseconds} ms, {plan.Sketch.Ink.Count + plan.Sketch.Paint.Count} strokes");
        }
        return tex;
    }

    private static Font Font => _font ??= GD.Load<FontFile>("res://theme/font/clerk-of-boston.ttf");

    // One sheet into an offscreen viewport, read back as an image.
    private static async Task<Image> Replay(Node host, Vector2I size, List<Sketch.Op> ops, Color? background)
    {
        var vp = new SubViewport
        {
            Size = size,
            TransparentBg = background is null,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Disable3D = true,
        };
        if (background is { } bg)
            vp.AddChild(new ColorRect { Color = bg, Size = size });
        vp.AddChild(new Player(ops));
        host.AddChild(vp);
        await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = vp.GetTexture().GetImage();
        vp.QueueFree();
        return img;
    }

    private static async Task<Image> Compose(Node host, Vector2I size, string? ground, Color groundColor, Image ink, Image paint)
    {
        var vp = new SubViewport { Size = size, RenderTargetUpdateMode = SubViewport.UpdateMode.Once, Disable3D = true };
        Texture2D groundTex = ground is not null ? GD.Load<Texture2D>(ground) : ImageTexture.CreateFromImage(Solid(groundColor));
        var shader = new Shader
        {
            Code = """
                shader_type canvas_item;
                uniform sampler2D ink_tex;
                uniform sampler2D paint_tex;
                void fragment() {
                    vec3 g = texture(TEXTURE, UV).rgb;
                    vec3 k = texture(ink_tex, UV).rgb;
                    vec3 c = g * (vec3(1.0) - (vec3(1.0) - k) * 0.93);
                    vec4 p = texture(paint_tex, UV);
                    COLOR = vec4(c * (1.0 - p.a) + p.rgb, 1.0);
                }
                """,
        };
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("ink_tex", ImageTexture.CreateFromImage(ink));
        mat.SetShaderParameter("paint_tex", ImageTexture.CreateFromImage(paint));
        vp.AddChild(new TextureRect
        {
            Texture = groundTex,
            Size = size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Material = mat,
        });
        host.AddChild(vp);
        await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = vp.GetTexture().GetImage();
        vp.QueueFree();
        return img;
    }

    private static Image Solid(Color c)
    {
        var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgb8);
        img.Fill(c);
        return img;
    }

    // Replays a sheet's strokes in its _Draw.
    private sealed partial class Player(List<Sketch.Op> ops) : Node2D
    {
        private readonly Dictionary<string, Texture2D> _pictures = [];

        public override void _Draw()
        {
            var rid = GetCanvasItem();
            foreach (var op in ops)
            {
                switch (op)
                {
                    case Sketch.Stroke s:
                        DrawPolyline(s.Points, s.Color, s.Width, true);
                        break;
                    case Sketch.Fill f:
                        var idx = new int[f.Triangles.Length];
                        for (var i = 0; i < idx.Length; i++)
                            idx[i] = i;
                        var cols = new Color[f.Triangles.Length];
                        Array.Fill(cols, f.Color);
                        RenderingServer.CanvasItemAddTriangleArray(rid, idx, f.Triangles, cols);
                        break;
                    case Sketch.Word w:
                        var font = Font;
                        var sz = font.GetStringSize(w.Text, HorizontalAlignment.Left, -1, w.Size);
                        var baseline = (font.GetAscent(w.Size) - font.GetDescent(w.Size)) / 2f;
                        DrawSetTransform(w.Centre, w.Angle, Vector2.One);
                        DrawString(font, new Vector2(w.Left ? 0 : -sz.X / 2f, baseline), w.Text, HorizontalAlignment.Left, -1, w.Size, w.Color);
                        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                        break;
                    case Sketch.Pic p:
                        if (!_pictures.TryGetValue(p.Path, out var tex))
                            _pictures[p.Path] = tex = GD.Load<Texture2D>(p.Path);
                        if (p.Angle == 0 && !p.FlipH && p.Tint is null)
                            DrawTextureRect(tex, p.Rect, false);
                        else
                        {
                            // centred on Rect.Position, turned and mirrored about its middle
                            DrawSetTransform(p.Rect.Position, p.Angle, new Vector2(p.FlipH ? -1 : 1, 1));
                            DrawTextureRect(tex, new Rect2(-p.Rect.Size / 2, p.Rect.Size), false, p.Tint ?? Colors.White);
                            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                        }
                        break;
                }
            }
        }
    }
}
