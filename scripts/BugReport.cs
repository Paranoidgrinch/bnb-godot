using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace BnbGodot;

// WHAT A PLAYER KNOWS THAT NO PROBE DOES: that what they are looking at is wrong. Every other check in this
// frontend is a measurement — a count of slots, a width against a viewport, a marathon that reaches act five —
// and not one of them can notice that an intent read as nonsense or that a card did the opposite of its words.
// So this is the one diagnostic that the person holding the mouse writes, and the only thing it has to do
// perfectly is SURVIVE THE SENDING: a report that fails to upload is still a report, and it is written to disk
// before the network is ever touched.
//
// What goes with the message, and why each one:
//   · the save  — the run itself, 20 KB of it, so the bug can be RESUMED rather than guessed at
//   · a screen  — captured BEFORE the dialog opens (see Remember), because a bug that is visible is answered
//                 by the picture alone, and that is the kind this visual overhaul produces
//   · diagnostics — the seed, the room, the version: which of a hundred generated maps this was
//   · the log tail — Godot's own `user://logs/godot.log`, where an escaped exception already printed itself
//
// The transport is a Discord webhook (one multipart POST, the files as attachments). Its URL is a credential of
// sorts and this repository is public, so it is NEVER committed: see Webhook() for the four places it is looked
// for. With no URL configured the feature still works and simply stops at the local copy, which is the correct
// behaviour for a dev checkout as well as for a player with no network.
public static class BugReport
{
    public const int MaxScreenEdge = 1600;     // a 4K window must not post an 8 MB screenshot
    public const int KeepFolders = 10;         // local copies kept; a screenshot is 300 KB and these add up
    private const int LogTail = 16000;         // characters of godot.log, taken from the END
    private const int DiscordContentLimit = 2000;

    public const string Folder = "user://bug-reports";
    private const string WebhookFile = "bugreport.cfg";
    private const string WebhookEnv = "BNB_BUGREPORT_WEBHOOK";

    // Everything the report is made of, gathered at the moment Send is pressed and then immutable — so the
    // local copy and the upload are provably the same report rather than two gatherings of a moving game.
    public sealed record Payload(
        string Message,
        string Headline,
        string Diagnostics,
        string? SaveJson,
        byte[]? Screen,
        string? Log)
    {
        public string Report =>
            $"{Headline}\n\n=== what the player wrote ===\n{Message}\n\n=== diagnostics ===\n{Diagnostics}\n";
    }

    // ── the picture ───────────────────────────────────────────────────────────────

    // ⚠ THE SCREENSHOT IS NOT TAKEN WHEN THE REPORT IS SENT. By then the dialog the player is typing into
    // covers the thing they are reporting, and on the Esc path the settings window covers it too. So the frame
    // is grabbed at the moment the player reaches for the menu — one step BEFORE any overlay exists — and the
    // dialog shows them a thumbnail of it so they can see which moment they caught.
    public static Image? LastScreen { get; private set; }

    public static void Remember(Viewport? viewport)
    {
        if (viewport is null || DisplayServer.GetName().Contains("headless"))
            return;
        var image = viewport.GetTexture()?.GetImage();
        if (image is null || image.IsEmpty())
            return;
        var longest = Mathf.Max(image.GetWidth(), image.GetHeight());
        if (longest > MaxScreenEdge)
        {
            var factor = (float)MaxScreenEdge / longest;
            image.Resize(Mathf.RoundToInt(image.GetWidth() * factor), Mathf.RoundToInt(image.GetHeight() * factor),
                Image.Interpolation.Bilinear);
        }
        LastScreen = image;
    }

    // ── gathering ─────────────────────────────────────────────────────────────────

