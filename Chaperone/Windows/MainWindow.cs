using System.Numerics;
using Chaperone.Core;
using Chaperone.Sensors;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Chaperone.Windows;

/// <summary>
/// The compact list: one header per subject, live targeter rows under it, greyed history rows
/// below those. Draws only from the engine's cached frame. Row interactions mirror PeepingTom:
/// hover focuses, left click links to chat, right click targets, Alt+click examines.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly Vector4 Red = new(1f, 0.35f, 0.35f, 1f);

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
        this.Size = new Vector2(300, 220);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(220, 120) };
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

    public override bool DrawConditions()
    {
        if (this.config.HideInCombat && Services.Condition[ConditionFlag.InCombat]) return false;
        if (this.config.HideInDuty && Services.Condition.Any(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95)) return false;
        if (Services.Condition.Any(ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78, ConditionFlag.OccupiedInCutSceneEvent)) return false;
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
        if (frame.Subjects.Count == 0)
        {
            ImGui.TextDisabled("Nothing to watch.");
            ImGui.TextDisabled("Add friends in settings, or target someone.");
            return;
        }

        var height = ImGui.GetContentRegionAvail().Y - ImGui.GetStyle().ItemSpacing.Y;
        var anyHovered = false;
        if (ImGui.BeginListBox("##chaperoneList", new Vector2(-1, height)))
        {
            for (var s = 0; s < frame.Subjects.Count; s++)
            {
                this.DrawSubject(frame.Subjects[s], frame.NowMs, ref anyHovered);
            }

            ImGui.EndListBox();
        }

        this.RestoreFocus(anyHovered);
    }

    private void DrawSubject(SubjectView view, long nowMs, ref bool anyHovered)
    {
        ImGui.TextUnformatted(view.Name);
        if (view.IsCurrentTarget)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(your target)");
        }

        var right = view.IsNearby ? (view.Live.Count == 0 ? "" : view.Live.Count.ToString()) : "not nearby";
        if (right.Length > 0)
        {
            var width = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
            ImGui.SameLine(width - ImGui.CalcTextSize(right).X);
            ImGui.TextDisabled(right);
        }

        ImGui.Indent();
        for (var i = 0; i < view.Live.Count; i++)
        {
            var row = view.Live[i];
            var timeText = this.config.ShowTimestamps ? $"{row.DwellSeconds:0}s" : "";
            this.DrawRow($"{row.Name}##{view.Key.Value}-{row.Key.Value}", timeText, row.Name, row.WorldId, row.ObjectIndex, row.GameObjectId, disabled: false, ref anyHovered);
        }

        for (var i = 0; i < view.History.Count; i++)
        {
            var h = view.History[i];
            var timeText = this.config.ShowTimestamps ? FormatWhen(h.LastSeenMs, nowMs) : "";
            this.DrawRow($"{h.Name}##{view.Key.Value}-h{h.Targeter.Value}", timeText, h.Name, h.WorldId, -1, h.LastGameObjectId, disabled: true, ref anyHovered);
        }

        ImGui.Unindent();
    }

    private void DrawRow(string label, string timeText, string name, uint worldId, int objectIndex, ulong gameObjectId, bool disabled, ref bool anyHovered)
    {
        ImGui.BeginGroup();
        ImGui.Selectable(label, false, disabled ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None);
        if (timeText.Length > 0)
        {
            var width = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
            ImGui.SameLine(width - ImGui.CalcTextSize(timeText).X);
            if (disabled) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextUnformatted(timeText);
            if (disabled) ImGui.PopStyleColor();
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

    private void RestoreFocus(bool anyHovered)
    {
        if (!this.config.FocusOnHover || anyHovered || this.previousFocus is not { } previous)
        {
            return;
        }

        Services.TargetManager.FocusTarget = previous == ulong.MaxValue ? null : Services.ObjectTable.SearchById(previous);
        this.previousFocus = null;
    }

    private static string FormatWhen(long thenMs, long nowMs)
    {
        var then = DateTimeOffset.FromUnixTimeMilliseconds(thenMs).ToLocalTime();
        return nowMs - thenMs >= 86_400_000 ? then.ToString("dd/MM") : then.ToString("t");
    }
}
