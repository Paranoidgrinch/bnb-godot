using System.IO;
using Godot;
using RogueDeck.Sandbox.Composition;

namespace BnbGodot;

// THE DIAGNOSTIC RUN LOG (user, 2026-11-19): "eine art run log, das den combat logs uebergeordnet ist und uns die
// diagnosemoeglichkeit gibt zu ueberpruefen ob effekte auf events, relikten, shops etc das bewirken was sie bewirken
// sollen". The engine's RunDiagnostics writes it; this class only gives it a file.
//
// One plain-text file per run in `user://run-logs/` (on Windows %APPDATA%\Godot\app_userdata\<game>\run-logs), named
// <start time>-<character>-<seed>.log. A resumed run appends to its own file. Every line is flushed at once, so a
// crash leaves the log up to the crash. The newest 60 are kept.
//
// Written for every run the game plays, the bot sims included (their file name says "sim"), and not for the
// tutorial or the trials, whose rooms are not a run's.
public static class RunDiagnosticsLog
{
    private const string Folder = "user://run-logs";
    private const int Kept = 60;

    private static RunDiagnostics? _diagnostics;
    private static StreamWriter? _file;

    public static string? CurrentPath { get; private set; }

    public static void Begin(RunPlayback play, int seed, string? character, bool probe) =>
        Open(play, seed, character, probe, resumed: false);

    public static void Resume(RunPlayback play, int seed, string? character) =>
        Open(play, seed, character, probe: false, resumed: true);

    public static void Stop()
    {
        _diagnostics?.Detach();
        _diagnostics = null;
        _file?.Dispose();
        _file = null;
    }

    private static void Open(RunPlayback play, int seed, string? character, bool probe, bool resumed)
    {
        Stop();
        try
        {
            var folder = ProjectSettings.GlobalizePath(Folder);
            Directory.CreateDirectory(folder);
            var who = string.IsNullOrEmpty(character) ? "run" : character;
            var tail = $"-{(probe ? "sim-" : "")}{who}-{seed}.log";
            var path = resumed
                ? Directory.GetFiles(folder, "*" + tail).Order(StringComparer.Ordinal).LastOrDefault()
                : null;
            path ??= Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + tail);
            _file = new StreamWriter(path, append: true) { AutoFlush = true };
            CurrentPath = path;
            var version = ProjectSettings.GetSetting("application/config/version").AsString();
            var title = $"{(resumed ? "resumed" : "new run")} · {who} · seed {seed} · bnb-godot {version}"
                + $" · {DateTime.Now:yyyy-MM-dd HH:mm}";
            _diagnostics = new RunDiagnostics(line => _file?.WriteLine(line), SessionScreen.Say);
            _diagnostics.Attach(play, title);
            if (!resumed)
                Prune(folder);
        }
        catch (Exception ex)
        {
            // A diagnosis is never worth a run.
            GD.PushWarning($"run diagnostics: not written ({ex.Message})");
            Stop();
        }
    }

    private static void Prune(string folder)
    {
        foreach (var old in Directory.GetFiles(folder, "*.log").Order(StringComparer.Ordinal).SkipLast(Kept))
            File.Delete(old);
    }
}
