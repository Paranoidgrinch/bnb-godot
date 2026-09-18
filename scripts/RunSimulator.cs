using Godot;
using RogueDeck.Bot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace BnbGodot;

// The run simulator, as Godot offers it: a thin caller of `RogueDeck.Bot.RunBot`.
//
// ⚠⚠ THE BRAIN IS NOT HERE ANY MORE (R4). It used to be — four hundred lines of answer loop, guards, policy
// and log format — and so did a second copy in `--smoke-marathon` and a third in bnb-content's playtest
// walker. Three walkers, three sets of guards, one of which learnt Act IV's measure rule months after the
// others (S8). What is left in this file is what is genuinely Godot's: reading the command line, yielding a
// frame every twenty answers so the scene tree can collect what the last redraw freed, folding in the screen
// faults R2a made countable, and the exit code.
//
//   godot --headless -- --sim [--sim-seed N] [--sim-immortal] [--sim-steps N] [--sim-ui] [--sim-policy f]
public partial class SessionScreen : Control
{
    public static string? SimCharacter;   // whoever Boot rolled for this run, for the log header

    public static bool IsSimulating => OS.GetCmdlineUserArgs().Contains("--sim");

    public static int SimArg(string name, int fallback)
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var value) ? value : fallback;
    }

    private static string? SimStringArg(string name)
    {
        var args = OS.GetCmdlineUserArgs();
        var at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    private async System.Threading.Tasks.Task SimulateRun()
    {
        if (Play is not { } play)
        {
            GD.Print("sim: there is no run to walk");
            GetTree().Quit(2);
            return;
        }

        BotPolicy? policy = null;
        if (SimStringArg("--sim-policy") is { } policyPath)
        {
            policy = ReadPolicy(policyPath);
            if (policy is null)
            {
                GD.Print($"sim: could not read the policy at {policyPath}");
                GetTree().Quit(2);
                return;
            }
        }

        var options = new BotOptions
        {
            Seed = SimArg("--sim-seed", 1),
            Budget = SimArg("--sim-steps", 40000),
            // WHICH GAME THIS IS A REPORT ABOUT. The generator is named on every line a reader might see on
            // its own, because two runs of the same seed on two generators are two different games.
            Maps = MapGenerators.Name(Session?.Run.GeneratedMapGenerator),
            Character = SimCharacter,
            Policy = policy,
            // Only a policy runner scores cards, and only then is the document worth re-reading for it.
            Features = policy is null ? null
                : CardFeatures.FromDocument(Godot.FileAccess.GetFileAsString(GameDocument)),
        };

        // One frame every twenty answers: the screen rebuilds into fresh nodes per answer and frees the old
        // ones deferred — a walk that never yields never lets the tree collect anything. (With `--sim-ui`
        // off nothing is drawn at all, but the yield costs nothing and the walk stays identical either way.)
        var result = await RunBot.Play(play, options, new DelegateBotLog(GD.Print),
            breathe: async () => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame));

        // A redraw that threw is a problem of the run, not a note beside it: it is what makes the batch stop
        // at this seed. Zero when nothing was drawn, so a batch that draws nothing reads exactly as before.
        // The number is a FLOOR: a few redraws still fire between here and the quit, and those print their
        // `!! PROBLEM screen` line — which is what simulate.sh greps for — without being counted into it.
        result = result with { Problems = result.Problems + _screenFaults };

        foreach (var line in BotReport.ActLines(result))
            GD.Print(line);
        GD.Print(BotReport.Fitness(result));
        GD.Print(BotReport.Result(result));

        // A lost run is a NORMAL outcome and exits clean; only something the run could not answer for —
        // an engine error, a refused play, a wall, a thrown exception — is worth the batch's attention.
        GetTree().Quit(result.Clean ? 0 : 1);
    }

    internal const string GameDocument = "res://content/game.roguedeck.json";

    // A policy may sit anywhere the trainer put it — `res://`, `user://`, or an ordinary path on disk — so it
    // is read through Godot's FileAccess, which understands all three.
    private static BotPolicy? ReadPolicy(string path)
    {
        var json = Godot.FileAccess.GetFileAsString(path);
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return BotPolicy.FromJson(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
