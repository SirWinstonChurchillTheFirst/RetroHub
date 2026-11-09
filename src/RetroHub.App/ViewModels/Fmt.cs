using RetroHub.App.Localization;
using RetroHub.Core.Catalog;

namespace RetroHub.App.ViewModels;

/// <summary>User-facing formatting of times, sizes and enum values (all through <see cref="Loc"/>).</summary>
public static class Fmt
{
    public static string PlayTime(long seconds)
    {
        if (seconds <= 0) return Loc.T("time.never");
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalMinutes < 1) return Loc.T("time.lessThanMinute");
        if (t.TotalHours < 1) return Loc.T("time.minutes", (int)t.TotalMinutes);
        if (t.Minutes == 0) return Loc.T("time.hours", (int)t.TotalHours);
        return Loc.T("time.hoursMinutes", (int)t.TotalHours, t.Minutes);
    }

    /// <summary>Compact total for stat tiles ("128 Std.").</summary>
    public static string PlayTimeShort(long seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (t.TotalHours >= 1) return Loc.T("time.hoursShort", (int)t.TotalHours);
        return Loc.T("time.minutesShort", (int)t.TotalMinutes);
    }

    public static string LastPlayed(DateTime? utc, DateTime? nowUtc = null)
    {
        if (utc == null) return Loc.T("time.neverPlayed");
        var now = (nowUtc ?? DateTime.UtcNow).ToLocalTime();
        var local = utc.Value.ToLocalTime();
        var days = (now.Date - local.Date).Days;
        if (days <= 0)
        {
            var diff = now - local;
            if (diff.TotalMinutes < 2) return Loc.T("time.justNow");
            if (diff.TotalHours < 1) return Loc.T("time.minutesAgo", (int)diff.TotalMinutes);
            return Loc.T("time.today");
        }
        if (days == 1) return Loc.T("time.yesterday");
        if (days < 7) return Loc.T("time.daysAgo", days);
        if (days < 30) return Loc.T("time.weeksAgo", days / 7);
        return local.ToString("d", Loc.Culture);
    }

    public static string Date(DateTime? utc) => utc == null ? "–" : utc.Value.ToLocalTime().ToString("d", Loc.Culture);

    public static string DateTimeText(DateTime? utc) => utc == null ? "–" : utc.Value.ToLocalTime().ToString("g", Loc.Culture);

    public static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : v.ToString(v >= 100 ? "0" : "0.0", Loc.Culture) + " " + units[u];
    }

    public static string Mode(LaunchMode mode) => mode switch
    {
        LaunchMode.Embedded => Loc.T("mode.embedded"),
        LaunchMode.Fullscreen => Loc.T("mode.fullscreen"),
        _ => Loc.T("mode.windowed"),
    };

    /// <summary>The mode inside a sentence: "Startet mit mGBA im Vollbild."</summary>
    public static string ModePhrase(LaunchMode mode) => mode switch
    {
        LaunchMode.Embedded => Loc.T("mode.embedded.phrase"),
        LaunchMode.Fullscreen => Loc.T("mode.fullscreen.phrase"),
        _ => Loc.T("mode.windowed.phrase"),
    };

    public static string ModeHint(LaunchMode mode) => mode switch
    {
        LaunchMode.Embedded => Loc.T("mode.embedded.hint"),
        LaunchMode.Fullscreen => Loc.T("mode.fullscreen.hint"),
        _ => Loc.T("mode.windowed.hint"),
    };

    public static string Category(SystemCategory c) => c switch
    {
        SystemCategory.Handheld => Loc.T("category.handheld"),
        SystemCategory.Arcade => Loc.T("category.arcade"),
        SystemCategory.Computer => Loc.T("category.computer"),
        SystemCategory.Pc => Loc.T("category.pc"),
        _ => Loc.T("category.console"),
    };

    /// <summary>The first letter in upper case – for a part that starts a sentence ("unter 1 Min." → "Unter 1 Min.").</summary>
    public static string SentenceStart(string text) =>
        string.IsNullOrEmpty(text) || !char.IsLower(text[0]) ? text : char.ToUpper(text[0], Loc.Culture) + text[1..];

    /// <summary>
    /// A region tag as a word for the meta sentence ("EUR" → "Europa"); null when the value is not a region (then it
    /// is a language like "Englisch" or "Deutsch, PAL", which the sentence only mentions beside other facts).
    /// </summary>
    public static string? RegionWord(string? region)
    {
        if (string.IsNullOrWhiteSpace(region)) return null;
        var key = region.Trim().TrimEnd('.').ToUpperInvariant();
        return key switch
        {
            "EUR" or "EU" or "EUROPE" or "EUROPA" or "PAL" => Loc.T("region.europe"),
            "USA" or "US" or "NTSC-U" or "NA" or "NORTH AMERICA" => Loc.T("region.usa"),
            "JPN" or "JP" or "JAP" or "JAPAN" or "NTSC-J" => Loc.T("region.japan"),
            "GER" or "DE" or "GERMANY" or "DEUTSCHLAND" => Loc.T("region.germany"),
            "WORLD" or "WLD" or "WELT" => Loc.T("region.world"),
            "USA, EUROPE" or "USA, EUR" or "EUR, USA" => Loc.T("region.usaEurope"),
            "KOR" or "KOREA" => Loc.T("region.korea"),
            _ => null,
        };
    }

    /// <summary>Up to two initials of a title ("The Legend of Zelda" → "LZ", "Tetris" → "TE").</summary>
    public static string Initials(string title)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "the", "of", "and", "und", "der", "die", "das", "a", "an" };
        var words = title.Split([' ', ':', '-', '.', '\'', '&'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0])).ToList();
        var main = words.Where(w => !skip.Contains(w)).ToList();
        if (main.Count == 0) main = words;
        if (main.Count == 0) return "?";
        if (main.Count == 1)
        {
            var w = main[0];
            return (w.Length >= 2 ? w[..2] : w).ToUpperInvariant();
        }
        return string.Concat(char.ToUpperInvariant(main[0][0]), char.ToUpperInvariant(main[1][0]));
    }
}
