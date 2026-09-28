using Lumina.Excel.Sheets;

namespace Chaperone;

public sealed class WorldResolver
{
    public bool TryResolve(string worldName, out uint worldId, out string canonicalName)
    {
        foreach (var row in Services.DataManager.GetExcelSheet<World>())
        {
            if (!row.IsPublic)
            {
                continue;
            }

            var name = row.Name.ExtractText();
            if (name.Equals(worldName, StringComparison.OrdinalIgnoreCase))
            {
                worldId = row.RowId;
                canonicalName = name;
                return true;
            }
        }

        worldId = 0;
        canonicalName = "";
        return false;
    }

    public string WorldName(uint worldId) =>
        Services.DataManager.GetExcelSheet<World>().GetRowOrDefault(worldId)?.Name.ExtractText() ?? $"#{worldId}";

    public uint CurrentWorldId() => Services.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0;

    public string CurrentWorldName()
    {
        var id = this.CurrentWorldId();
        return id == 0 ? "" : this.WorldName(id);
    }
}
