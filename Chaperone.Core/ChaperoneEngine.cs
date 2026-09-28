using System.Diagnostics;

namespace Chaperone.Core;

/// <summary>
/// One poll: resolve subjects (watched friends plus the current target), find every player whose
/// hard target is a subject, update pair dwell, decide alerts, end stale pairs into history, and
/// rebuild the frame. No LINQ, no allocations once every key has been seen once.
/// </summary>
public sealed class ChaperoneEngine
{
    private readonly PairTracker tracker = new();
    private readonly AlertPolicy policy = new();
    private readonly HistoryBuffer history = new();
    private readonly Dictionary<ulong, int> byGameObjectId = new();
    private readonly Dictionary<ulong, int> byContentId = new();
    private readonly Dictionary<ulong, SubjectView> subjectsByObject = new();
    private readonly Dictionary<PlayerKey, SubjectView> viewsByKey = new();
    private readonly HashSet<PlayerKey> watchedKeys = new();
    private readonly List<PairState> ended = new();
    private readonly SubjectView transientView = new();

    // The sampler owns exactly one Snapshot and refills it in place, so holding the reference
    // between Poll phases is safe. FillRows reads object indexes from it.
    private Snapshot? lastSnapshot;

    public ChaperoneEngine(EngineSettings settings)
    {
        this.Settings = settings;
    }

    public EngineSettings Settings { get; }

    public Frame Frame { get; } = new();

    public PollStats Stats { get; } = new();

    public PlayerKey CurrentTargetKey { get; private set; } = PlayerKey.None;

    public void Poll(Snapshot snap, IReadOnlyList<WatchlistEntry> watchlist)
    {
        var started = Stopwatch.GetTimestamp();
        var now = snap.NowMs;
        var frame = this.Frame;
        this.lastSnapshot = snap;
        frame.Alerts.Clear();
        frame.Subjects.Clear();
        frame.WatchlistChanged = false;
        frame.NowMs = now;
        frame.LoadedPlayers = snap.Count;

        this.IndexSnapshot(snap);
        this.ResolveWatchedSubjects(snap, watchlist);
        this.ResolveCurrentTarget(snap);
        this.ObservePairs(snap, now);
        this.EndStalePairs(now);
        this.FillRows();

        frame.LivePairs = this.tracker.Live.Count;
        this.Stats.Record(now, Stopwatch.GetElapsedTime(started).TotalMicroseconds);
    }

    /// <summary>Logout: forget every pair, history entry and view.</summary>
    public void Reset()
    {
        this.tracker.Clear();
        this.history.ClearAll();
        this.policy.Reset();
        this.viewsByKey.Clear();
        this.subjectsByObject.Clear();
        this.CurrentTargetKey = PlayerKey.None;
        this.Frame.Subjects.Clear();
        this.Frame.Alerts.Clear();
        this.Frame.LivePairs = 0;
    }

    /// <summary>A friend was removed from the watchlist.</summary>
    public void ForgetSubject(PlayerKey subject)
    {
        this.tracker.EndSubject(subject, this.ended);
        this.ReleaseEnded();
        this.history.Clear(subject);
        this.viewsByKey.Remove(subject);
    }

    private void IndexSnapshot(Snapshot snap)
    {
        this.byGameObjectId.Clear();
        this.byContentId.Clear();
        for (var i = 0; i < snap.Count; i++)
        {
            ref var o = ref snap.Players[i];
            this.byGameObjectId[o.GameObjectId] = i;
            if (o.ContentId != 0)
            {
                this.byContentId[o.ContentId] = i;
            }
        }
    }

