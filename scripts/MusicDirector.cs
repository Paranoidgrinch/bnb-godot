using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BnbGodot;

// WHAT THE GAME SOUNDS LIKE, AND WHO DECIDES IT.
//
// The whole music system is one idea: a screen never says "play this file", it says WHERE THE PLAYER IS
// (MusicDirector.Want) and this node works out what should be sounding. Two things follow from that, and
// both are the reason it is built this way rather than with a Play() call scattered over the screens:
//
//   · A cue that is already playing is left alone. The session screen redraws on every card played, and if
//     "walk into the map" meant "start the act theme" the act theme would restart several times a second.
//     Want() compares and returns; the ONE state change is the only thing that costs anything.
//   · Leaving somewhere returns you to where you were. A shop, a campfire and an elite fight all interrupt
//     an act theme, and the act theme comes back AT THE BAR IT WAS ON — the position is remembered per cue
//     (`_resume`), because a forty-second errand that rewinds the music to its first note is a worse
//     result than no music at all.
//
// Crossfades are done with two players rather than one: the outgoing track fades out while the incoming
// one fades in, so nothing is ever cut off and nothing is ever stacked at full volume. The PLAYER volumes
// carry the fade; the player's own Music Volume setting lives on the bus underneath (AudioSettings), so
// the two never fight and moving the slider mid-fade does the obvious thing.
public partial class MusicDirector : Godot.Node
{
    public static MusicDirector? Instance { get; private set; }

    // Where the player is, in the only terms the music cares about. The order is the priority order from
    // the integration document — Act V first, Title last — and `Cue` below resolves a run state to exactly
    // one of these.
    public enum Track
    {
        None,
        ActFive,        // Act V and its final confrontation: one theme, replacing the whole act structure
        Boss,           // any Act I–IV boss fight
        Elite,          // any Act I–IV elite fight
        Campfire,       // rest sites
        Shop,           // shops
        ActOne,
        ActTwo,
        ActThree,
        ActFour,
        Title,
    }

    // The file behind each cue. Named for the state and the track both, so the mapping stays readable from
    // the folder alone — a file that stops existing is a missing-music warning at boot, not a silent screen.
    private static readonly Dictionary<Track, string> Files = new()
    {
        [Track.Title] = "music_title_secret_sanctum.ogg",
        [Track.Campfire] = "music_campfire_waystone_inn.ogg",
        [Track.Shop] = "music_shop_savvy_merchant.ogg",
        [Track.ActOne] = "music_act1_victoriana_loop.ogg",
        [Track.ActTwo] = "music_act2_dark_chamber.ogg",
        [Track.ActThree] = "music_act3_forest_whisper_theme.ogg",
        [Track.ActFour] = "music_act4_eternal_sands.ogg",
        [Track.Elite] = "music_elite_dark_descent.ogg",
        [Track.Boss] = "music_boss_desecrated_temple.ogg",
        [Track.ActFive] = "music_act5_land_of_the_great_gods.ogg",
    };

    private const string Folder = "res://assets/music";
    private const float FadeOut = 0.9f;
    private const float FadeIn = 1.2f;

    // ⚠ THE ACT THEMES ARE THE ONLY ONES WORTH COMING BACK TO. A shop is a minute long and an act is an
    // hour, so the act theme resumes where it was and everything else starts at its beginning — coming back
    // to the middle of a boss theme you heard four rooms ago is disorientation, not continuity.
    private static readonly HashSet<Track> Resumable =
        [Track.ActOne, Track.ActTwo, Track.ActThree, Track.ActFour, Track.ActFive];

    private readonly Dictionary<Track, float> _resume = [];
    private readonly Dictionary<Track, AudioStream?> _streams = [];
    private AudioStreamPlayer _a = null!;
    private AudioStreamPlayer _b = null!;
    private bool _bIsCurrent;
    private Tween? _fade;

    public Track Current { get; private set; } = Track.None;

