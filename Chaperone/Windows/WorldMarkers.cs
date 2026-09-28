using System.Numerics;
using Chaperone.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility;

namespace Chaperone.Windows;

/// <summary>Circles on the background draw list at each live targeter and each nearby watched friend. Off by default.</summary>
public sealed class WorldMarkers : IDisposable
{
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

        var subjects = this.engine.Frame.Subjects;
        for (var s = 0; s < subjects.Count; s++)
        {
            var view = subjects[s];
            if (this.config.MarkFriends && view.IsWatched && view.IsNearby)
            {
                Mark(ObjectLookup.Resolve(view.ObjectIndex, view.GameObjectId), this.config.FriendColour, this.config.FriendSize);
            }

            if (!this.config.MarkTargeters)
            {
                continue;
            }

            for (var r = 0; r < view.Live.Count; r++)
            {
                var row = view.Live[r];
                Mark(ObjectLookup.Resolve(row.ObjectIndex, row.GameObjectId), this.config.TargeterColour, this.config.TargeterSize);
            }
        }
    }

    private static void Mark(IGameObject? obj, Vector4 colour, float size)
    {
        if (obj == null || !Services.GameGui.WorldToScreen(obj.Position, out var screen))
        {
            return;
        }

        var drawList = ImGui.GetBackgroundDrawList();
        var viewport = ImGuiHelpers.MainViewport;
        drawList.PushClipRect(viewport.Pos, viewport.Pos + viewport.Size, false);
        drawList.AddCircleFilled(screen, size, ImGui.GetColorU32(colour), 32);
        drawList.PopClipRect();
    }
}
