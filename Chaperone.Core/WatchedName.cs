namespace Chaperone.Core;

public readonly record struct WatchedName(string Name, string World)
{
    public static bool TryParse(string input, string fallbackWorld, out WatchedName result)
    {
        result = default;
        var parts = input.Split('@');
        if (parts.Length > 2)
        {
            return false;
        }

        var nameTokens = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (nameTokens.Length != 2)
        {
            return false;
        }

        var world = (parts.Length == 2 ? parts[1] : fallbackWorld).Trim();
        if (world.Length == 0)
        {
            return false;
        }

        result = new WatchedName($"{nameTokens[0]} {nameTokens[1]}", world);
        return true;
    }
}
