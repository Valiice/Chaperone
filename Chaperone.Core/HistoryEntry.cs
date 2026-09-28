namespace Chaperone.Core;

public readonly record struct HistoryEntry(PlayerKey Targeter, string Name, uint WorldId, ulong LastGameObjectId, long LastSeenMs, double DwellSeconds);
