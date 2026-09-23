using System;
using System.Linq;
using Godot;

namespace BnbGodot;

// SETTINGS ▸ CONTROLS: every key the game answers to, and the way to put each one somewhere else. Reached from
// the settings window on the title screen and from Esc inside a run — it takes the settings panel's place in the
// same dialog and "Back" gives it back, so there is never a second window to close.
//
// Click a key, press the new one. Esc while waiting cancels the change (Esc itself cannot be bound — see
// Controls). A key another action already had is swapped, and the row that lost it shows its new key at once.
public partial class ControlsPanel : PanelContainer
{
    private readonly Action _onBack;
    private string? _listening;
    private VBoxContainer _rows = null!;

    public ControlsPanel(Action onBack) => _onBack = onBack;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(600, 0);
        AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        var title = new Label { Text = "Controls", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(title);
        var hint = new Label
        {
            Text = "Click a key, then press the one you want instead.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        column.AddChild(hint);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 400),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_rows);
        column.AddChild(scroll);
        Fill();

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        var reset = new Button { Text = "Reset to defaults", CustomMinimumSize = new Vector2(0, 40) };
        reset.Pressed += () =>
        {
            _listening = null;
            Controls.ResetAll();
            Fill();
        };
        buttons.AddChild(reset);
        var back = new Button { Text = "Back", CustomMinimumSize = new Vector2(120, 40) };
        back.Pressed += () => _onBack();
        buttons.AddChild(back);
        column.AddChild(buttons);
    }

    private void Fill()
    {
        foreach (var child in _rows.GetChildren())
            child.QueueFree();
        // The ten card keys are one idea, so they are one row of small keys rather than ten rows of big ones.
        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 3);
        foreach (var binding in Controls.All.Where(b => Controls.IsCardKey(b.Action)))
            cards.AddChild(KeyButton(binding, width: 34));
        _rows.AddChild(Row("Pick a card in your hand (1st … 10th)", cards));
        foreach (var binding in Controls.All.Where(b => !Controls.IsCardKey(b.Action)))
            _rows.AddChild(Row(binding.Label, KeyButton(binding, width: 140)));
        foreach (var (name, label) in Controls.Fixed)
        {
            var key = new Label
            {
                Text = name,
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(140, 30),
                TooltipText = "Always on this key.",
                MouseFilter = MouseFilterEnum.Stop,
            };
            key.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
            _rows.AddChild(Row(label, key));
        }
    }

    private Button KeyButton(Controls.Binding binding, int width)
    {
        var action = binding.Action;
        var key = new Button
        {
            Text = _listening == action ? (width < 60 ? "…" : "Press a key…") : Controls.KeyName(action),
            CustomMinimumSize = new Vector2(width, 30),
            TooltipText = $"{binding.Label} · default {Controls.KeyName(binding.Default)}",
        };
        if (_listening == action)
            key.AddThemeColorOverride("font_color", MoonvineTheme.Signal);
        key.Pressed += () =>
        {
            _listening = _listening == action ? null : action;
            Fill();
        };
        return key;
    }

    private static Control Row(string label, Control key)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var name = new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        name.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        row.AddChild(name);
        row.AddChild(key);
        return row;
    }

    // _Input, not _UnhandledInput: the key being bound must reach this panel before the screen underneath
    // treats it as the shortcut it currently is — and Esc must cancel the wait, not close the window.
    public override void _Input(InputEvent @event)
    {
        if (_listening is null || @event is not InputEventKey { Pressed: true, Echo: false } press)
            return;
        GetViewport().SetInputAsHandled();
        if (press.Keycode != Key.Escape && Controls.Bindable(press.Keycode))
            Controls.Rebind(_listening, press.Keycode);
        _listening = null;
        Fill();
    }

    public bool Listening => _listening is not null;
}
