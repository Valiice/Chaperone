namespace Chaperone.Core;

/// <summary>Decides whether a live pair earns an alert right now. Pure apart from the global floor timestamp.</summary>
public sealed class AlertPolicy
{
    private long lastAlertMs = long.MinValue;

    public static bool PassesFilters(PairState pair, PolicySettings s)
    {
        if (s.IgnoreParty && pair.Flags.HasFlag(ObservationFlags.Party)) return false;
        if (s.IgnoreAlliance && pair.Flags.HasFlag(ObservationFlags.Alliance)) return false;
        if (s.IgnoreFriends && pair.Flags.HasFlag(ObservationFlags.Friend)) return false;
        if (s.IgnoreFreeCompany && pair.Flags.HasFlag(ObservationFlags.FreeCompany)) return false;
        if (s.IgnoreWatched && pair.TargeterIsWatched) return false;
        if (!s.AlertInCombat && pair.Flags.HasFlag(ObservationFlags.InCombat)) return false;
        return true;
    }

    public bool ShouldAlert(PairState pair, PolicySettings s, long nowMs)
    {
        if (pair.DwellSeconds < s.DwellThresholdSeconds) return false;
        if (!PassesFilters(pair, s)) return false;
        if (pair.Alerted && nowMs - pair.LastAlertMs < s.PairCooldownSeconds * 1000L) return false;
        if (this.lastAlertMs != long.MinValue && nowMs - this.lastAlertMs < s.GlobalFloorSeconds * 1000L) return false;
        return true;
    }

    public void MarkAlerted(PairState pair, long nowMs)
    {
        pair.Alerted = true;
        pair.LastAlertMs = nowMs;
        this.lastAlertMs = nowMs;
    }

    public void Reset() => this.lastAlertMs = long.MinValue;
}
