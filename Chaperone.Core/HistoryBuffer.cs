namespace Chaperone.Core;

/// <summary>In-memory, per-subject list of ended pairs, newest first. Lists are kept and reused per subject.</summary>
public sealed class HistoryBuffer
{
    private static readonly HistoryEntry[] Empty = [];
    private readonly Dictionary<PlayerKey, List<HistoryEntry>> bySubject = new();

    public void Push(PlayerKey subject, HistoryEntry entry, int capacity)
    {
        if (!this.bySubject.TryGetValue(subject, out var list))
        {
            list = new List<HistoryEntry>();
            this.bySubject[subject] = list;
        }

        RemoveTargeter(list, entry.Targeter);
        list.Insert(0, entry);
        while (list.Count > capacity)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    public void Remove(PlayerKey subject, PlayerKey targeter)
    {
        if (this.bySubject.TryGetValue(subject, out var list))
        {
            RemoveTargeter(list, targeter);
        }
    }

    public void Clear(PlayerKey subject)
    {
        if (this.bySubject.TryGetValue(subject, out var list))
        {
            list.Clear();
        }
    }

    /// <summary>Drops the subject entirely, unlike <see cref="Clear"/> which leaves an empty list
    /// behind. Use this for a subject that will never be looked up again, so the dictionary does
    /// not grow without bound over a session (every transient current-target, every forgotten
    /// watched friend).</summary>
    public void Forget(PlayerKey subject) => this.bySubject.Remove(subject);

    public void ClearAll()
    {
        foreach (var list in this.bySubject.Values)
        {
            list.Clear();
        }
    }

    public IReadOnlyList<HistoryEntry> For(PlayerKey subject) =>
        this.bySubject.TryGetValue(subject, out var list) ? list : Empty;

    private static void RemoveTargeter(List<HistoryEntry> list, PlayerKey targeter)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Targeter == targeter)
            {
                list.RemoveAt(i);
            }
        }
    }
}
