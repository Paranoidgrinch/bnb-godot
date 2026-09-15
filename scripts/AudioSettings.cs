using System;
using Godot;

namespace BnbGodot;

// HOW LOUD THE MUSIC IS, and the one place that is decided. It is a single global setting — the title
// screen's slider and the Esc menu's slider are not two settings that are kept in step, they are two views
// of this one, which is why changing either shows up in the other with nothing to synchronise.
//
// Stored in `user://settings.cfg` beside the display settings (section `[audio]`), so a player carries one
// file and one set of preferences. ⚠ Both writers Load() before they Save() — see DisplaySettings.Save.
//
// The scale is the player's, not the engine's: 0–100 % where 100 is the volume the tracks were mastered to
// (they are all normalised to the same loudness, so "70 %" means the same thing on every screen of the
// game). The conversion to decibels is perceptual — a slider that runs linearly in dB spends its whole
// lower half inaudible.
public static class AudioSettings
{
    public const string Bus = "Music";

    private const string Path = "user://settings.cfg";
    private const string Section = "audio";

    // 0–100. The default is deliberately below full: music under a game is accompaniment, and a player who
    // has never touched the slider should not be reaching for it on the title screen.
    public static int MusicVolume { get; private set; } = 70;

    private static bool _loaded;
    private static int _bus = -1;

    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        var file = new ConfigFile();
        if (file.Load(Path) != Error.Ok)
            return;
        MusicVolume = Math.Clamp((int)file.GetValue(Section, "music_volume", 70), 0, 100);
    }

    public static void Save()
    {
        var file = new ConfigFile();
        file.Load(Path);                       // keep [display] — see DisplaySettings.Save
        file.SetValue(Section, "music_volume", MusicVolume);
        file.Save(Path);
    }

    // The bus every music player routes through. Made here rather than in a default_bus_layout.tres so the
    // volume has exactly one owner and a missing resource cannot silently put the music on Master, where the
    // slider would not reach it.
    public static int BusIndex()
    {
        if (_bus >= 0 && _bus < AudioServer.BusCount && AudioServer.GetBusName(_bus) == Bus)
            return _bus;
        _bus = AudioServer.GetBusIndex(Bus);
        if (_bus < 0)
        {
            AudioServer.AddBus();
            _bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(_bus, Bus);
            AudioServer.SetBusSend(_bus, "Master");
        }
        return _bus;
    }

    // Put the stored volume on the bus. Called at boot and on every change, so "changes apply immediately"
    // is not a feature the sliders have to implement — it is the only thing this method does.
    public static void Apply()
    {
        Load();
        var bus = BusIndex();
        // ⚠ MUTE, DO NOT JUST TURN DOWN. LinearToDb(0) is -inf, which Godot will happily put on a bus and
        // then produce NaN from; and a bus left at -80 dB is not silence on every output device.
        AudioServer.SetBusMute(bus, MusicVolume <= 0);
        AudioServer.SetBusVolumeDb(bus, MusicVolume <= 0 ? -60f : Mathf.LinearToDb(MusicVolume / 100f));
    }

    public static void SetMusicVolume(int percent)
    {
        var wanted = Math.Clamp(percent, 0, 100);
        if (wanted == MusicVolume && _loaded)
            return;
        Load();
        MusicVolume = wanted;
        Apply();
        Save();
    }
}
