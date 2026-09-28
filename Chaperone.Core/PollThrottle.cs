namespace Chaperone.Core;

/// <summary>Runs at most once per interval. Unlike PeepingTom, the clock is restarted on every run.</summary>
public sealed class PollThrottle
{
    private long lastRunMs = long.MinValue;

    public bool ShouldRun(long nowMs, int intervalMs)
    {
        if (this.lastRunMs != long.MinValue && nowMs - this.lastRunMs < intervalMs)
        {
            return false;
        }

        this.lastRunMs = nowMs;
        return true;
    }

    public void Reset() => this.lastRunMs = long.MinValue;
}