    public static Payload Gather(string message)
    {
        var host = GameHost.Instance;
        var session = host?.Play?.Session;
        var headline = session is null
            ? "title screen"
            : SessionScreen.Where(session);
        return new Payload(
            string.IsNullOrWhiteSpace(message) ? "(no message)" : message.Trim(),
            headline,
            Diagnostics(host, headline),
            // On the title screen there is no live run — the save on disk is the next best thing, and it is
            // what a player who quit out of a broken room still has to hand us.
            host?.Play?.SaveJson() ?? OnDisk("user://run-save.json"),
            LastScreen is { } screen ? screen.SavePngToBuffer() : null,
            Tail("user://logs/godot.log", LogTail));
    }

    private static string Diagnostics(GameHost? host, string headline)
    {
        var lines = new List<string>();
        void Say(string key, object? value) => lines.Add($"{key,-18}{value}");

        Say("when", Time.GetDatetimeStringFromSystem(utc: true) + "Z");
        Say("game", host?.GameTitle ?? "—");
        // A report that cannot be placed in a build is a report about a game nobody can look at any more. The
        // version names the release; the content line names the DOCUMENT, which in this game changes daily and
        // is the thing a card's wrong number actually belongs to.
        var version = ProjectSettings.GetSetting("application/config/version", "").AsString();
        Say("version", string.IsNullOrWhiteSpace(version) ? "unversioned" : version);
        Say("godot", Engine.GetVersionInfo()["string"]);
        Say("os", $"{OS.GetName()} {OS.GetVersion()}");
        Say("locale", OS.GetLocale());
        Say("window", $"{DisplayServer.WindowGetSize()} · canvas scale {DisplaySettings.Scale:0.00} "
            + $"· {DisplaySettings.Mode}{(DisplaySettings.VSync ? " · vsync" : "")}");
        Say("where", headline);
        if (host?.Blueprint is { } blueprint)
            Say("content", $"schema {blueprint.SchemaVersion} · {blueprint.Cards.Count} cards "
                + $"· {blueprint.Relics.Count} relics · {blueprint.Encounters.Count} encounters");

        if (host?.HostError is { } hostError)
            Say("HOST ERROR", hostError);

        if (host?.Play is { } play)
        {
            if (play.Error is { } playError)
                Say("RUN ERROR", playError);
            if (play.Session is { } session)
            {
                var run = session.Run;
                if (session.Error is { } sessionError)
                    Say("SESSION ERROR", sessionError);
                Say("seed", run.RandomSeed);
                Say("result", run.Result);
                Say("health", $"{run.Health.Current}/{run.Health.Max}");
                Say("deck", $"{run.Deck.Count} cards · {run.Relics.Count} relics · {run.Consumables.Count} consumables");
                Say("awaiting", Awaiting(session));
                Say("map", $"{run.Map.Nodes.Count} nodes · at {run.CurrentNodeId?.Value ?? "—"}");
            }
            if (play.CombatDriver?.Current is { } combat)
                Say("combat", $"round {combat.Round} · hand {combat.Hand.Count} "
                    + $"· {combat.State.Combatants.Count} combatants");
        }
        else
        {
            Say("run", "none (title screen)");
        }
        Say("save on disk", host?.HasSave == true ? "yes" : "no");
        return string.Join("\n", lines);
    }

    // WHICH ANSWER THE GAME WAS WAITING FOR. A bug report that says "nothing happened when I clicked" is
    // answered by this line more often than by any other: a screen parked on a question it never drew is
    // indistinguishable, to the player, from a screen that ignored them.
    private static string Awaiting(RogueDeck.Sandbox.Run.InteractiveRunSession session) =>
        session.IsAwaitingChoice ? $"a choice ({session.PendingSituation?.Id ?? "—"})"
        : session.IsAwaitingEntities ? $"a pick ({session.PendingEntities?.Purpose ?? "—"})"
        : session.IsAwaitingNodeChoice ? "a room"
        : session.IsAwaitingInterlude ? "an interlude"
        : "nothing (a fight, or the run is over)";

    private static string? OnDisk(string path) =>
        Godot.FileAccess.FileExists(path) ? Godot.FileAccess.GetFileAsString(path) : null;

