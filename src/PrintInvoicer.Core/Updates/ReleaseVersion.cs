namespace PrintInvoicer.Core.Updates;

/// <summary>Version numbers from release tags, and when the startup update check is due.</summary>
public static class ReleaseVersion
{
    /// <summary>How often the startup check asks GitHub; launches in between reuse the last answer.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    /// <summary>Reads "v3.2.0", "3.2" or "3.2.0+abc123" as a version; null when it is not one.</summary>
    public static Version? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string s = tag.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(['+', '-', ' ']);
        if (cut >= 0) s = s[..cut];
        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    /// <summary>Major.minor.patch only, so 3.2.0 (a tag) and 3.2.0.0 (an assembly) compare as equal.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>True when <paramref name="latest"/> is a later version than <paramref name="current"/>.</summary>
    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    /// <summary>True when the last check was a day or more ago, never happened, or is in the future (a clock change).</summary>
    public static bool IsDue(DateTime? lastCheckUtc, DateTime nowUtc) =>
        lastCheckUtc is not DateTime last || last > nowUtc || nowUtc - last >= CheckInterval;

    /// <summary>A version as shown to users, e.g. "3.6.0".</summary>
    public static string Display(Version v) => Normalize(v).ToString(3);
}
