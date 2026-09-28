namespace Chaperone.Core;

/// <summary>Alert decisions. Mirrors the Alerts and Filters settings tabs; the plugin copies its config into this each poll.</summary>
public sealed class PolicySettings
{
    public double DwellThresholdSeconds { get; set; } = 3;
    public int PairCooldownSeconds { get; set; } = 60;
    public int GlobalFloorSeconds { get; set; } = 2;
    public bool IgnoreParty { get; set; } = true;
    public bool IgnoreAlliance { get; set; } = true;
    public bool IgnoreFriends { get; set; } = true;
    public bool IgnoreFreeCompany { get; set; } = true;
    public bool IgnoreWatched { get; set; } = true;
    public bool AlertInCombat { get; set; }
    public bool LogSelf { get; set; }
}
