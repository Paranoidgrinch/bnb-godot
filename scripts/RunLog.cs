using System.IO;
using System.Text;
using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace BnbGodot;

// EVERY RUN A PLAYER PLAYS, WRITTEN DOWN AND SENT HOME. The recording itself is the engine's (RunRecording:
// seed, start, every answer, one fingerprint per room — enough to replay the run step for step, a few kilobytes
// for a whole game); this class is only its life in the game:
//
//   • it starts with the run and is attached again on every resume, so a run that spans three evenings is ONE
//     recording
//   • it is written beside the save, in the same moment and only when the save landed — so the two can never
//     disagree about how far the run got
//   • when the run ends (won, lost, or abandoned by starting another over it) it moves to a queue folder, and the
//     queue is posted to a Discord webhook, oldest first; a file leaves the queue only when Discord took it, so
//     a player with no network sends it next time
//
// A GitHub workflow in Paranoidgrinch/bnb-runs reads the channel and files each recording under its player.
//
// ⚠ ONLY A PLAYER IS RECORDED. The runner, the golden set and every probe start the game with arguments; their
// runs are not anybody's decisions and must never reach the channel the real ones arrive in. BNB_RUNLOG_FORCE=1
// records anyway (for testing this class), and the webhook lookup refuses probes on its own as well.
public static class RunLog
{
    private const string OpenPath = "user://run-record.json";
    private const string QueueFolder = "user://run-uploads";
    private const string WebhookFile = "runlog.cfg";
    private const string WebhookEnv = "BNB_RUNLOG_WEBHOOK";

    private static RunRecorder? _recorder;
    private static bool _flushing;

    // The recording of the run in play, if one is being kept — the history reads its room count at the end.
    public static RunRecording? Current => _recorder?.Recording;

    public static bool Enabled =>
        OS.GetCmdlineUserArgs().Length == 0 || OS.GetEnvironment("BNB_RUNLOG_FORCE") == "1";

    // A new run: whatever run was still open is over (abandoned), and a fresh recording begins.
    public static void Begin(
        RunPlayback play, int seed, string? character, string? mapGenerator, MetaState meta, string? contentHash)
    {
        if (!Enabled)
            return;
        Abandon();
        var recording = RunRecorder.Begin(
            seed, character, mapGenerator, meta,
            new RunRecordingPlayer(PlayerIdentity.Name ?? "unnamed", PlayerIdentity.Id),
            contentHash, $"bnb-godot {ProjectSettings.GetSetting("application/config/version").AsString()}",
            DateTime.UtcNow);
        Attach(play, new RunRecorder(recording));
    }

    // A resumed run picks its recording back up — if there is one, and it is this run's. A save from before
    // recordings existed simply goes unrecorded: its first half is gone and a half cannot be replayed.
    public static void Resume(RunPlayback play, int seed)
    {
        if (!Enabled)
            return;
        _recorder?.Detach();
        _recorder = null;
        if (Read(OpenPath) is not { } open || open.Start.Seed != seed || open.Result is not null)
            return;
        open.Resumes++;
        Attach(play, new RunRecorder(open));
    }

    private static void Attach(RunPlayback play, RunRecorder recorder)
    {
        _recorder?.Detach();
        try
        {
            recorder.Attach(play);
            _recorder = recorder;
            Write(OpenPath, recorder.Recording);
        }
        catch (Exception ex)
        {
            // A recording is never worth a run: if it cannot attach, the run is played unrecorded.
            GD.PushWarning($"run log: not recording this run ({ex.Message})");
            _recorder = null;
        }
    }

    // Called right after the run's save landed, so the recording on disk always matches the save on disk.
    public static void Saved()
    {
        if (_recorder is { } recorder)
            Write(OpenPath, recorder.Recording);
    }