    private void ResolveWatchedSubjects(Snapshot snap, IReadOnlyList<WatchlistEntry> watchlist)
    {
        this.subjectsByObject.Clear();
        this.watchedKeys.Clear();

        for (var w = 0; w < watchlist.Count; w++)
        {
            var entry = watchlist[w];
            if (!entry.Enabled)
            {
                continue;
            }

            var key = entry.Key;
            var view = this.GetView(key);
            view.Name = entry.Name;
            view.WorldId = entry.WorldId;
            view.IsWatched = true;
            view.IsNearby = false;
            view.IsCurrentTarget = false;
            view.GameObjectId = 0;
            view.ObjectIndex = -1;
            view.Live.Clear();

            var index = this.FindEntry(snap, entry);
            if (index >= 0)
            {
                ref var o = ref snap.Players[index];
                if (entry.ContentId == 0 && o.ContentId != 0)
                {
                    // First sight with a content id: the subject key changes from name to content.
                    // Any pair keyed by the old subject is at most a few polls old; drop it.
                    entry.ContentId = o.ContentId;
                    this.Frame.WatchlistChanged = true;
                    this.tracker.EndSubject(key, this.ended);
                    this.ReleaseEnded();
                    this.history.Clear(key);
                    this.viewsByKey.Remove(key);
                    key = entry.Key;
                    this.viewsByKey[key] = view;
                    view.Key = key;
                }

                view.Name = o.Name;
                view.IsNearby = true;
                view.GameObjectId = o.GameObjectId;
                view.ObjectIndex = o.ObjectIndex;
                view.IsCurrentTarget = o.GameObjectId == snap.LocalTargetId;
                this.subjectsByObject[o.GameObjectId] = view;
            }

            // Both spellings of a watched player's key, so a watched targeter is recognised
            // whether or not their content id is known yet.
            this.watchedKeys.Add(key);
            this.watchedKeys.Add(PlayerKey.FromName(entry.Name, entry.WorldId));
            if (view.IsNearby)
            {
                this.watchedKeys.Add(PlayerKey.FromName(view.Name, view.WorldId));
            }

            this.Frame.Subjects.Add(view);
        }
    }

    private void ResolveCurrentTarget(Snapshot snap)
    {
        var targetKey = PlayerKey.None;
        if (snap.LocalTargetId != 0
            && !this.subjectsByObject.ContainsKey(snap.LocalTargetId)
            && this.byGameObjectId.TryGetValue(snap.LocalTargetId, out var index))
        {
            ref var o = ref snap.Players[index];
            targetKey = PlayerKey.From(o.ContentId, o.Name, o.HomeWorldId);
        }

        if (targetKey != this.CurrentTargetKey)
        {
            if (!this.CurrentTargetKey.IsNone)
            {
                this.tracker.EndSubject(this.CurrentTargetKey, this.ended);
                this.ReleaseEnded();
                this.history.Clear(this.CurrentTargetKey);
                this.viewsByKey.Remove(this.CurrentTargetKey);
            }

            this.CurrentTargetKey = targetKey;
        }

        if (targetKey.IsNone)
        {
            return;
        }

        ref var target = ref snap.Players[this.byGameObjectId[snap.LocalTargetId]];
        var view = this.transientView;
        view.Key = targetKey;
        view.Name = target.Name;
        view.WorldId = target.HomeWorldId;
        view.GameObjectId = target.GameObjectId;
        view.ObjectIndex = target.ObjectIndex;
        view.IsCurrentTarget = true;
        view.IsNearby = true;
        view.IsWatched = false;
        view.Live.Clear();
        this.viewsByKey[targetKey] = view;
        this.subjectsByObject[target.GameObjectId] = view;
        this.Frame.Subjects.Add(view);
    }

