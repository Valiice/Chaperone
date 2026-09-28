namespace Chaperone.Core;

/// <summary>Mutable, pooled state of one (targeter, subject) pair. Owned by <see cref="PairTracker"/>.</summary>
public sealed class PairState
{
    public PairKey Key;
    public string TargeterName = "";
    public uint TargeterWorldId;
    public ulong TargeterGameObjectId;
    public ObservationFlags Flags;
    public bool TargeterIsWatched;
    public long StartedMs;
    public long LastSeenMs;
    public double DwellSeconds;
    public bool Alerted;
    public long LastAlertMs;
    public bool SeenThisPoll;

    internal void Reset(PairKey key, long nowMs)
    {
        this.Key = key;
        this.TargeterName = "";
        this.TargeterWorldId = 0;
        this.TargeterGameObjectId = 0;
        this.Flags = ObservationFlags.None;
        this.TargeterIsWatched = false;
        this.StartedMs = nowMs;
        this.LastSeenMs = nowMs;
        this.DwellSeconds = 0;
        this.Alerted = false;
        this.LastAlertMs = long.MinValue;
        this.SeenThisPoll = true;
    }
}
