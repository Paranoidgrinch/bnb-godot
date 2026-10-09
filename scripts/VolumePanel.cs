using System;
using Godot;

namespace BnbGodot;

// SETTINGS ▸ VOLUME: master, music and effects (AudioSettings). Like Controls and Gameplay, it takes the settings
// panel's place in the same dialog and "Back" gives it back — reached from the title screen and from Esc alike.
public partial class VolumePanel : PanelContainer
{
    private readonly Action _onBack;

    public VolumePanel(Action onBack) => _onBack = onBack;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(460, 0);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);

        var title = new Label { Text = "Volume settings", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(title);

        column.AddChild(Slider("Master", AudioSettings.Channel.Master,
            "Everything the game plays. 0 turns the game silent."));
        column.AddChild(Slider("Music", AudioSettings.Channel.Music,
            "How loud the music is. 0 turns it off."));
        column.AddChild(Slider("Effects", AudioSettings.Channel.Effects,
            "Blows, block, statuses and falls in a fight. 0 turns them off."));

        var back = new Button
        {
            Text = "Back",
            CustomMinimumSize = new Vector2(120, 40),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter
        };
        back.Pressed += () => _onBack();
        column.AddChild(back);
    }

    private static Control Slider(string label, AudioSettings.Channel channel, string explanation)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var name = new Label { Text = label, CustomMinimumSize = new Vector2(90, 0), TooltipText = explanation };
        name.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        row.AddChild(name);

        var slider = new HSlider
        {
            Name = $"{label}Slider",
            MinValue = 0,
            MaxValue = 100,
            Step = 1,
            Value = AudioSettings.Get(channel),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 24),
            TooltipText = explanation + " Takes effect as you drag it.",
        };
        var value = new Label
        {
            Text = Volume(AudioSettings.Get(channel)),
            CustomMinimumSize = new Vector2(46, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        value.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        // ValueChanged and not drag_ended: the whole point of a volume slider is that you hear the answer while
        // you are still holding it — and for the effects, a blow is played as you let go so there is one.
        slider.ValueChanged += v =>
        {
            AudioSettings.Set(channel, (int)v);
            value.Text = Volume((int)v);
        };
        if (channel == AudioSettings.Channel.Effects)
            slider.DragEnded += _ => Sfx.Hit();
        row.AddChild(slider);
        row.AddChild(value);
        return row;
    }

    // "Off" and not "0 %": zero is the one value on a slider that is a different KIND of answer.
    private static string Volume(int percent) => percent <= 0 ? "Off" : $"{percent} %";
}
