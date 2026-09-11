using System;
using Godot;

namespace BnbGodot;

// The window the player types into. Built in code like every other screen here so it wears MoonvineTheme
// without a .tscn, and shaped by one rule: A REPORT THAT IS HARD TO SEND DOES NOT GET SENT. So it is one text
// box, one button, and no question it could have answered itself — it never asks for a version, a seed, a
// platform or "steps to reproduce", because it already knows the first three and the save is the fourth.
//
// It is honest about what leaves the machine: the attachments are named, with their real sizes, and the
// screenshot is shown as a thumbnail — a player can SEE the moment they are about to send, which is the only
// way to be sure the picture is of the bug and not of this dialog (see BugReport.Remember).
public partial class BugReportPanel : PanelContainer
{
    private const int ThumbWidth = 200;

    private readonly Action? _onClose;
    private TextEdit _message = null!;
    private Button _send = null!;
    private Button _close = null!;
    private Label _status = null!;
    private BugReport.Payload _payload = null!;
    private bool _sent;

    public BugReportPanel(Action? onClose = null) => _onClose = onClose;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(620, 0);
        AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(MoonvineTheme.BgPanelStrong, MoonvineTheme.Accent));

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        var title = new Label { Text = "Report a bug", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
        column.AddChild(title);

        var ask = new Label
        {
            Text = "What went wrong, and what were you doing when it did?",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        ask.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        column.AddChild(ask);

        _message = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, 110),
            PlaceholderText = "The card said it deals 6 and it dealt 3 …",
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        _message.TextChanged += RefreshEnabled;
        column.AddChild(_message);

        // ── what goes with it ────────────────────────────────────────────────────
        // GATHERED NOW, NOT ON SEND, and that is the point: the state worth reporting is the state at the
        // moment the player reached for the menu, not the one forty seconds later when they finished typing.
        // It also means the sizes shown below are the sizes that actually go.
        _payload = BugReport.Gather("");
        var carried = new HBoxContainer();
        carried.AddThemeConstantOverride("separation", 12);

        if (BugReport.LastScreen is { } screen)
        {
            var thumb = new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(screen),
                CustomMinimumSize = new Vector2(ThumbWidth, ThumbWidth * screen.GetHeight() / (float)screen.GetWidth()),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TooltipText = "The screen as it was when you opened the menu — not as it is now, with this "
                    + "window over it.",
            };
            carried.AddChild(thumb);
        }

        var manifest = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        manifest.AddThemeConstantOverride("separation", 4);
        manifest.AddChild(Muted("Sent with your message:"));
        manifest.AddChild(Muted(Item("your run", _payload.SaveJson?.Length,
            "the save itself, so the bug can be loaded instead of guessed at")));
        manifest.AddChild(Muted(Item("a screenshot", _payload.Screen?.Length, "the screen you just saw")));
        manifest.AddChild(Muted(Item("the log", _payload.Log?.Length, "what the game printed to itself")));
        manifest.AddChild(Muted($"· where you are — {_payload.Headline}"));
        carried.AddChild(manifest);
        column.AddChild(carried);

        // Smaller than the rest on purpose: the outcome line can carry a filesystem path, and the whole window
        // has to stay inside a 720-tall canvas even when the player has set the interface one size larger.
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeFontSizeOverride("font_size", 12);
        _status.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        column.AddChild(_status);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        _send = new Button { Text = "Send report", CustomMinimumSize = new Vector2(0, 40),
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _send.Pressed += () => _ = Submit();
        buttons.AddChild(_send);
        _close = new Button { Text = "Cancel", CustomMinimumSize = new Vector2(140, 40) };
        _close.Pressed += () => _onClose?.Invoke();
        buttons.AddChild(_close);
        column.AddChild(buttons);

        RefreshEnabled();
        _message.GrabFocus();
    }

    // A report with no words is noise: the save and the screenshot say what the game was doing, and only the
    // player can say what it was supposed to do instead.
    private void RefreshEnabled() =>
        _send.Disabled = _sent || string.IsNullOrWhiteSpace(_message.Text);

    // For the probe: type into the box the way a player does, so Send becomes pressable for the same reason.
    public void Fill(string message)
    {
        _message.Text = message;
        RefreshEnabled();
    }

    public async System.Threading.Tasks.Task Submit()
    {
        if (_sent)
            return;
        _sent = true;
        RefreshEnabled();
        _send.Text = "Sending …";

        // ⚠ THE LOCAL COPY IS WRITTEN BEFORE THE NETWORK IS TOUCHED. Whatever the upload does next, the report
        // exists; a player on a train has still reported the bug and can send the folder later.
        var payload = _payload with { Message = _message.Text.Trim() };
        var folder = BugReport.WriteLocally(payload);
        var local = ProjectSettings.GlobalizePath(folder);
        GD.Print($"bug-report: written to {local} ({payload.Headline})");

        var failure = await BugReport.Upload(this, payload);
        if (failure is null)
        {
            _status.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
            _status.Text = "Sent — thank you. A copy was kept on your machine too.";
            GD.Print("bug-report: uploaded");
        }
        else
        {
            // NOT AN ERROR DIALOG. The report is safe on disk; what failed is only the delivery, and the player
            // is told where their report is so that saying so to the developer is still possible.
            _status.AddThemeColorOverride("font_color", MoonvineTheme.Signal);
            _status.Text = $"Saved on your machine, but not uploaded — {failure}.\nThe report is here:\n{local}";
            GD.Print($"bug-report: not uploaded — {failure}");
        }
        _send.Text = "Sent";
        _close.Text = "Close";
    }

    private static string Item(string what, int? bytes, string why) =>
        bytes is null or 0 ? $"· {what} — none to send" : $"· {what} ({Bytes(bytes.Value)}) — {why}";

    private static string Bytes(int bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / (1024f * 1024f):0.0} MB" : $"{Mathf.Max(1, bytes / 1024)} KB";

    private static Label Muted(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        return label;
    }

    // The dialog as a full-screen overlay, the same shape as SettingsPanel.Overlay — a dimmed sheet with the
    // panel centred on it. `onClose` frees it.
    public static Control Overlay(Action onClose)
    {
        var veil = new Control { MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        // Named on purpose: the probe (and Open) finds the panel inside the overlay by this name.
        center.AddChild(new BugReportPanel(onClose) { Name = nameof(BugReportPanel) });
        veil.AddChild(center);
        return veil;
    }

    // Opening it from anywhere: remembers the screen FIRST (so the picture is of the game, not of this window),
    // then hangs the overlay off `screen` under a known name so Esc can find and close it.
    public const string OverlayName = "BugReportOverlay";

    public static BugReportPanel? Open(Godot.Node screen)
    {
        if (screen.GetNodeOrNull(OverlayName) is not null)
            return null;
        var overlay = Overlay(() => screen.GetNodeOrNull(OverlayName)?.QueueFree());
        overlay.Name = OverlayName;
        screen.AddChild(overlay);
        return overlay.FindChild(nameof(BugReportPanel), recursive: true, owned: false) as BugReportPanel;
    }
}
