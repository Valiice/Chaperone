namespace Chaperone.Core;

/// <summary>What the last poll produced. The window reads this and nothing else.</summary>
public sealed class Frame
{
    public readonly List<SubjectView> Subjects = new();
    public readonly List<AlertEvent> Alerts = new();
    public int LoadedPlayers;
    public int LivePairs;
    public long NowMs;

    /// <summary>True when a watched entry learned its content id this poll; the plugin saves config.</summary>
    public bool WatchlistChanged;
}
