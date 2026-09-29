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

    // Last alert time per pair, kept after the pair itself ends so a targeter who drops target
    // briefly and comes back still honours the pair cooldown. Pruned once the cooldown has passed.
    private readonly Dictionary<PairKey, long> lastAlertByPair = new();
    private readonly List<PairKey> pruneScratch = new();
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

        this.ClearLiveRows();
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
        this.lastAlertByPair.Clear();
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
        this.history.Forget(subject);
        this.viewsByKey.Remove(subject);
    }

    /// <summary>
    /// Clears every known view's row list, not just this poll's Frame.Subjects. A watchlist entry
    /// disabled while its pairs are still live is skipped by ResolveWatchedSubjects and so never
    /// reaches its own Live.Clear() below; without this, FillRows would keep appending rows to
    /// that orphaned view every poll until it grows without bound.
    /// </summary>
    private void ClearLiveRows()
    {
        foreach (var view in this.viewsByKey.Values)
        {
            view.Live.Clear();
        }
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
                    // Carry pairs, history and cooldowns across. The old name key may be the current
                    // target's transient key (the target was just added by name); the player is now
                    // watched, so the transient subject simply stops, with nothing torn down.
                    var oldKey = key;
                    entry.ContentId = o.ContentId;
                    this.Frame.WatchlistChanged = true;
                    key = entry.Key;
                    this.RenameSubject(oldKey, key);
                    this.viewsByKey.Remove(oldKey);
                    this.viewsByKey[key] = view;
                    view.Key = key;
                    if (this.CurrentTargetKey == oldKey)
                    {
                        this.CurrentTargetKey = PlayerKey.None;
                    }
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
            // Keyed by name, not content id: a content id that reads 0 on first sight and non-zero
            // a poll later must not look like a retarget and drop the subject's rows.
            targetKey = PlayerKey.FromName(o.Name, o.HomeWorldId);
        }

        if (targetKey != this.CurrentTargetKey)
        {
            if (!this.CurrentTargetKey.IsNone)
            {
                // A watched entry may have claimed this key this poll (the player was just added
                // to the watchlist, or the watchlist entry just learned this player's content id
                // and it happens to match). Its live pairs and history are not ours to end, and
                // its view (found under this same key) must not be removed.
                if (!this.watchedKeys.Contains(this.CurrentTargetKey))
                {
                    this.tracker.EndSubject(this.CurrentTargetKey, this.ended);
                    this.ReleaseEnded();
                    this.history.Forget(this.CurrentTargetKey);
                }
                else if (this.subjectsByObject.TryGetValue(this.transientView.GameObjectId, out var claimed)
                    && claimed.Key != this.CurrentTargetKey
                    && PlayerKey.FromName(claimed.Name, claimed.WorldId) == this.CurrentTargetKey)
                {
                    // The transient subject is keyed by name but the watched entry that just claimed
                    // it is keyed by content id: carry its pairs, history and cooldowns across.
                    this.RenameSubject(this.CurrentTargetKey, claimed.Key);
                }

                if (this.viewsByKey.TryGetValue(this.CurrentTargetKey, out var oldView) && ReferenceEquals(oldView, this.transientView))
                {
                    this.viewsByKey.Remove(this.CurrentTargetKey);
                }
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

        // Independent of how small the grace is: a grace at or below the poll interval must not
        // restart a pair on every consecutive sighting.
        var restartGapMs = Math.Max(this.Settings.GraceMs, 3L * this.Settings.PollIntervalMs);

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
            var pair = this.tracker.Observe(new PairKey(targeterKey, view.Key), now, restartGapMs);
            pair.TargeterName = o.Name;
            pair.TargeterWorldId = o.HomeWorldId;
            pair.TargeterGameObjectId = o.GameObjectId;
            pair.Flags = o.Flags;
            pair.TargeterIsWatched = this.watchedKeys.Contains(targeterKey);

            if (pair.StartedMs == now)
            {
                // A returning targeter leaves history and becomes live again.
                this.history.Remove(view.Key, targeterKey);

                // The pair may have ended and restarted within its cooldown; carry the last alert over.
                if (this.lastAlertByPair.TryGetValue(pair.Key, out var lastAlertMs))
                {
                    pair.Alerted = true;
                    pair.LastAlertMs = lastAlertMs;
                }
            }

            if (this.policy.ShouldAlert(pair, settings, now))
            {
                this.policy.MarkAlerted(pair, now);
                this.lastAlertByPair[pair.Key] = now;
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
        this.PruneCooldowns(now);
    }

    private void PruneCooldowns(long now)
    {
        if (this.lastAlertByPair.Count == 0)
        {
            return;
        }

        var cooldownMs = this.Settings.Policy.PairCooldownSeconds * 1000L;
        this.pruneScratch.Clear();
        foreach (var (key, alertedMs) in this.lastAlertByPair)
        {
            if (now - alertedMs >= cooldownMs)
            {
                this.pruneScratch.Add(key);
            }
        }

        for (var i = 0; i < this.pruneScratch.Count; i++)
        {
            this.lastAlertByPair.Remove(this.pruneScratch[i]);
        }
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
                }

                // Mutual: the subject's own hard target is this targeter.
                row.Mutual = this.byGameObjectId.TryGetValue(view.GameObjectId, out var subjectIdx)
                    && this.lastSnapshot!.Players[subjectIdx].HardTargetId == row.GameObjectId;
                view.Live[r] = row;
            }
        }
    }

    private void RenameSubject(PlayerKey from, PlayerKey to)
    {
        this.tracker.RenameSubject(from, to, this.ended);
        this.ReleaseEnded();
        this.history.Move(from, to);

        this.pruneScratch.Clear();
        foreach (var (key, _) in this.lastAlertByPair)
        {
            if (key.Subject == from)
            {
                this.pruneScratch.Add(key);
            }
        }

        for (var i = 0; i < this.pruneScratch.Count; i++)
        {
            var key = this.pruneScratch[i];
            if (this.lastAlertByPair.Remove(key, out var alertedMs))
            {
                this.lastAlertByPair.TryAdd(new PairKey(key.Targeter, to), alertedMs);
            }
        }
    }

    private SubjectView GetView(PlayerKey key)
    {
        // The shared transientView must never become a watched entry's own view: it is mutated
        // and reused for whatever the current unwatched target is next poll, and ResolveCurrentTarget
        // identifies "still the transient view" by reference. A watched entry that found it under
        // its key (the player was the current target and just got added to the watchlist, or vice
        // versa) gets a fresh, permanent view instead.
        if (!this.viewsByKey.TryGetValue(key, out var view) || ReferenceEquals(view, this.transientView))
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
