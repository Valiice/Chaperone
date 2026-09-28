namespace Chaperone.Core;

/// <summary>One live targeter row in the window. A struct so the per-subject list holds it without allocation.</summary>
public struct TargeterRow
{
    public PlayerKey Key;
    public string Name;
    public uint WorldId;
    public ulong GameObjectId;
    public int ObjectIndex;
    public double DwellSeconds;
    public long LastSeenMs;
    public bool Alerted;
    public ObservationFlags Flags;
}
