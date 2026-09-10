using System;
using Godot;

namespace BnbGodot;

// The settings dialog, built in code like every other screen here so it wears MoonvineTheme without a .tscn.
// It is the same object on the title screen and inside a run (Esc) — a setting is a setting, and a player who
// finds the window too small finds that out during a fight, not on the menu.
//
// Everything it offers is applied the moment it is chosen and written to `user://settings.cfg` at once; there
// is no Apply button and nothing to confirm, because every one of these is reversible by looking at it.
public partial class SettingsPanel : PanelContainer
{
    private readonly Action? _onClose;
    private OptionButton _mode = null!;
    private OptionButton _size = null!;
    private OptionButton _scale = null!;
    private CheckBox _vsync = null!;

    public SettingsPanel(Action? onClose = null) => _onClose = onClose;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(460, 0);
        AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(MoonvineTheme.BgPanelStrong, MoonvineTheme.Accent));

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);

        var title = new Label { Text = "Settings", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        column.AddChild(title);

        _mode = new OptionButton { TooltipText = "Windowed, or fullscreen with or without a display mode switch." };
        _mode.AddItem("Windowed", (int)DisplaySettings.WindowKind.Windowed);
        _mode.AddItem("Fullscreen (borderless)", (int)DisplaySettings.WindowKind.Borderless);
        _mode.AddItem("Fullscreen (exclusive)", (int)DisplaySettings.WindowKind.Exclusive);
        _mode.Selected = (int)DisplaySettings.Mode;
        _mode.ItemSelected += _ => Commit();
        column.AddChild(Row("Window", _mode));

        // The offered sizes, plus whatever the window happens to be right now if that is not one of them —
        // a dragged window is a real choice and the dropdown may not silently forget it.
        _size = new OptionButton { TooltipText = "The window's size in pixels. The game is drawn on a 1280 × 720 canvas and scaled to fit it." };
        var current = DisplaySettings.CurrentWindowSize();
        var known = false;
        for (var i = 0; i < DisplaySettings.Sizes.Length; i++)
        {
            var size = DisplaySettings.Sizes[i];
            _size.AddItem($"{size.X} × {size.Y}", i);
            if (size == DisplaySettings.Size)
            {
                _size.Selected = i;
                known = true;
            }
        }
        if (!known)
        {
            _size.AddItem($"{current.X} × {current.Y} (current)", DisplaySettings.Sizes.Length);
            _size.Selected = DisplaySettings.Sizes.Length;
        }
        _size.ItemSelected += _ => Commit();
        column.AddChild(Row("Resolution", _size));

        // ⚠ THE INTERFACE SCALE IS NOT THE WINDOW SIZE. A bigger window shows the same canvas larger; this
        // multiplies the canvas itself, which makes text and cards bigger and the screen hold LESS of them.
        // Both are wanted for different reasons and neither replaces the other.
        _scale = new OptionButton { TooltipText = "Makes the whole interface larger or smaller inside the window." };
        for (var i = 0; i < DisplaySettings.Scales.Length; i++)
        {
            _scale.AddItem($"{DisplaySettings.Scales[i] * 100:0}%", i);
            if (Math.Abs(DisplaySettings.Scales[i] - DisplaySettings.Scale) < 0.001f)
                _scale.Selected = i;
        }
        _scale.ItemSelected += _ => Commit();
        column.AddChild(Row("Interface size", _scale));

        _vsync = new CheckBox { Text = "V-Sync", ButtonPressed = DisplaySettings.VSync };
        _vsync.Toggled += _ => Commit();
        column.AddChild(Row("Frames", _vsync));

        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(0, 40) };
        close.Pressed += () => _onClose?.Invoke();
        column.AddChild(close);

        RefreshEnabled();
    }

    private static Control Row(string label, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var name = new Label { Text = label, CustomMinimumSize = new Vector2(140, 0) };
        name.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        row.AddChild(name);
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(control);
        return row;
    }

    private void Commit()
    {
        var mode = (DisplaySettings.WindowKind)_mode.GetSelectedId();
        var sizeIndex = _size.GetSelectedId();
        var size = sizeIndex >= 0 && sizeIndex < DisplaySettings.Sizes.Length
            ? DisplaySettings.Sizes[sizeIndex]
            : DisplaySettings.CurrentWindowSize();
        var scaleIndex = _scale.GetSelectedId();
        var scale = scaleIndex >= 0 && scaleIndex < DisplaySettings.Scales.Length
            ? DisplaySettings.Scales[scaleIndex]
            : 1.0f;
        DisplaySettings.Set(mode, size, scale, _vsync.ButtonPressed, GetTree());
        RefreshEnabled();
    }

    // A size means nothing while the window is fullscreen — the screen decides. Saying so with a greyed
    // dropdown is the difference between "this control does nothing here" and "this control is broken".
    private void RefreshEnabled() =>
        _size.Disabled = (DisplaySettings.WindowKind)_mode.GetSelectedId() != DisplaySettings.WindowKind.Windowed;

    // The dialog as a full-screen overlay: a dimmed sheet with the panel centred on it. `onClose` frees it.
    public static Control Overlay(Action onClose)
    {
        var veil = new Control { MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        center.AddChild(new SettingsPanel(onClose));
        veil.AddChild(center);
        return veil;
    }
}
