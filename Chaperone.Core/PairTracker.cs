namespace Chaperone.Core;

/// <summary>
/// Tracks live (targeter, subject) pairs and their dwell. A pair survives a gap shorter than the
/// grace period so target flicker does not reset it. No allocations in steady state: states are
/// pooled and the scratch list is reused.
/// </summary>
public sealed class PairTracker
{
    private readonly Dictionary<PairKey, PairState> live = new();
    private readonly Stack<PairState> pool = new();
    private readonly List<PairKey> scratch = new();

    public IReadOnlyDictionary<PairKey, PairState> Live => this.live;

    public void BeginPoll()
    {
        foreach (var state in this.live.Values)
        {
            state.SeenThisPoll = false;
        }
    }

    /// <summary>
    /// Records a sighting of the pair. An existing pair last seen more than <paramref name="restartGapMs"/>
    /// ago (polling was gated by GPose, a cutscene or PvP, so <see cref="EndPoll"/> never ran) is
    /// restarted in place instead of being credited the whole gap as dwell. The caller keeps the
    /// gap above a few poll intervals so a small grace period never restarts consecutive sightings.
    /// </summary>
    public PairState Observe(PairKey key, long nowMs, long restartGapMs)
    {
        if (this.live.TryGetValue(key, out var state))
        {
            if (nowMs - state.LastSeenMs > restartGapMs)
            {
                state.Reset(key, nowMs);
                return state;
            }

            state.DwellSeconds += (nowMs - state.LastSeenMs) / 1000d;
            state.LastSeenMs = nowMs;
            state.SeenThisPoll = true;
            return state;
        }

        state = this.pool.Count > 0 ? this.pool.Pop() : new PairState();
        state.Reset(key, nowMs);
        this.live[key] = state;
        return state;
    }

    /// <summary>Ends every pair not seen for longer than the grace period. Appends them to <paramref name="ended"/>.</summary>
    public void EndPoll(long nowMs, long graceMs, List<PairState> ended)
    {
        this.scratch.Clear();
        foreach (var (key, state) in this.live)
        {
            if (!state.SeenThisPoll && nowMs - state.LastSeenMs > graceMs)
            {
                this.scratch.Add(key);
            }
        }

        this.RemoveScratch(ended);
    }

    /// <summary>Ends every pair whose subject matches, immediately.</summary>
    public void EndSubject(PlayerKey subject, List<PairState> ended)
    {
        this.scratch.Clear();
        foreach (var (key, _) in this.live)
        {
            if (key.Subject == subject)
            {
                this.scratch.Add(key);
            }
        }

        this.RemoveScratch(ended);
    }

    /// <summary>
    /// Re-keys every pair of <paramref name="from"/> to <paramref name="to"/>, keeping dwell and alert
    /// state. A pair that already exists under the new key wins; the old one goes to <paramref name="ended"/>.
    /// </summary>
    public void RenameSubject(PlayerKey from, PlayerKey to, List<PairState> ended)
    {
        this.scratch.Clear();
        foreach (var (key, _) in this.live)
        {
            if (key.Subject == from)
            {
                this.scratch.Add(key);
            }
        }

        foreach (var key in this.scratch)
        {
            if (!this.live.Remove(key, out var state))
            {
                continue;
            }

            var renamed = new PairKey(key.Targeter, to);
            if (this.live.ContainsKey(renamed))
            {
                ended.Add(state);
                continue;
            }

            state.Key = renamed;
            this.live[renamed] = state;
        }
    }

    public void Release(PairState state) => this.pool.Push(state);

    public void Clear()
    {
        foreach (var state in this.live.Values)
        {
            this.pool.Push(state);
        }

        this.live.Clear();
    }

    private void RemoveScratch(List<PairState> ended)
    {
        foreach (var key in this.scratch)
        {
            if (this.live.Remove(key, out var state))
            {
                ended.Add(state);
            }
        }
    }
}
