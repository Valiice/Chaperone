namespace Chaperone.Core;

/// <summary>
/// One loaded player as seen by one poll. Plain data: the sampler fills it, Core reads it.
/// <see cref="Name"/> is a cached string owned by the sampler's name cache, so copying this
/// struct allocates nothing.
/// </summary>
public struct PlayerObservation
{
    public ulong GameObjectId;

    /// <summary>Slot in the object table, so the UI can resolve the object in O(1) and verify by id.</summary>
    public int ObjectIndex;

    public ulong ContentId;
    public string Name;
    public uint HomeWorldId;

    /// <summary>Server-synced hard target. 0 means none (the sampler folds 0xE0000000 into 0).</summary>
    public ulong HardTargetId;

    /// <summary>Dalamud's look-at target, debug tab only.</summary>
    public ulong LookAtTargetId;

    public ObservationFlags Flags;

    public readonly bool Has(ObservationFlags flag) => (this.Flags & flag) != 0;
}