    // ⚠ A PROBE WALKS A WHOLE GAME IN SECONDS. Starting, crossfading and stopping ten streams at that speed
    // is thousands of pointless tween kills, so under a probe the director keeps its STATE and skips the
    // sound. Deciding what should play is free, and it is the half worth exercising: a run through all five
    // acts is the only thing that ever asks the cue policy every question in order.
    public bool Silent { get; set; }

    // How many times the music has actually changed, and which tracks a run actually reached. A whole-game
    // probe reports both, because the count alone is a weak claim: a run that re-cues on every redraw would
    // show thousands and one that never cues would show one, but only the SET says whether the shop theme
    // and the campfire theme were ever heard at all. Ten tracks that ship and two that play is a bug the
    // count cannot see.
    public int Changes { get; private set; }

    public HashSet<Track> Visited { get; } = [];

    // What the last Want() actually did, for the --smoke-music probe and for the bug report: a music system
    // that silently plays nothing looks exactly like a music system that is working.
    public string LastAction { get; private set; } = "nothing yet";

    private AudioStreamPlayer Live => _bIsCurrent ? _b : _a;
    private AudioStreamPlayer Idle => _bIsCurrent ? _a : _b;

    public override void _Ready()
    {
        Instance = this;
        // Every probe and the run simulator walk the game far faster than a person can, so they get the
        // policy without the sound — except `--smoke-music`, whose whole job is the sound.
        var args = OS.GetCmdlineUserArgs();
        Silent = !args.Contains("--smoke-music")
            && args.Any(a => a.StartsWith("--smoke", StringComparison.Ordinal)
                || a.StartsWith("--sim", StringComparison.Ordinal));
        AudioSettings.Apply();
        _a = NewPlayer("MusicA");
        _b = NewPlayer("MusicB");
    }