    private static string? Tail(string path, int characters)
    {
        if (OnDisk(path) is not { } text)
            return null;
        return text.Length <= characters ? text : text[^characters..];
    }

    // ── the local copy, which is written FIRST and always ─────────────────────────

    public static string WriteLocally(Payload payload)
    {
        var stamp = Time.GetDatetimeStringFromSystem().Replace(":", "-");
        var folder = $"{Folder}/{stamp}";
        DirAccess.MakeDirRecursiveAbsolute(folder);
        Put($"{folder}/report.txt", payload.Report);
        if (payload.SaveJson is { } save)
            Put($"{folder}/run-save.json", save);
        if (payload.Log is { } log)
            Put($"{folder}/godot.log", log);
        if (payload.Screen is { } screen)
        {
            using var file = Godot.FileAccess.Open($"{folder}/screen.png", Godot.FileAccess.ModeFlags.Write);
            file?.StoreBuffer(screen);
        }
        Prune();
        return folder;
    }

    private static void Put(string path, string text)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(text);
    }

    // A folder name is a timestamp, so the oldest are the first in order. Keeping ten is keeping about 3 MB.
    private static void Prune()
    {
        if (DirAccess.Open(Folder) is not { } dir)
            return;
        var folders = new List<string>(dir.GetDirectories());
        folders.Sort(StringComparer.Ordinal);
        for (var i = 0; i < folders.Count - KeepFolders; i++)
            Wipe($"{Folder}/{folders[i]}");
    }

    private static void Wipe(string folder)
    {
        if (DirAccess.Open(folder) is not { } dir)
            return;
        foreach (var file in dir.GetFiles())
            dir.Remove(file);
        DirAccess.Open(Folder)?.Remove(folder.Substring(Folder.Length + 1));
    }

    // ── the transport ─────────────────────────────────────────────────────────────

    // The webhook URL, looked for in the four places it can honestly live. It is NOT in this repository and
    // must not be put in it: the repo is public, and a webhook URL is a write credential for a channel.
    //   1. the environment — how a probe and a developer test the real path without a file
    //   2. `user://bugreport.cfg` — a per-machine override, e.g. a tester pointed at their own channel
    //   3. `res://bugreport.cfg` — a dev checkout (gitignored); present when running from the editor
    //   4. beside the executable — the exported game, where tools/export.sh copies it next to the binary
    //      rather than into the .pck, so swapping the channel does not mean re-exporting the game
    public static string? Webhook()
    {
        var fromEnv = OS.GetEnvironment(WebhookEnv);
        if (Usable(fromEnv))
            return fromEnv.Trim();
        // ⚠ A PROBE DOES NOT POST INTO THE LIVE CHANNEL. Once bugreport.cfg sits in a dev checkout, every
        // `--smoke-bug` run would file a report against the real channel, and a probe that spams the place
        // where real reports arrive is a probe somebody switches off. Under a smoke run the environment
        // variable is therefore the ONLY way to reach a webhook: deliberate, per command, never by accident.
        if (OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--smoke", StringComparison.Ordinal)))
            return null;
        foreach (var path in new[]
                 {
                     $"user://{WebhookFile}",
                     $"res://{WebhookFile}",
                     OS.GetExecutablePath().GetBaseDir().PathJoin(WebhookFile),
                 })
        {
            if (OnDisk(path) is { } text && Usable(text))
                return text.Trim();
        }
        return null;
    }

    // https only, with ONE exception: a listener on this machine, which is how the multipart body and the whole
    // upload path get tested without a real channel to post into (tools/bug-sink.py).
    private static bool Usable(string? url)
    {
        var trimmed = url?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed)
            && (trimmed.StartsWith("https://", StringComparison.Ordinal)
                || trimmed.StartsWith("http://127.0.0.1", StringComparison.Ordinal)
                || trimmed.StartsWith("http://localhost", StringComparison.Ordinal));
    }

    // Sends the report. Returns null when it landed, or the reason it did not — and the reason is shown to the
    // player, because "it did not work" without a because is what makes someone report the same bug twice.
    public static async Task<string?> Upload(Godot.Node host, Payload payload)
    {
        if (Webhook() is not { } url)
            return "no upload is configured for this build";

        var request = new HttpRequest { Timeout = 30 };
        host.AddChild(request);
        try
        {
            var boundary = $"----bnb{Time.GetTicksUsec():x}";
            var body = Multipart(boundary, payload);
            string[] headers = [$"Content-Type: multipart/form-data; boundary={boundary}"];
            var error = request.RequestRaw(url, headers, Godot.HttpClient.Method.Post, body);
            if (error != Error.Ok)
                return $"the request could not be started ({error})";

            var finished = await host.ToSignal(request, HttpRequest.SignalName.RequestCompleted);
            var result = (HttpRequest.Result)(int)finished[0];
            var code = (int)finished[1];
            if (result != HttpRequest.Result.Success)
                return $"the upload did not reach the server ({result})";
            return code switch
            {
                >= 200 and < 300 => null,
                401 or 403 or 404 => "the upload address is no longer valid — tell the developer",
                429 => "too many reports just now; try again in a minute",
                _ => $"the server refused the report (HTTP {code})",
            };
        }
        catch (Exception ex)
        {
            return $"the upload failed: {ex.Message}";
        }
        finally
        {
            request.QueueFree();
        }
    }

    // Discord's webhook form: one `payload_json` part plus one `files[n]` part per attachment. The message also
    // goes in `content` so the channel is readable without opening a file — truncated there, never in the file.
    private static byte[] Multipart(string boundary, Payload payload)
    {
        var files = new List<(string Name, string Mime, byte[] Bytes)>();
        void Attach(string name, string mime, byte[]? bytes)
        {
            if (bytes is { Length: > 0 })
                files.Add((name, mime, bytes));
        }
        Attach("report.txt", "text/plain", Encoding.UTF8.GetBytes(payload.Report));
        if (payload.SaveJson is { } save)
            Attach("run-save.json", "application/json", Encoding.UTF8.GetBytes(save));
        Attach("screen.png", "image/png", payload.Screen);
        if (payload.Log is { } log)
            Attach("godot.log", "text/plain", Encoding.UTF8.GetBytes(log));

        var attachments = new Godot.Collections.Array();
        for (var i = 0; i < files.Count; i++)
            attachments.Add(new Godot.Collections.Dictionary { { "id", i }, { "filename", files[i].Name } });

        var json = Json.Stringify(new Godot.Collections.Dictionary
        {
            { "content", Content(payload) },
            { "attachments", attachments },
            // A report must never summon the channel; it is read when someone sits down to read reports.
            { "allowed_mentions", new Godot.Collections.Dictionary { { "parse", new Godot.Collections.Array() } } },
        });

        using var stream = new MemoryStream();
        void Write(string text) => stream.Write(Encoding.UTF8.GetBytes(text));

        Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"payload_json\"\r\n"
            + "Content-Type: application/json\r\n\r\n");
        Write(json);
        Write("\r\n");
        for (var i = 0; i < files.Count; i++)
        {
            Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"files[{i}]\"; "
                + $"filename=\"{files[i].Name}\"\r\nContent-Type: {files[i].Mime}\r\n\r\n");
            stream.Write(files[i].Bytes);
            Write("\r\n");
        }
        Write($"--{boundary}--\r\n");
        return stream.ToArray();
    }

    private static string Content(Payload payload)
    {
        var head = $"🐞 **{payload.Headline}**\n";
        var quoted = payload.Message.Replace("\r", "").Replace("\n", "\n> ");
        var body = $"> {quoted}";
        var room = DiscordContentLimit - head.Length - 2;
        return head + (body.Length <= room ? body : body[..room] + " …");
    }
}
