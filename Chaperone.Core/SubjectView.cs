namespace Chaperone.Core;

/// <summary>One subject (watched friend or current target) as the window draws it. Persistent per key; lists are refilled each poll.</summary>
public sealed class SubjectView
{
    public PlayerKey Key;
    public string Name = "";
    public uint WorldId;
    public ulong GameObjectId;
    public int ObjectIndex;
    public bool IsCurrentTarget;
    public bool IsNearby;
    public bool IsWatched;
    public readonly List<TargeterRow> Live = new();
    public IReadOnlyList<HistoryEntry> History = [];
}
