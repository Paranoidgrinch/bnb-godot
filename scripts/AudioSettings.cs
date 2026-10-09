using System;
using Godot;

namespace BnbGodot;

// HOW LOUD THE GAME IS — master, music, effects — and the one place that is decided. It is a single global setting — the title
// screen's slider and the Esc menu's slider are not two settings that are kept in step, they are two views
// of this one, which is why changing either shows up in the other with nothing to synchronise.
//
// Stored in `user://settings.cfg` beside the display settings (section `[audio]`), so a player carries one
// file and one set of preferences. ⚠ Both writers Load() before they Save() — see DisplaySettings.Save.
//
// THREE SLIDERS, THREE BUSES (user, 2026-10-09): Master is Godot's own bus, which everything ends on; Music
// and Effects are made here and both send to it. So "Master 50 %" halves the music AND the blows, and either of
// the other two can be turned off without touching the rest.
//
// The scale is the player's, not the engine's: 0–100 % where 100 is the volume the tracks were mastered to
// (they are all normalised to the same loudness, so "70 %" means the same thing on every screen of the
// game). The conversion to decibels is perceptual — a slider that runs linearly in dB spends its whole
// lower half inaudible.
public static class AudioSettings
{
    public enum Channel { Master, Music, Effects }

    public const string Bus = "Music";
    public const string EffectsBus = "Effects";

    private const string Path = "user://settings.cfg";
    private const string Section = "audio";

    // 0–100 each. The music's default is deliberately below full: music under a game is accompaniment, and a
    // player who has never touched the slider should not be reaching for it on the title screen. The effects
    // are normalised to sit under it at their own full value (assets/sfx/SOURCES.md).
    private static readonly int[] Volumes = [100, 70, 80];
    private static readonly string[] Keys = ["master_volume", "music_volume", "effects_volume"];

    public static int MusicVolume => Get(Channel.Music);

    public static int Get(Channel channel)
    {
        Load();
        return Volumes[(int)channel];
    }

    private static bool _loaded;

    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        var file = new ConfigFile();
        if (file.Load(Path) != Error.Ok)
            return;
        for (var i = 0; i < Keys.Length; i++)
            Volumes[i] = Math.Clamp((int)file.GetValue(Section, Keys[i], Volumes[i]), 0, 100);
    }

    public static void Save()
    {
        var file = new ConfigFile();
        file.Load(Path);                       // keep [display] — see DisplaySettings.Save
        for (var i = 0; i < Keys.Length; i++)
            file.SetValue(Section, Keys[i], Volumes[i]);
        file.Save(Path);
    }

    // The bus a channel plays through. Music and Effects are made here rather than in a default_bus_layout.tres
    // so the volume has exactly one owner and a missing resource cannot silently put a player on Master, where
    // its own slider would not reach it.
    public static int BusIndex(Channel channel = Channel.Music)
    {
        if (channel == Channel.Master)
            return 0;
        var name = channel == Channel.Music ? Bus : EffectsBus;
        var bus = AudioServer.GetBusIndex(name);
        if (bus < 0)
        {
            AudioServer.AddBus();
            bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, name);
            AudioServer.SetBusSend(bus, "Master");
        }
        return bus;
    }

    // Put the stored volumes on the buses. Called at boot and on every change, so "changes apply immediately"
    // is not a feature the sliders have to implement — it is the only thing this method does.
    public static void Apply()
    {
        Load();
        foreach (var channel in Enum.GetValues<Channel>())
        {
            var bus = BusIndex(channel);
            var volume = Volumes[(int)channel];
            // ⚠ MUTE, DO NOT JUST TURN DOWN. LinearToDb(0) is -inf, which Godot will happily put on a bus and
            // then produce NaN from; and a bus left at -80 dB is not silence on every output device.
            AudioServer.SetBusMute(bus, volume <= 0);
            AudioServer.SetBusVolumeDb(bus, volume <= 0 ? -60f : Mathf.LinearToDb(volume / 100f));
        }
    }

    public static void Set(Channel channel, int percent)
    {
        Load();
        var wanted = Math.Clamp(percent, 0, 100);
        if (wanted == Volumes[(int)channel])
            return;
        Volumes[(int)channel] = wanted;
        Apply();
        Save();
    }
}
