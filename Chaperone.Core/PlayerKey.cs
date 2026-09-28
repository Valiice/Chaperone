namespace Chaperone.Core;

/// <summary>
/// Stable identity for a player across polls. Content id when the game provides it, otherwise a
/// hash of name plus home world with the top bit set so the two spaces never overlap.
/// </summary>
public readonly record struct PlayerKey(ulong Value)
{
    public static readonly PlayerKey None = new(0);

    public bool IsNone => this.Value == 0;

    public static PlayerKey FromContentId(ulong contentId) => new(contentId);

    public static PlayerKey FromName(string name, uint worldId)
    {
        // FNV-1a over the lower-cased name, then the world id, then force the top bit.
        var hash = 14695981039346656037UL;
        foreach (var ch in name)
        {
            hash ^= char.ToLowerInvariant(ch);
            hash *= 1099511628211UL;
        }

        hash ^= worldId;
        hash *= 1099511628211UL;
        return new PlayerKey(hash | 0x8000_0000_0000_0000UL);
    }

    public static PlayerKey From(ulong contentId, string name, uint worldId) =>
        contentId != 0 ? FromContentId(contentId) : FromName(name, worldId);
}
