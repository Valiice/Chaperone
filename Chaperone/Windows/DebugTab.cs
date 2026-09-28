using Chaperone.Core;
using Chaperone.Sensors;
using Dalamud.Bindings.ImGui;

namespace Chaperone.Windows;

/// <summary>Reads cached numbers only. Nothing here touches the object table.</summary>
public sealed class DebugTab
{
    private readonly ObjectTableSampler sampler;
    private readonly ChaperoneEngine engine;

    public DebugTab(ObjectTableSampler sampler, ChaperoneEngine engine)
    {
        this.sampler = sampler;
        this.engine = engine;
    }

    public void Draw()
    {
        ImGui.TextUnformatted("Sampler");
        ImGui.Separator();
        Row("State", this.sampler.Stopped ? $"stopped: {this.sampler.StopReason}" : this.sampler.GateReason);
        Row("Poll cost (total)", Cost(this.sampler.TotalStats));
        Row("Poll cost (engine)", Cost(this.engine.Stats));
        Row("Loaded players", this.engine.Frame.LoadedPlayers.ToString());
        Row("Cached names", this.sampler.NameCacheCount.ToString());
        Row("Subjects", this.engine.Frame.Subjects.Count.ToString());
        Row("Live pairs", this.engine.Frame.LivePairs.ToString());

        ImGui.Spacing();
        ImGui.TextUnformatted("Target field check");
        ImGui.Separator();
        ImGui.TextWrapped("For the player you are targeting: what the game says they are hard-targeting versus what Dalamud's look-at field says. These should usually agree.");
        Row("Hard target id", $"0x{this.sampler.DebugTargetHardId:X}");
        Row("Look-at id", $"0x{this.sampler.DebugTargetLookAtId:X}");
        Row("Mismatches this poll", this.sampler.DebugMismatchCount.ToString());
    }

    private static string Cost(PollStats stats) =>
        $"{stats.Last:0} us last, {stats.AverageLastSecond:0} us avg, {stats.MaxLastSecond:0} us max (1 s)";

    private static void Row(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine(180);
        ImGui.TextWrapped(value);
    }
}
