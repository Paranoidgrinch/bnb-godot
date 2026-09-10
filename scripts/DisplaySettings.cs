using System;
using Godot;

namespace BnbGodot;

// WHAT THE WINDOW IS, AND HOW BIG THE GAME IS DRAWN IN IT. Godot already solves this and there is nothing to
// invent: the project declares ONE design canvas (1280 × 720, `display/window/size/viewport_*`) and a stretch
// mode (`canvas_items`), and from then on every coordinate in the frontend is a design unit that the engine
// scales to whatever the window happens to be. A card is 134 × 190 design units at 1280 × 720 and at
// 3840 × 2160 alike; nothing in the game code multiplies anything by a scale factor.
//
// The aspect is `expand`: on a 16:9 window the canvas is exactly 1280 × 720 scaled up, and on a wider or
// taller one the VIEWPORT grows in design units instead of putting black bars on it — so an ultrawide screen
// gives the combat pane more room rather than pillarboxing the game. That is why the pane's regions are
// anchored (see SessionScreen's region block) instead of measured against 1280.
//
// This file is only the player-facing half: which window mode, which size, and one extra scale factor for
// players who want the whole interface bigger than the window would make it. It is stored in
// `user://settings.cfg` (Godot's own ConfigFile) and applied at boot, before the first screen is built.
public static class DisplaySettings
{
    public enum WindowKind
    {
        Windowed,
        Borderless,   // fullscreen without a mode switch — the safe default for alt-tabbing
        Exclusive,    // a real mode switch; lowest latency, slowest alt-tab
    }

    // The sizes offered in the dropdown. Anything the player already has (a dragged window, a third monitor's
    // odd size) is kept and shown alongside them — the list is an offer, not a fence.
    public static readonly Vector2I[] Sizes =
    [
        new(1280, 720),
        new(1366, 768),
        new(1600, 900),
        new(1920, 1080),
        new(2560, 1440),
        new(3840, 2160),
    ];

    // The scale factors offered for the interface itself, on top of whatever the window size already does.
    public static readonly float[] Scales = [0.8f, 0.9f, 1.0f, 1.1f, 1.25f, 1.5f];

    private const string Path = "user://settings.cfg";
    private const string Section = "display";

    public static WindowKind Mode { get; private set; } = WindowKind.Windowed;
    public static Vector2I Size { get; private set; } = new(1280, 720);
    public static float Scale { get; private set; } = 1.0f;
    public static bool VSync { get; private set; } = true;

    private static bool _loaded;

    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        var file = new ConfigFile();
        if (file.Load(Path) != Error.Ok)
            return;
        Mode = Enum.TryParse((string)file.GetValue(Section, "mode", "Windowed"), out WindowKind kind)
            ? kind
            : WindowKind.Windowed;
        var width = (int)file.GetValue(Section, "width", 1280);
        var height = (int)file.GetValue(Section, "height", 720);
        Size = new Vector2I(Math.Max(640, width), Math.Max(360, height));
        Scale = Math.Clamp((float)file.GetValue(Section, "scale", 1.0f), 0.5f, 2.0f);
        VSync = (bool)file.GetValue(Section, "vsync", true);
    }

    public static void Save()
    {
        var file = new ConfigFile();
        file.SetValue(Section, "mode", Mode.ToString());
        file.SetValue(Section, "width", Size.X);
        file.SetValue(Section, "height", Size.Y);
        file.SetValue(Section, "scale", Scale);
        file.SetValue(Section, "vsync", VSync);
        file.Save(Path);
    }

    // Put the settings on the actual window. ⚠ A PROBE'S WINDOW IS NOT THE PLAYER'S — every screenshot probe
    // in this project measures a 1280 × 720 window and compares against numbers taken from one, so a stored
    // 4K fullscreen would silently invalidate every one of them. Headless has no window at all. Both are left
    // exactly as they are.
    public static void Apply(SceneTree tree, bool skipWindow = false)
    {
        ArgumentNullException.ThrowIfNull(tree);
        Load();

        // The interface scale is not a window property — it multiplies the design canvas, so it is safe (and
        // useful) even under a probe.
        tree.Root.ContentScaleFactor = Scale;

        if (skipWindow || DisplayServer.GetName().Contains("headless"))
            return;

        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        switch (Mode)
        {
            case WindowKind.Exclusive:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                break;
            case WindowKind.Borderless:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
            default:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(Size);
                Center();
                break;
        }
    }

    public static void Set(WindowKind mode, Vector2I size, float scale, bool vsync, SceneTree tree)
    {
        Mode = mode;
        Size = size;
        Scale = Math.Clamp(scale, 0.5f, 2.0f);
        VSync = vsync;
        Save();
        Apply(tree);
    }

    // The window the player is looking at right now, whatever put it there (a drag, a --resolution flag).
    public static Vector2I CurrentWindowSize() => DisplayServer.GetName().Contains("headless")
        ? Size
        : DisplayServer.WindowGetSize();

    private static void Center()
    {
        var screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var window = DisplayServer.WindowGetSize();
        DisplayServer.WindowSetPosition(usable.Position + (usable.Size - window) / 2);
    }
}
