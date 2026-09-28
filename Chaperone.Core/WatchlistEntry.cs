namespace Chaperone.Core;

/// <summary>One watched friend. Serialized into the plugin configuration as-is.</summary>
public sealed class WatchlistEntry
{
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
    public uint WorldId { get; set; }

    /// <summary>0 until the friend has been seen once; then a secondary match key.</summary>
    public ulong ContentId { get; set; }

    public bool Enabled { get; set; } = true;

    public string Display => $"{this.Name}@{this.World}";

    /// <summary>Content key once seen, name key before that. This is the subject key the engine uses.</summary>
    public PlayerKey Key => this.ContentId != 0 ? PlayerKey.FromContentId(this.ContentId) : PlayerKey.FromName(this.Name, this.WorldId);

    public bool Matches(string name, uint worldId, ulong contentId)
    {
        // When both content ids are known (non-zero), use that as the definitive check
        if (contentId != 0 && this.ContentId != 0)
        {
            return contentId == this.ContentId;
        }

        // Otherwise fall back to name+world matching
        return worldId == this.WorldId && string.Equals(name, this.Name, StringComparison.OrdinalIgnoreCase);
    }
}
