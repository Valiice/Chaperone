using Dalamud.Game.Gui.ContextMenu;

namespace Chaperone;

public sealed class ContextMenuIntegration : IDisposable
{
    private readonly WatchlistManager watchlist;
    private readonly WorldResolver worlds;

    public ContextMenuIntegration(WatchlistManager watchlist, WorldResolver worlds)
    {
        this.watchlist = watchlist;
        this.worlds = worlds;
        Services.ContextMenu.OnMenuOpened += this.OnMenuOpened;
    }

    public void Dispose() => Services.ContextMenu.OnMenuOpened -= this.OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetDefault target)
        {
            return;
        }

        var name = target.TargetName;
        var worldId = target.TargetHomeWorld.RowId;
        if (string.IsNullOrEmpty(name) || worldId == 0)
        {
            return;
        }

        var world = this.worlds.WorldName(worldId);
        if (this.watchlist.Contains(name, worldId))
        {
            args.AddMenuItem(new MenuItem
            {
                Name = "Remove from Chaperone",
                OnClicked = _ => Services.ChatGui.Print(this.watchlist.Remove($"{name}@{world}") ?? $"Chaperone: removed {name}@{world}."),
            });
        }
        else
        {
            args.AddMenuItem(new MenuItem
            {
                Name = "Add to Chaperone",
                OnClicked = _ => Services.ChatGui.Print(this.watchlist.AddResolved(name, world, worldId) ?? $"Chaperone: now watching {name}@{world}."),
            });
        }
    }
}