    private AudioStreamPlayer NewPlayer(string name)
    {
        var player = new AudioStreamPlayer
        {
            Name = name,
            Bus = AudioSettings.Bus,
            VolumeDb = -60f,
            // The music must not stop because a fight paused the tree for a title card.
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(player);
        return player;
    }

    // ── what should be playing ───────────────────────────────────────────────────

    // The run state, reduced to one cue, in the document's priority order. Pure and static on purpose: this
    // is the whole policy of the music system and it is decided in one readable place, not spread across
    // the screens that happen to know each fact.
    public static Track Cue(int actNumber, bool inCombat, bool boss, bool elite, bool shop, bool rest)
    {
        // ACT V IS THE EXCEPTION, and it is first for that reason: the gauntlet has no separate map, shop,
        // elite or boss music — one theme is the identity of the whole act, including its last fight.
        if (actNumber >= 5)
            return Track.ActFive;
        if (inCombat && boss)
            return Track.Boss;
        if (inCombat && elite)
            return Track.Elite;
        if (rest)
            return Track.Campfire;
        if (shop)
            return Track.Shop;
        return actNumber switch
        {
            <= 1 => Track.ActOne,
            2 => Track.ActTwo,
            3 => Track.ActThree,
            _ => Track.ActFour,
        };
    }

    // ── making it so ─────────────────────────────────────────────────────────────

    // Ask for a cue. Cheap and idempotent: call it on every redraw.
    public void Want(Track track)
    {
        if (track == Current)
        {
            LastAction = $"{track} already playing";
            return;
        }
        if (track == Track.None)
        {
            Stop();
            return;
        }

        Changes++;
        Visited.Add(track);
        if (Silent)
        {
            LastAction = $"{Current} → {track} (silent)";
            Current = track;
            return;
        }

        if (Load(track) is not { } stream)
        {
            LastAction = $"{track}: no file at {Folder}/{Files.GetValueOrDefault(track)}";
            return;
        }

        // Where the outgoing track had got to, so it can be picked up again later.
        if (Current != Track.None && Resumable.Contains(Current) && Live.Playing)
            _resume[Current] = Live.GetPlaybackPosition();

        var incoming = Idle;
        incoming.Stream = stream;
        incoming.VolumeDb = -60f;
        var from = Resumable.Contains(track) ? _resume.GetValueOrDefault(track) : 0f;
        incoming.Play(from);

        Crossfade(incoming, Live);
        _bIsCurrent = !_bIsCurrent;
        var previous = Current;
        Current = track;
        LastAction = $"{previous} → {track}" + (from > 0.01f ? $" (resumed at {from:0.0}s)" : "");
    }

    private void Crossfade(AudioStreamPlayer incoming, AudioStreamPlayer outgoing)
    {
        _fade?.Kill();
        _fade = CreateTween().SetParallel();
        _fade.SetProcessMode(Tween.TweenProcessMode.Idle);
        _fade.TweenProperty(incoming, "volume_db", 0f, FadeIn).SetTrans(Tween.TransitionType.Sine);
        if (outgoing.Playing)
        {
            _fade.TweenProperty(outgoing, "volume_db", -60f, FadeOut).SetTrans(Tween.TransitionType.Sine);
            // Stopping it is not cosmetic: two streams decoding forever is the kind of leak that only shows
            // up after an hour of play, which is the hour that matters.
            _fade.Chain().TweenCallback(Callable.From(outgoing.Stop));
        }
    }

    // ⚠ LET THE STREAMS GO BEFORE GODOT COUNTS THEM. The cache holds a reference to each loaded track, and an
    // autoload is torn down after the scene tree but before the object database is swept — so ten cached
    // streams come out at exit as "10 ObjectDB instances were leaked", every run, in every log a player might
    // send with a bug report. They are not a leak during play (that is what a cache IS), only at the door.
    public override void _ExitTree()
    {
        _fade?.Kill();
        _fade = null;
        _a.Stream = null;
        _b.Stream = null;
        _streams.Clear();
        if (Instance == this)
            Instance = null;
    }

    public void Stop()
    {
        _fade?.Kill();
        _fade = null;
        _a.Stop();
        _b.Stop();
        Current = Track.None;
        LastAction = "stopped";
    }

    // ── the files ────────────────────────────────────────────────────────────────

    private AudioStream? Load(Track track)
    {
        if (_streams.TryGetValue(track, out var cached))
            return cached;
        AudioStream? stream = null;
        if (Files.TryGetValue(track, out var file))
        {
            var path = $"{Folder}/{file}";
            if (ResourceLoader.Exists(path))
                stream = ResourceLoader.Load<AudioStream>(path);
            else
                GD.PushWarning($"music: nothing at {path} — {track} will be silent");
        }
        _streams[track] = stream;
        return stream;
    }

    // Every cue and the file behind it, with the loop flag the importer gave it. The --smoke-music probe's
    // material: a track that ships without `loop` set plays once and leaves the game silent, and that is
    // invisible until somebody sits on a screen for four minutes.
    //
    // ⚠ NOT AN ITERATOR. `yield return` compiles to a state machine whose locals survive across each yield,
    // so the stream opened to read one track's flag stays referenced while the caller processes it — and ten
    // of those are still held when Godot sweeps its object database at exit, which it reports as ten leaked
    // instances. Reading each track inside a method that RETURNS lets every stream go at once.
    public List<(Track Track, string File, bool Present, bool Loops, double Seconds)> Inventory()
    {
        var rows = new List<(Track, string, bool, bool, double)>();
        foreach (var (track, file) in Files)
            rows.Add(Describe(track, file));
        return rows;
    }

    private static (Track, string, bool, bool, double) Describe(Track track, string file)
    {
        var path = $"{Folder}/{file}";
        if (!ResourceLoader.Exists(path))
            return (track, file, false, false, 0);
        var stream = ResourceLoader.Load<AudioStream>(path);
        return (track, file, true, stream is AudioStreamOggVorbis { Loop: true }, stream?.GetLength() ?? 0);
    }
}
