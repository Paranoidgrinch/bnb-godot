using Godot;

namespace BnbGodot;

// Card look: the shared card shape (portrait, ~5:7 like the tatters-of-numina art) and the animated card
// back. The back plays the Ogg-Theora clip via VideoStreamPlayer (Godot decodes it on the CPU, so only a
// FEW should animate at once — the deck's top card); everywhere else uses the still poster.
public static class CardVisuals
{
    public const int CardW = 112;
    public const int CardH = 156; // 112 * 7/5 ≈ 157

    private static VideoStream? _stream;
    private static Texture2D? _poster;

    private static VideoStream Stream => _stream ??= GD.Load<VideoStream>("res://assets/cards/card-back.ogv");
    private static Texture2D Poster => _poster ??= GD.Load<Texture2D>("res://assets/cards/card-back.png");

    // A card-shaped back. animated=true plays the looping clip (use sparingly); otherwise the still poster.
    public static Control Back(bool animated, float phase = 0f)
    {
        if (animated)
        {
            var video = new VideoStreamPlayer
            {
                Stream = Stream,
                Autoplay = true,
                Loop = true,
                Expand = true,
                CustomMinimumSize = new Vector2(CardW, CardH),
            };
            video.Play();
            return Framed(video);
        }
        var still = new TextureRect
        {
            Texture = Poster,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            CustomMinimumSize = new Vector2(CardW, CardH),
        };
        return Framed(still);
    }

    // Wrap card content in a clipped, framed panel of the card size.
    private static Control Framed(Control content)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(CardW, CardH), ClipContents = true };
        panel.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(new Color("050505"), new Color(MoonvineTheme.Accent, 0.4f), 6));
        content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddChild(content);
        return panel;
    }
}
