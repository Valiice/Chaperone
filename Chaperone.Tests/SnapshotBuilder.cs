using Chaperone.Core;

namespace Chaperone.Tests;

/// <summary>Builds snapshots by hand. Names are literals so steady-state polls allocate nothing.</summary>
public static class SnapshotBuilder
{
    public static Snapshot New(long nowMs, ulong localId = 1, ulong localTarget = 0)
    {
        var snap = new Snapshot();
        Fill(snap, nowMs, localId, localTarget);
        return snap;
    }

    public static void Fill(Snapshot snap, long nowMs, ulong localId = 1, ulong localTarget = 0)
    {
        snap.Clear(nowMs);
        snap.LocalGameObjectId = localId;
        snap.LocalTargetId = localTarget;
        Add(snap, localId, "Local Player", 1, target: localTarget, contentId: 1000, flags: ObservationFlags.LocalPlayer);
    }

    public static void Add(Snapshot snap, ulong id, string name, uint world, ulong target = 0, ulong contentId = 0, ObservationFlags flags = ObservationFlags.None, ulong lookAt = 0)
    {
        ref var o = ref snap.Add();
        o.GameObjectId = id;
        o.ObjectIndex = (int)id * 2;
        o.Name = name;
        o.HomeWorldId = world;
        o.ContentId = contentId;
        o.HardTargetId = target;
        o.LookAtTargetId = lookAt;
        o.Flags = flags;
    }

    public static WatchlistEntry Entry(string name, uint world, ulong contentId = 0) =>
        new() { Name = name, World = $"W{world}", WorldId = world, ContentId = contentId };
}