    private void ObservePairs(Snapshot snap, long now)
    {
        this.tracker.BeginPoll();
        var settings = this.Settings.Policy;

        for (var i = 0; i < snap.Count; i++)
        {
            ref var o = ref snap.Players[i];
            if (o.HardTargetId == 0 || !this.subjectsByObject.TryGetValue(o.HardTargetId, out var view))
            {
                continue;
            }

            if (o.GameObjectId == view.GameObjectId)
            {
                continue;
            }

            if (o.Has(ObservationFlags.LocalPlayer) && !settings.LogSelf)
            {
                continue;
            }

            var targeterKey = PlayerKey.FromName(o.Name, o.HomeWorldId);
            var pair = this.tracker.Observe(new PairKey(targeterKey, view.Key), now);
            pair.TargeterName = o.Name;
            pair.TargeterWorldId = o.HomeWorldId;
            pair.TargeterGameObjectId = o.GameObjectId;
            pair.Flags = o.Flags;
            pair.TargeterIsWatched = this.watchedKeys.Contains(targeterKey);

            if (pair.StartedMs == now)
            {
                // A returning targeter leaves history and becomes live again.
                this.history.Remove(view.Key, targeterKey);
            }

            if (this.policy.ShouldAlert(pair, settings, now))
            {
                this.policy.MarkAlerted(pair, now);
                this.Frame.Alerts.Add(new AlertEvent(
                    targeterKey, o.Name, o.HomeWorldId, view.Key, view.Name, view.WorldId, pair.DwellSeconds));
            }
        }
    }

    private void EndStalePairs(long now)
    {
        this.tracker.EndPoll(now, this.Settings.GraceMs, this.ended);
        for (var i = 0; i < this.ended.Count; i++)
        {
            var e = this.ended[i];
            this.history.Push(
                e.Key.Subject,
                new HistoryEntry(e.Key.Targeter, e.TargeterName, e.TargeterWorldId, e.TargeterGameObjectId, e.LastSeenMs, e.DwellSeconds),
                this.Settings.HistorySize);
        }

        this.ReleaseEnded();
    }

    private void FillRows()
    {
        // PairTracker.Live is IReadOnlyDictionary-typed; enumerating it through the interface
        // boxes a fresh Dictionary<>.Enumerator every poll. The concrete type behind it is
        // always a Dictionary<PairKey, PairState>, so cast back to iterate its Values with the
        // struct enumerator and avoid the allocation.
        var live = (Dictionary<PairKey, PairState>)this.tracker.Live;
        foreach (var pair in live.Values)
        {
            if (!this.viewsByKey.TryGetValue(pair.Key.Subject, out var view))
            {
                continue;
            }

            view.Live.Add(new TargeterRow
            {
                Key = pair.Key.Targeter,
                Name = pair.TargeterName,
                WorldId = pair.TargeterWorldId,
                GameObjectId = pair.TargeterGameObjectId,
                ObjectIndex = -1,
                DwellSeconds = pair.DwellSeconds,
                LastSeenMs = pair.LastSeenMs,
                Alerted = pair.Alerted,
                Flags = pair.Flags,
            });
        }

        var subjects = this.Frame.Subjects;
        for (var i = 0; i < subjects.Count; i++)
        {
            var view = subjects[i];
            view.History = this.history.For(view.Key);

            // Object indexes for rows come from the snapshot index built this poll.
            for (var r = 0; r < view.Live.Count; r++)
            {
                var row = view.Live[r];
                if (this.byGameObjectId.TryGetValue(row.GameObjectId, out var idx))
                {
                    row.ObjectIndex = this.lastSnapshot!.Players[idx].ObjectIndex;
                    view.Live[r] = row;
                }
            }
        }
    }

    private SubjectView GetView(PlayerKey key)
    {
        if (!this.viewsByKey.TryGetValue(key, out var view))
        {
            view = new SubjectView { Key = key };
            this.viewsByKey[key] = view;
        }

        return view;
    }

    private int FindEntry(Snapshot snap, WatchlistEntry entry)
    {
        if (entry.ContentId != 0 && this.byContentId.TryGetValue(entry.ContentId, out var byId))
        {
            return byId;
        }

        for (var i = 0; i < snap.Count; i++)
        {
            ref var o = ref snap.Players[i];
            if (entry.ContentId != 0 && o.ContentId != 0 && o.ContentId != entry.ContentId)
            {
                // A freed name can be reassigned to another player; a differing known content id
                // is conclusive proof this is not the watched entry.
                continue;
            }

            if (o.HomeWorldId == entry.WorldId && string.Equals(o.Name, entry.Name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void ReleaseEnded()
    {
        for (var i = 0; i < this.ended.Count; i++)
        {
            this.tracker.Release(this.ended[i]);
        }

        this.ended.Clear();
    }
}
