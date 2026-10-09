using System;
using Godot;

namespace BnbGodot;

// THE FIGHT'S SOUNDS: a blow, block, a debuff, a buff, a body falling (assets/sfx/SOURCES.md). An autoload, like
// the music, so a sound that has started is not cut off by the redraw that follows it — a fight is redrawn
// whole on every change. Every file was normalised to the same loudness, so nothing here sets a volume per
// sound: the Effects slider (AudioSettings) is the one thing that does.
//
// ⚠ ITS OWN DICE. The pick of which of the 37 blows plays is not the game's luck and may not touch it — a
// System.Random of its own, never Godot's global one, which nothing in a run is allowed to share.
public partial class Sfx : Node
{
    public enum Kind { Hit, Block, Debuff, Buff, Death }

    private const int Voices = 10;
    private const double Spacing = 0.07; // the same kind twice in this time is one sound

    private static Sfx? _instance;
    private static readonly Random Dice = new();

    private static readonly string[] Hits = Numbered("res://assets/sfx/hits/hit{0:00}.ogg", 37);
    private static readonly string[] Metal =
    [
        "res://assets/sfx/metal/metal_interaction1.wav",
        "res://assets/sfx/metal/metal_interaction2.wav",
        "res://assets/sfx/metal/metal_button_press1.wav",
        "res://assets/sfx/metal/metal_button_press2.wav",
        "res://assets/sfx/metal/metal_swing1.wav",
    ];
    private const string Drain = "res://assets/sfx/debuff_energy_drain.ogg";
    private const string Spell = "res://assets/sfx/buff_spell3.wav";
    private const string Fall = "res://assets/sfx/death_falling_body.wav";

    private readonly AudioStreamPlayer[] _voices = new AudioStreamPlayer[Voices];
    private readonly double[] _last = new double[Enum.GetValues<Kind>().Length];
    private int _next;

    // What has been played, by kind — for the probe that checks the sounds arrive (SessionScreen.SmokeEffects).
    public static readonly int[] Played = new int[Enum.GetValues<Kind>().Length];

    // Probes and the simulator walk the game far faster than a person can: they get the effects without the
    // sound, as they get the policy without the music.
    public static bool Silent { get; private set; }

    public override void _Ready()
    {
        _instance = this;
        var args = OS.GetCmdlineUserArgs();
        // (`--smoke-effects` is the probe whose whole job is the fight's feedback, so it is heard.)
        Silent = Array.Exists(args, a => (a.StartsWith("--smoke", StringComparison.Ordinal) && a != "--smoke-effects")
            || a.StartsWith("--sim", StringComparison.Ordinal));
        AudioSettings.Apply();
        for (var i = 0; i < Voices; i++)
        {
            _voices[i] = new AudioStreamPlayer { Name = $"Voice{i}", Bus = AudioSettings.EffectsBus };
            AddChild(_voices[i]);
        }
    }

    public static void Hit() => Play(Kind.Hit);

    public static void Play(Kind kind)
    {
        if (Silent || _instance is not { } sfx)
            return;
        var now = Time.GetTicksMsec() / 1000.0;
        if (now - sfx._last[(int)kind] < Spacing)
            return;
        sfx._last[(int)kind] = now;
        var path = kind switch
        {
            Kind.Hit => Hits[Dice.Next(Hits.Length)],
            Kind.Block => Metal[Dice.Next(Metal.Length)],
            Kind.Debuff => Drain,
            Kind.Buff => Spell,
            _ => Fall,
        };
        if (GD.Load<AudioStream>(path) is not { } stream)
            return;
        // The oldest voice is the one taken when all are busy: a fight's sounds are short and the newest blow
        // is the one the player is looking at.
        var voice = sfx._voices[sfx._next];
        sfx._next = (sfx._next + 1) % Voices;
        voice.Stream = stream;
        voice.Play();
        Played[(int)kind]++;
    }

    private static string[] Numbered(string pattern, int count)
    {
        var paths = new string[count];
        for (var i = 0; i < count; i++)
            paths[i] = string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, i + 1);
        return paths;
    }
}
