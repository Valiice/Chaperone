using Dalamud.Game.ClientState.Objects.Types;

namespace Chaperone.Windows;

public static class ObjectLookup
{
    /// <summary>O(1) by slot when the hint is fresh, otherwise a linear search. Null if the object is gone.</summary>
    public static IGameObject? Resolve(int objectIndex, ulong gameObjectId)
    {
        if (gameObjectId == 0)
        {
            return null;
        }

        if (objectIndex >= 0)
        {
            var bySlot = Services.ObjectTable[objectIndex];
            if (bySlot != null && bySlot.GameObjectId == gameObjectId)
            {
                return bySlot;
            }
        }

        return Services.ObjectTable.SearchById(gameObjectId);
    }
}
