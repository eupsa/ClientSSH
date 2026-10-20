namespace ClientSSH.Services.Localization;

/// <summary>"5 min ago" style text in the current language.</summary>
public static class RelativeTime
{
    public static string Describe(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 1) return Loc.Get("Time_JustNow");
        if (span.TotalHours < 1) return string.Format(Loc.Get("Time_Minutes"), (int)span.TotalMinutes);
        if (span.TotalDays < 1) return string.Format(Loc.Get("Time_Hours"), (int)span.TotalHours);
        if (span.TotalDays < 30) return string.Format(Loc.Get("Time_Days"), (int)span.TotalDays);
        return utc.ToLocalTime().ToString("d");
    }
}
