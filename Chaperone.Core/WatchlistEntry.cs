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

    public bool Matches(string name, uint worldId, ulong contentId)
    {
        if (contentId != 0 && this.ContentId != 0 && contentId == this.ContentId)
        {
            return true;
        }

        return worldId == this.WorldId && string.Equals(name, this.Name, StringComparison.OrdinalIgnoreCase);
    }
}
