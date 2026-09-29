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
    private const long DayMs = 86_400_000;

    private static readonly Vector4 Red = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 Mutual = new(0.55f, 0.95f, 0.55f, 1f);

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
    private readonly RowTextCache rowText = new();
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
        this.rowText.BeginFrame(this.config.ShowTimestamps);
        if (ImGui.BeginListBox("##chaperoneList", new Vector2(-1, height)))
        {
            // Live rows across every subject first, then history rows, greyed. Row strings are
            // cached per (subject, targeter) so a rendered frame allocates nothing while the
            // frame is unchanged.
            for (var s = 0; s < frame.Subjects.Count; s++)
            {
                var view = frame.Subjects[s];
                for (var i = 0; i < view.Live.Count; i++)
                {
                    var row = view.Live[i];
                    var text = this.rowText.Get(view, row.Key, row.Name, row.StartedMs, frame.NowMs, history: false);
                    this.DrawRow(text.Label, row.Mutual, text.Right, row.Name, row.WorldId, row.ObjectIndex, row.GameObjectId, disabled: false, ref anyHovered);
                }
            }

            for (var s = 0; s < frame.Subjects.Count; s++)
            {
                var view = frame.Subjects[s];
                for (var i = 0; i < view.History.Count; i++)
                {
                    var h = view.History[i];
                    var text = this.rowText.Get(view, h.Targeter, h.Name, h.LastSeenMs, frame.NowMs, history: true);
                    this.DrawRow(text.Label, false, text.Right, h.Name, h.WorldId, -1, h.LastGameObjectId, disabled: true, ref anyHovered);
                }
            }

            ImGui.EndListBox();
        }

        this.rowText.EndFrame();
        this.RestoreFocus(anyHovered);
    }

    /// <summary>
    /// Caches the label and right-hand text of each row. ImGui needs a unique label per row and
    /// the clock text only changes when the row's timestamp does, so both are built once and
    /// reused until the row disappears for a while.
    /// </summary>
    private sealed class RowTextCache
    {
        private const int PruneEveryFrames = 600;

        private readonly Dictionary<(ulong Subject, ulong Targeter, bool History), Entry> entries = new();
        private int frameCounter;
        private bool showTimes;

        public void BeginFrame(bool showTimestamps)
        {
            this.frameCounter++;
            if (showTimestamps != this.showTimes)
            {
                this.showTimes = showTimestamps;
                this.entries.Clear();
            }
        }

        public Entry Get(SubjectView view, PlayerKey targeter, string name, long stampMs, long nowMs, bool history)
        {
            var key = (view.Key.Value, targeter.Value, history);
            var dayOld = nowMs - stampMs >= DayMs;
            if (!this.entries.TryGetValue(key, out var entry))
            {
                // The label depends only on the names and keys, so it is built once per row.
                entry = new Entry
                {
                    Label = history ? $"{name}##{key.Item1}-h{key.Item2}" : $"{name}##{key.Item1}-{key.Item2}",
                };
                this.entries[key] = entry;
            }

            if (entry.StampMs != stampMs || entry.DayOld != dayOld || !string.Equals(entry.SubjectName, view.Name, StringComparison.Ordinal))
            {
                entry.StampMs = stampMs;
                entry.DayOld = dayOld;
                entry.SubjectName = view.Name;
                entry.Right = this.showTimes ? $"{view.Name} {FormatClock(stampMs, dayOld)}" : view.Name;
            }

            entry.LastFrame = this.frameCounter;
            return entry;
        }

        public void EndFrame()
        {
            if (this.frameCounter % PruneEveryFrames != 0)
            {
                return;
            }

            // Removing during enumeration is safe for Dictionary on this runtime.
            foreach (var (key, entry) in this.entries)
            {
                if (this.frameCounter - entry.LastFrame > PruneEveryFrames)
                {
                    this.entries.Remove(key);
                }
            }
        }

        public sealed class Entry
        {
            public string Label = "";
            public string Right = "";
            public string SubjectName = "";
            public long StampMs = long.MinValue;
            public bool DayOld;
            public int LastFrame;
        }
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

    private void DrawRow(string label, bool mutual, string rightText, string name, uint worldId, int objectIndex, ulong gameObjectId, bool disabled, ref bool anyHovered)
    {
        ImGui.BeginGroup();

        // Mutual targeting is shown by colouring the name, so it costs no horizontal space.
        if (mutual) ImGui.PushStyleColor(ImGuiCol.Text, Mutual);
        ImGui.Selectable(label, false, disabled ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None);
        if (mutual) ImGui.PopStyleColor();

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
    private static string FormatClock(long unixMs, bool dayOld)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime();
        return dayOld ? local.ToString("dd/MM") : local.ToString("HH:mm");
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
