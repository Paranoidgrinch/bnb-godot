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
    private readonly Action? _onReportBug;
    private readonly Action? _onSaveAndQuit;
    private readonly Action? _onArchive;
    private readonly Action? _onCompendium;
    private OptionButton _mode = null!;
    private OptionButton _size = null!;
    private OptionButton _scale = null!;
    private CheckBox _vsync = null!;
    private HSlider _music = null!;
    private Label _musicValue = null!;

    public SettingsPanel(
        Action? onClose = null, Action? onReportBug = null, Action? onSaveAndQuit = null,
        Action? onArchive = null, Action? onCompendium = null)
    {
        _onClose = onClose;
        _onReportBug = onReportBug;
        _onSaveAndQuit = onSaveAndQuit;
        _onArchive = onArchive;
        _onCompendium = onCompendium;
    }

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

        // ⚠ ONE SLIDER, SHOWN TWICE. This panel is the title screen's Settings AND the Esc menu, so there is
        // nothing here to keep in step with anything: both are this control, reading and writing the one
        // stored setting. A player who finds the music too loud finds that out four rooms into a run, which
        // is exactly why it may not live on the title screen alone.
        AudioSettings.Load();
        _music = new HSlider
        {
            MinValue = 0,
            MaxValue = 100,
            Step = 1,
            Value = AudioSettings.MusicVolume,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 24),
            TooltipText = "How loud the music is. 0 turns it off. Takes effect as you drag it.",
        };
        _musicValue = new Label
        {
            Text = Volume(AudioSettings.MusicVolume),
            CustomMinimumSize = new Vector2(46, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _musicValue.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        // ValueChanged and not drag_ended: the whole point of a volume slider is that you hear the answer
        // while you are still holding it.
        _music.ValueChanged += value =>
        {
            AudioSettings.SetMusicVolume((int)value);
            _musicValue.Text = Volume((int)value);
        };
        var music = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        music.AddThemeConstantOverride("separation", 10);
        music.AddChild(_music);
        music.AddChild(_musicValue);
        column.AddChild(Row("Music volume", music));

        // THE KEYS take this panel's place in the same dialog rather than opening a second window on top of it:
        // Esc then closes one thing, and "Back" is the only way between the two.
        var controls = new Button
        {
            Text = "🎮  Controls",
            CustomMinimumSize = new Vector2(0, 40),
            TooltipText = "Every shortcut, and the key each one is on.",
        };
        controls.Pressed += ShowControls;

        // THE HELPS, switchable, on a page of their own beside the keys — the same shape and the same way back.
        var gameplay = new Button
        {
            Text = "🎲  Gameplay",
            CustomMinimumSize = new Vector2(0, 40),
            TooltipText = "The end-turn question, the damage calculator and the key names over your cards.",
        };
        gameplay.Pressed += () => ShowPage(new GameplayPanel(Restore));
        var pages = new HBoxContainer();
        pages.AddThemeConstantOverride("separation", 10);
        controls.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        gameplay.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pages.AddChild(controls);
        pages.AddChild(gameplay);
        column.AddChild(pages);

        // THE BUG BUTTON LIVES WHERE THE PLAYER ALREADY IS. This panel *is* the Esc menu, and Esc is what
        // somebody presses the moment the game does something wrong — so the report is one keystroke and one
        // click away from the wrongness, with the screen already captured (BugReport.Remember) from before this
        // window covered it.
        if (_onReportBug is { } report)
        {
            var bug = new Button
            {
                Text = "🐞  Report a bug",
                CustomMinimumSize = new Vector2(0, 40),
                TooltipText = "Send what went wrong, with your save and a picture of the screen.",
            };
            bug.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
            bug.Pressed += () => report();
            column.AddChild(bug);
        }

        // THE ARCHIVE, FROM INSIDE THE RUN THAT IS FILLING IT. The question it answers — how much HP did that
        // thing have, what did that relic do — is asked DURING a fight far more often than on the title
        // screen, and until now the only way to look was to stop playing. Like the way out below, it belongs
        // to the run rather than to the window, so the caller supplies it and the title screen (which has its
        // own Archive button two feet away) passes nothing.
        // THE COMPENDIUM: what every effect and status means, in plain words with an example. A rule is needed
        // mid-fight far more often than on the title screen, so it is here as well as there.
        if (_onCompendium is { } compendium)
        {
            var read = new Button
            {
                Text = "📜  Compendium",
                CustomMinimumSize = new Vector2(0, 40),
                TooltipText = "Every effect and status in the game, in plain words with an example.",
            };
            read.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
            read.Pressed += () => compendium();
            column.AddChild(read);
        }

        if (_onArchive is { } archive)
        {
            var open = new Button
            {
                Text = "📖  Archive",
                CustomMinimumSize = new Vector2(0, 40),
                TooltipText = "Everything you have met, kept between runs.",
            };
            open.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
            open.Pressed += () => archive();
            column.AddChild(open);
        }

        // …AND SO DOES THE WAY OUT. Esc is also what somebody presses when they have to stop playing, and
        // until now this window could only be closed: leaving a run meant quitting the program and trusting
        // that the autosave had caught the last thing they did. Saying it out loud — save, then put me back on
        // the title screen — is one button, and it is the only one here that is about the RUN rather than about
        // the window, which is why the caller supplies it and the title screen passes nothing.
        if (_onSaveAndQuit is { } leave)
        {
            var quit = new Button
            {
                Text = "Save and quit to title",
                CustomMinimumSize = new Vector2(0, 40),
                TooltipText = "Saves this run and returns to the title screen. Continue run picks it up again.",
            };
            quit.Pressed += () => leave();
            column.AddChild(quit);
        }

        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(0, 40) };
        close.Pressed += () => _onClose?.Invoke();
        column.AddChild(close);

        RefreshEnabled();
    }

    private void ShowControls() => ShowPage(new ControlsPanel(Restore));

    // A page takes this panel's place in the same dialog; its Back frees it and shows this panel again.
    private Control? _page;

    private void ShowPage(Control page)
    {
        _page = page;
        Visible = false;
        GetParent().AddChild(page);
    }

    private void Restore()
    {
        _page?.QueueFree();
        _page = null;
        Visible = true;
    }

    // "Off" and not "0 %": zero is the one value on this slider that is a different KIND of answer.
    private static string Volume(int percent) => percent <= 0 ? "Off" : $"{percent} %";

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
    public static Control Overlay(
        Action onClose, Action? onReportBug = null, Action? onSaveAndQuit = null, Action? onArchive = null,
        Action? onCompendium = null)
    {
        var veil = new Control { MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        center.AddChild(new SettingsPanel(onClose, onReportBug, onSaveAndQuit, onArchive, onCompendium));
        veil.AddChild(center);
        return veil;
    }
}
