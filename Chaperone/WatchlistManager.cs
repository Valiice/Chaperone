using Chaperone.Core;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace Chaperone;

/// <summary>Every way the watchlist changes goes through here: command, context menu, settings tab.</summary>
public sealed class WatchlistManager
{
    private readonly Configuration config;
    private readonly WorldResolver worlds;
    private readonly ChaperoneEngine engine;

    public WatchlistManager(Configuration config, WorldResolver worlds, ChaperoneEngine engine)
    {
        this.config = config;
        this.worlds = worlds;
        this.engine = engine;
    }

    public IReadOnlyList<WatchlistEntry> Entries => this.config.Watchlist;

    /// <summary>Parses "First Last@World" (world optional, defaults to the current world). Returns an error line or null.</summary>
    public string? Add(string input)
    {
        if (!WatchedName.TryParse(input, this.worlds.CurrentWorldName(), out var parsed))
        {
            return "Chaperone: use First Last@World.";
        }

        if (!this.worlds.TryResolve(parsed.World, out var worldId, out var canonical))
        {
            return $"Chaperone: unknown world '{parsed.World}'.";
        }

        return this.AddResolved(parsed.Name, canonical, worldId);
    }

    public string? AddResolved(string name, string world, uint worldId)
    {
        lock (this.config.WatchlistLock)
        {
            if (this.Contains(name, worldId))
            {
                return $"Chaperone: {name}@{world} is already on the list.";
            }

            this.config.Watchlist.Add(new WatchlistEntry { Name = name, World = world, WorldId = worldId });
        }

        this.config.Save();
        return null;
    }

    public string? AddCurrentTarget()
    {
        var target = Services.TargetManager.SoftTarget ?? Services.TargetManager.Target;
        if (target is not IPlayerCharacter pc)
        {
            return "Chaperone: target a player first.";
        }

        var worldId = pc.HomeWorld.RowId;
        return this.AddResolved(pc.Name.TextValue, this.worlds.WorldName(worldId), worldId);
    }

    public string? Remove(string input)
    {
        if (!WatchedName.TryParse(input, this.worlds.CurrentWorldName(), out var parsed))
        {
            return "Chaperone: use First Last@World.";
        }

        this.worlds.TryResolve(parsed.World, out var worldId, out _);
        lock (this.config.WatchlistLock)
        {
            for (var i = 0; i < this.config.Watchlist.Count; i++)
            {
                var entry = this.config.Watchlist[i];
                if (entry.WorldId == worldId && string.Equals(entry.Name, parsed.Name, StringComparison.OrdinalIgnoreCase))
                {
                    this.RemoveAtLocked(i);
                    this.config.Save();
                    return null;
                }
            }
        }

        return $"Chaperone: {parsed.Name}@{parsed.World} is not on the list.";
    }

    public void RemoveAt(int index)
    {
        lock (this.config.WatchlistLock)
        {
            if (index < 0 || index >= this.config.Watchlist.Count)
            {
                return;
            }

            this.RemoveAtLocked(index);
        }

        this.config.Save();
    }

    public bool Contains(string name, uint worldId)
    {
        for (var i = 0; i < this.config.Watchlist.Count; i++)
        {
            if (this.config.Watchlist[i].Matches(name, worldId, 0))
            {
                return true;
            }
        }

        return false;
    }

    private void RemoveAtLocked(int index)
    {
        var entry = this.config.Watchlist[index];
        this.config.Watchlist.RemoveAt(index);
        this.engine.ForgetSubject(entry.Key);
    }
}
