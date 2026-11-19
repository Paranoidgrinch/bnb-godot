using Godot;

namespace BnbGodot;

// THE WHOLE PROFILE BACK TO ZERO (user, 2026-11-19): "wir brauchen definitiv einen reset profile button der das
// spiel auf 0 zuruecksetzt". A player who installs a new version over an old one keeps the old profile, and an old
// profile can hold unlocks, owned cards and flags written by rules that no longer exist, so playing it does not
// show what a fresh player sees.
//
// Everything a player has EARNED goes:
//   • the engine's meta profile (user://metastate.json): character unlocks, tutorial-asked flags, level and XP,
//     opened boosters, owned cards and relics, shells, owned and chosen modifiers, win streaks, lock flags
//   • the archive's fund book and the Witch's Recipe Book (Archive.Reset)
//   • the shell statement and the run history
//   • the run in play and its save. Its recording is closed as abandoned first, so the upload still goes home.
//
// What STAYS: settings (window, sound, controls), the player's name (PlayerIdentity, it signs the uploads), the
// upload queue, and the encounter trials, which stand outside the progression and are balance data.
//
// ⚠ Not while a run is on screen: RunPlayback writes the profile it loaded back when the run ends (see
// ArchivePanel.Footer). The run is dropped here before the files go, so nothing is left to write it back.
public static class ProfileReset
{
    private static readonly string[] Files =
    [
        "user://run-save.json",
        "user://shell-statement.json",
    ];

    public static void EraseAll(GameHost host)
    {
        host.AbandonRun();      // closes the recording (abandoned, queued for upload) and drops the run
        Archive.Reset();        // fund book, recipe book, metastate.json
        foreach (var file in Files)
            Remove(file);
        Remove(RunHistory.Path);
        GD.Print("profile: reset to zero");
    }

    private static void Remove(string file)
    {
        if (Godot.FileAccess.FileExists(file))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(file));
    }
}