    // After every state change: a run that has just ended is sealed and queued.
    public static void Observe(Godot.Node host)
    {
        if (_recorder is not { Recording.Result: not null } recorder)
            return;
        recorder.Detach();
        _recorder = null;
        recorder.Recording.EndedUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        Enqueue(recorder.Recording);
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(OpenPath));
        _ = Flush(host);
    }

    // The open run will never be finished: it was given up (a new run over it, or the run thrown away).
    public static void Abandon()
    {
        var open = _recorder?.Recording ?? Read(OpenPath);
        _recorder?.Detach();
        _recorder = null;
        if (open is { Result: null } && open.Answers.Count > 0)
        {
            open.Result = "Abandoned";
            open.EndedUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            Enqueue(open);
            RunHistory.Abandoned(open);
        }
        if (Godot.FileAccess.FileExists(OpenPath))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(OpenPath));
    }

    private static void Enqueue(RunRecording recording)
    {
        DirAccess.MakeDirRecursiveAbsolute(QueueFolder);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        Write($"{QueueFolder}/{stamp}-{recording.Start.Seed}.bnbrun.json", recording);
    }

    // Post every queued recording, oldest first. Stops at the first that does not land (no network, no webhook,
    // Discord refusing) and leaves it and everything after it for the next start.
    public static async Task Flush(Godot.Node host)
    {
        if (_flushing || BugReport.FindWebhook(WebhookFile, WebhookEnv) is not { } url)
            return;
        _flushing = true;
        try
        {
            using var dir = DirAccess.Open(QueueFolder);
            if (dir is null)
                return;
            foreach (var name in dir.GetFiles().Where(f => f.EndsWith(".bnbrun.json", StringComparison.Ordinal)).Order())
            {
                var path = $"{QueueFolder}/{name}";
                var text = Godot.FileAccess.GetFileAsString(path);
                if (await Post(host, url, name, text) is { } problem)
                {
                    GD.Print($"run log: {name} not sent yet ({problem})");
                    return;
                }
                dir.Remove(name);
            }
        }
        finally
        {
            _flushing = false;
        }
    }

    private static async Task<string?> Post(Godot.Node host, string url, string fileName, string text)
    {
        var request = new HttpRequest { Timeout = 30 };
        host.AddChild(request);
        try
        {
            var boundary = $"----bnbrun{Time.GetTicksUsec():x}";
            var body = Multipart(boundary, Summary(text), fileName, Encoding.UTF8.GetBytes(text));
            string[] headers = [$"Content-Type: multipart/form-data; boundary={boundary}"];
            if (request.RequestRaw(url, headers, Godot.HttpClient.Method.Post, body) is var error and not Error.Ok)
                return $"the request could not be started ({error})";
            var finished = await host.ToSignal(request, HttpRequest.SignalName.RequestCompleted);
            var result = (HttpRequest.Result)(int)finished[0];
            var code = (int)finished[1];
            if (result != HttpRequest.Result.Success)
                return $"no connection ({result})";
            return code is >= 200 and < 300 ? null : $"HTTP {code}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            request.QueueFree();
        }
    }

    // One line for the channel, so it can be read without opening a file.
    private static string Summary(string text)
    {
        try
        {
            var recording = RunRecordingJson.FromJson(text);
            var last = recording.Rooms.Count > 0 ? recording.Rooms[^1].State.Split(" | ")[0] : "—";
            return $"🎲 **{recording.Player?.Name ?? "?"}** · {recording.Result ?? "?"} · reached {last}"
                + $" · seed {recording.Start.Seed} · {recording.Answers.Count} answers";
        }
        catch (Exception)
        {
            return "🎲 run recording";
        }
    }

    private static byte[] Multipart(string boundary, string content, string fileName, byte[] file)
    {
        var json = Json.Stringify(new Godot.Collections.Dictionary
        {
            { "content", content },
            { "attachments", new Godot.Collections.Array
                { new Godot.Collections.Dictionary { { "id", 0 }, { "filename", fileName } } } },
            { "allowed_mentions", new Godot.Collections.Dictionary { { "parse", new Godot.Collections.Array() } } },
        });
        using var stream = new MemoryStream();
        void Put(string s) => stream.Write(Encoding.UTF8.GetBytes(s));
        Put($"--{boundary}\r\nContent-Disposition: form-data; name=\"payload_json\"\r\n"
            + "Content-Type: application/json\r\n\r\n");
        Put(json);
        Put($"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"files[0]\"; "
            + $"filename=\"{fileName}\"\r\nContent-Type: application/json\r\n\r\n");
        stream.Write(file);
        Put($"\r\n--{boundary}--\r\n");
        return stream.ToArray();
    }

    private static RunRecording? Read(string path)
    {
        if (!Godot.FileAccess.FileExists(path))
            return null;
        try
        {
            return RunRecordingJson.FromJson(Godot.FileAccess.GetFileAsString(path));
        }
        catch (Exception ex)
        {
            GD.PushWarning($"run log: {path} unreadable ({ex.Message})");
            return null;
        }
    }

    private static void Write(string path, RunRecording recording)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(RunRecordingJson.ToJson(recording));
    }
}
