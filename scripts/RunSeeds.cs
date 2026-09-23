using System;
using System.Globalization;
using System.Text;

namespace BnbGodot;

// WHERE A NEW RUN'S SEED COMES FROM. A seed is the whole of a run's luck: the same seed, character and map
// generator give the same maps, the same rewards and the same shops, so a seed is something players can SHARE —
// "try 48213, the second elite is brutal" — and it is what makes a daily run the same run for everybody.
//
// Typed text is a seed too: digits are read as the number they are, anything else ("dragon") is hashed, so a
// word a friend sends works exactly like a number does. The hash is FNV-1a, spelled out here rather than
// string.GetHashCode, which .NET randomises per process — a seed that means something different every time the
// game starts is not a seed.
public static class RunSeeds
{
    public static int Random() => (int)(Godot.Time.GetUnixTimeFromSystem() % int.MaxValue);

    // Null for an empty field (the run gets a random seed).
    public static int? Parse(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number <= int.MaxValue)
            return (int)number;
        return Hash(trimmed);
    }

    // Today's run, the same for every player: the UTC date, so it turns over at the same moment everywhere.
    public static int Daily(DateTime utcNow) => Hash("bnb-daily-" + utcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    public static string DailyLabel(DateTime utcNow) => utcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static int Hash(string text)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return (int)(hash & int.MaxValue);
    }
}
