using System.Numerics;
using Chaperone.Core;
using Chaperone.Sensors;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Chaperone.Windows;

/// <summary>
/// The compact list: one row per person targeting someone you watch (or your current target).
/// Left is the targeter, right is who they are targeting and the time it started. Ended targetings
/// stay as greyed rows with the time they stopped. Draws only from the engine's cached frame. Row
/// interactions mirror PeepingTom: hover focuses, left click links to chat, right click targets,
/// Alt+click examines.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly Vector4 Red = new(1f, 0.35f, 0.35f, 1f);

    private static readonly ConditionFlag[] DutyConditions =
    [
        ConditionFlag.BoundByDuty,
        ConditionFlag.BoundByDuty56,
        ConditionFlag.BoundByDuty95,
    ];

    private static readonly ConditionFlag[] CutsceneConditions =
    [
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
        ConditionFlag.OccupiedInCutSceneEvent,
    ];

    private readonly Configuration config;
    private readonly ChaperoneEngine engine;
    private readonly ObjectTableSampler sampler;
    private ulong? previousFocus;

    public MainWindow(Configuration config, ChaperoneEngine engine, ObjectTableSampler sampler, Action openSettings)
        : base("Chaperone###ChaperoneMain")
    {
        this.config = config;
        this.engine = engine;
        this.sampler = sampler;
        this.Size = new Vector2(320, 200);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(240, 100) };
        this.TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            Click = _ => openSettings(),
            IconOffset = new Vector2(2, 1),
            ShowTooltip = () => ImGui.SetTooltip("Chaperone settings"),
        });
        this.ApplyConfig();
    }

    public void ApplyConfig() => this.RespectCloseHotkey = this.config.CloseWithEscape;

    public override void OnClose() => this.RestoreFocus(false);

    public override bool DrawConditions()
    {
        // Draw stops being called while hidden, so a row hovered at that moment would leave the
        // focus target pinned; restore it on the way out.
        if ((this.config.HideInCombat && Services.Condition[ConditionFlag.InCombat])
            || (this.config.HideInDuty && Services.Condition.Any(DutyConditions))
            || Services.Condition.Any(CutsceneConditions))
        {
            this.RestoreFocus(false);
            return false;
        }

        return true;
    }

    public override void Draw()
    {
        if (this.sampler.Stopped)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Red);
            ImGui.TextWrapped($"Sampler stopped: {this.sampler.StopReason}");
            ImGui.PopStyleColor();
            if (ImGui.Button("Restart"))
            {
                this.sampler.Restart();
            }

            ImGui.Separator();
        }

        var frame = this.engine.Frame;
        if (!HasRows(frame))
        {
            this.RestoreFocus(false);
            return;
        }

        var height = ImGui.GetContentRegionAvail().Y - ImGui.GetStyle().ItemSpacing.Y;
        var anyHovered = false;
        if (ImGui.BeginListBox("##chaperoneList", new Vector2(-1, height)))
        {
            // Live rows across every subject first, then history rows, greyed.
            for (var s = 0; s < frame.Subjects.Count; s++)
            {
                var view = frame.Subjects[s];
                for (var i = 0; i < view.Live.Count; i++)
                {
                    var row = view.Live[i];
                    var right = this.config.ShowTimestamps ? $"{view.Name} {FormatClock(row.StartedMs, frame.NowMs)}" : view.Name;
                    this.DrawRow(
                        $"{row.Name}##{view.Key.Value}-{row.Key.Value}",
                        row.Mutual ? "mutual" : "",
                        right,
                        row.Name,
                        row.WorldId,
                        row.ObjectIndex,
                        row.GameObjectId,
                        disabled: false,
                        ref anyHovered);
                }
            }

            for (var s = 0; s < frame.Subjects.Count; s++)
            {
                var view = frame.Subjects[s];
                for (var i = 0; i < view.History.Count; i++)
                {
                    var h = view.History[i];
                    var right = this.config.ShowTimestamps ? $"{view.Name} {FormatClock(h.LastSeenMs, frame.NowMs)}" : view.Name;
                    this.DrawRow(
                        $"{h.Name}##{view.Key.Value}-h{h.Targeter.Value}",
                        "",
                        right,
                        h.Name,
                        h.WorldId,
                        -1,
                        h.LastGameObjectId,
                        disabled: true,
                        ref anyHovered);
                }
            }

            ImGui.EndListBox();
        }

        this.RestoreFocus(anyHovered);
    }

    private static bool HasRows(Frame frame)
    {
        for (var s = 0; s < frame.Subjects.Count; s++)
        {
            var view = frame.Subjects[s];
            if (view.Live.Count > 0 || view.History.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void DrawRow(string label, string tag, string rightText, string name, uint worldId, int objectIndex, ulong gameObjectId, bool disabled, ref bool anyHovered)
    {
        ImGui.BeginGroup();
        ImGui.Selectable(label, false, disabled ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None);
        if (tag.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(tag);
        }

        if (rightText.Length > 0)
        {
            var width = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
            ImGui.SameLine(width - ImGui.CalcTextSize(rightText).X);
            ImGui.TextDisabled(rightText);
        }

        ImGui.EndGroup();

        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        if (!hovered)
        {
            return;
        }

        var obj = ObjectLookup.Resolve(objectIndex, gameObjectId);
        if (obj != null)
        {
            anyHovered = true;
            if (this.config.FocusOnHover)
            {
                this.previousFocus ??= Services.TargetManager.FocusTarget?.GameObjectId ?? ulong.MaxValue;
                Services.TargetManager.FocusTarget = obj;
            }
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (this.config.ExamineOnAltClick && ImGui.GetIO().KeyAlt)
            {
                if (obj != null)
                {
                    unsafe
                    {
                        AgentInspect.Instance()->ExamineCharacter(obj.EntityId);
                    }
                }
                else
                {
                    Services.ToastGui.ShowError($"{name} is not nearby.");
                }
            }
            else
            {
                Services.ChatGui.Print(new SeStringBuilder()
                    .Add(new PlayerPayload(name, worldId))
                    .AddText(name)
                    .Add(RawPayload.LinkTerminator)
                    .Build(), "Chaperone");
            }
        }
        else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && obj != null)
        {
            Services.TargetManager.Target = obj;
        }
    }

    /// <summary>Local clock time like PeepingTom: "03:23", or "dd/MM" once it is a day old.</summary>
    private static string FormatClock(long unixMs, long nowMs)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime();
        return nowMs - unixMs >= 86_400_000 ? local.ToString("dd/MM") : local.ToString("HH:mm");
    }

    private void RestoreFocus(bool anyHovered)
    {
        if (!this.config.FocusOnHover || anyHovered || this.previousFocus is not { } previous)
        {
            return;
        }

        Services.TargetManager.FocusTarget = previous == ulong.MaxValue ? null : Services.ObjectTable.SearchById(previous);
        this.previousFocus = null;
    }
}
