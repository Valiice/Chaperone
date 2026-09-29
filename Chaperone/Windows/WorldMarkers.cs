using System.Numerics;
using Chaperone.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;

namespace Chaperone.Windows;

/// <summary>Circles on the background draw list at each live targeter and each nearby watched friend. Off by default.</summary>
public sealed class WorldMarkers : IDisposable
{
    private static readonly ConditionFlag[] CutsceneConditions =
    [
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
        ConditionFlag.OccupiedInCutSceneEvent,
    ];

    private readonly Configuration config;
    private readonly ChaperoneEngine engine;

    public WorldMarkers(Configuration config, ChaperoneEngine engine)
    {
        this.config = config;
        this.engine = engine;
        Services.PluginInterface.UiBuilder.Draw += this.Draw;
    }

    public void Dispose() => Services.PluginInterface.UiBuilder.Draw -= this.Draw;

    private void Draw()
    {
        if (!this.config.MarkTargeters && !this.config.MarkFriends)
        {
            return;
        }

        if (!Services.ClientState.IsLoggedIn || (this.config.PauseInPvp && Services.ClientState.IsPvP))
        {
            return;
        }

        if (Services.ClientState.IsGPosing || Services.Condition.Any(CutsceneConditions))
        {
            return;
        }

        var subjects = this.engine.Frame.Subjects;
        if (subjects.Count == 0)
        {
            return;
        }

        // The background draw list already carries the main viewport clip rect for the frame,
        // so nothing is pushed here: a push without a guaranteed pop would leak into other plugins.
        var drawList = ImGui.GetBackgroundDrawList();
        var friendColour = ImGui.GetColorU32(this.config.FriendColour);
        var targeterColour = ImGui.GetColorU32(this.config.TargeterColour);
        for (var s = 0; s < subjects.Count; s++)
        {
            var view = subjects[s];
            if (this.config.MarkFriends && view.IsWatched && view.IsNearby)
            {
                Mark(drawList, ObjectLookup.Resolve(view.ObjectIndex, view.GameObjectId), friendColour, this.config.FriendSize);
            }

            if (!this.config.MarkTargeters)
            {
                continue;
            }

            for (var r = 0; r < view.Live.Count; r++)
            {
                var row = view.Live[r];
                Mark(drawList, ObjectLookup.Resolve(row.ObjectIndex, row.GameObjectId), targeterColour, this.config.TargeterSize);
            }
        }
    }

    private static void Mark(ImDrawListPtr drawList, IGameObject? obj, uint colour, float size)
    {
        if (obj == null || !Services.GameGui.WorldToScreen(obj.Position, out var screen))
        {
            return;
        }

        drawList.AddCircleFilled(screen, size, colour, 32);
    }
}
