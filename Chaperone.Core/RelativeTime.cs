namespace Chaperone.Core;

/// <summary>Short, culture-free time strings for the window: "12s", "2m 5s", "1h 2m", "3m ago".</summary>
public static class RelativeTime
{
    public static string Ago(long elapsedMs)
    {
        var seconds = Math.Max(0, elapsedMs) / 1000;
        if (seconds < 60)
        {
            return $"{seconds}s ago";
        }

        var minutes = seconds / 60;
        if (minutes < 60)
        {
            return $"{minutes}m ago";
        }

        return $"{minutes / 60}h ago";
    }

    public static string Dwell(double dwellSeconds)
    {
        var total = (long)Math.Round(Math.Max(0, dwellSeconds));
        if (total < 60)
        {
            return $"{total}s";
        }

        var minutes = total / 60;
        if (minutes < 60)
        {
            return $"{minutes}m {total % 60}s";
        }

        return $"{minutes / 60}h {minutes % 60}m";
    }
}
