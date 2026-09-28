using System.Numerics;
using Chaperone.Alerting;
using Chaperone.Core;
using Chaperone.Sensors;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Chaperone.Windows;

public sealed class SettingsWindow : Window
{
    private readonly Configuration config;
    private readonly WatchlistManager watchlist;
    private readonly DebugTab debugTab;
    private readonly Action configChanged;
    private string addInput = "";
    private string? addError;

    public SettingsWindow(Configuration config, WatchlistManager watchlist, ObjectTableSampler sampler, ChaperoneEngine engine, Action configChanged)
        : base("Chaperone Settings###ChaperoneSettings")
    {
        this.config = config;
        this.watchlist = watchlist;
        this.debugTab = new DebugTab(sampler, engine);
        this.configChanged = configChanged;
        this.Size = new Vector2(460, 380);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##chaperoneSettingsTabs"))
        {
            return;
        }

        this.Tab("Watchlist", this.DrawWatchlist);
        this.Tab("Alerts", this.DrawAlerts);
        this.Tab("Filters", this.DrawFilters);
        this.Tab("Window", this.DrawWindow);
        this.Tab("Debug", this.debugTab.Draw);
        ImGui.EndTabBar();
    }

    private void Tab(string title, Action draw)
    {
        if (ImGui.BeginTabItem(title))
        {
            draw();
            ImGui.EndTabItem();
        }
    }

    private void Changed()
    {
        this.config.Clamp();
        this.config.Save();
        this.configChanged();
    }

    private void DrawWatchlist()
    {
        ImGui.SetNextItemWidth(260);
        ImGui.InputText("##chaperoneAdd", ref this.addInput, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add"))
        {
            this.addError = this.watchlist.Add(this.addInput);
            if (this.addError == null) this.addInput = "";
        }

        ImGui.SameLine();
        if (ImGui.Button("Add current target"))
        {
            this.addError = this.watchlist.AddCurrentTarget();
        }

        ImGui.TextDisabled("First Last@World. Without @World the current world is used.");
        if (this.addError != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
            ImGui.TextWrapped(this.addError);
            ImGui.PopStyleColor();
        }

        ImGui.Separator();
        if (!ImGui.BeginTable("##chaperoneWatchlist", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 30);
        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##remove", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableHeadersRow();

        var removeIndex = -1;
        var entries = this.watchlist.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var enabled = entry.Enabled;
            if (ImGui.Checkbox($"##on{i}", ref enabled))
            {
                entry.Enabled = enabled;
                this.config.Save();
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.Display);
            ImGui.TableNextColumn();
            if (ImGui.Button($"Remove##{i}"))
            {
                removeIndex = i;
            }
        }

        ImGui.EndTable();
        if (removeIndex >= 0)
        {
            this.watchlist.RemoveAt(removeIndex);
        }
    }

    private void DrawAlerts()
    {
        var chat = this.config.ChatAlerts;
        if (ImGui.Checkbox("Chat message", ref chat)) { this.config.ChatAlerts = chat; this.Changed(); }

        var sound = this.config.SoundAlerts;
        if (ImGui.Checkbox("Sound", ref sound)) { this.config.SoundAlerts = sound; this.Changed(); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        var soundIndex = this.config.SoundId - 1;
        if (ImGui.BeginCombo("##chaperoneSound", SoundEffects.Names[Math.Clamp(soundIndex, 0, 15)]))
        {
            for (var i = 0; i < SoundEffects.Names.Length; i++)
            {
                if (ImGui.Selectable(SoundEffects.Names[i], i == soundIndex))
                {
                    this.config.SoundId = i + 1;
                    this.Changed();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("Preview")) SoundEffects.Play(this.config.SoundId);

        var notify = this.config.NotificationAlerts;
        if (ImGui.Checkbox("Dalamud notification", ref notify)) { this.config.NotificationAlerts = notify; this.Changed(); }

        ImGui.Separator();
        var threshold = (float)this.config.DwellThresholdSeconds;
        if (ImGui.SliderFloat("Alert after (seconds targeted)", ref threshold, 0f, 60f, "%.1f")) { this.config.DwellThresholdSeconds = threshold; this.Changed(); }

        var cooldown = this.config.PairCooldownSeconds;
        if (ImGui.SliderInt("Repeat alert for the same pair after (s)", ref cooldown, 0, 600)) { this.config.PairCooldownSeconds = cooldown; this.Changed(); }

        var floor = this.config.GlobalFloorSeconds;
        if (ImGui.SliderInt("Minimum gap between any two alerts (s)", ref floor, 0, 60)) { this.config.GlobalFloorSeconds = floor; this.Changed(); }

        var grace = this.config.GraceMs;
        if (ImGui.SliderInt("Flicker grace (ms)", ref grace, 0, 5000)) { this.config.GraceMs = grace; this.Changed(); }

        var history = this.config.HistorySize;
        if (ImGui.SliderInt("History rows per person", ref history, 0, 50)) { this.config.HistorySize = history; this.Changed(); }

        var combat = this.config.AlertInCombat;
        if (ImGui.Checkbox("Alert for targeters who are in combat", ref combat)) { this.config.AlertInCombat = combat; this.Changed(); }

        var self = this.config.LogSelf;
        if (ImGui.Checkbox("Show my own targeting as a row (testing)", ref self)) { this.config.LogSelf = self; this.Changed(); }
    }

    private void DrawFilters()
    {
        ImGui.TextWrapped("Ignored targeters still appear in the window; they never trigger an alert.");
        this.Toggle("Ignore party members", () => this.config.IgnoreParty, v => this.config.IgnoreParty = v);
        this.Toggle("Ignore alliance members", () => this.config.IgnoreAlliance, v => this.config.IgnoreAlliance = v);
        this.Toggle("Ignore my friend list", () => this.config.IgnoreFriends, v => this.config.IgnoreFriends = v);
        this.Toggle("Ignore my free company", () => this.config.IgnoreFreeCompany, v => this.config.IgnoreFreeCompany = v);
        this.Toggle("Ignore other watched players", () => this.config.IgnoreWatched, v => this.config.IgnoreWatched = v);
        ImGui.Separator();
        this.Toggle("Pause in PvP", () => this.config.PauseInPvp, v => this.config.PauseInPvp = v);
        var interval = this.config.PollIntervalMs;
        if (ImGui.SliderInt("Poll interval (ms)", ref interval, 50, 1000)) { this.config.PollIntervalMs = interval; this.Changed(); }
    }

    private void DrawWindow()
    {
        this.Toggle("Open on login", () => this.config.OpenOnLogin, v => this.config.OpenOnLogin = v);
        this.Toggle("Hide in combat", () => this.config.HideInCombat, v => this.config.HideInCombat = v);
        this.Toggle("Hide in duties", () => this.config.HideInDuty, v => this.config.HideInDuty = v);
        this.Toggle("Close with Escape", () => this.config.CloseWithEscape, v => this.config.CloseWithEscape = v);
        this.Toggle("Focus target on hover", () => this.config.FocusOnHover, v => this.config.FocusOnHover = v);
        this.Toggle("Alt+click opens examine", () => this.config.ExamineOnAltClick, v => this.config.ExamineOnAltClick = v);
        this.Toggle("Show times", () => this.config.ShowTimestamps, v => this.config.ShowTimestamps = v);
        this.Toggle("Server info bar entry (live targeter count)", () => this.config.ShowDtrEntry, v => this.config.ShowDtrEntry = v);

        ImGui.Separator();
        this.Toggle("Mark targeters in the world", () => this.config.MarkTargeters, v => this.config.MarkTargeters = v);
        var tc = this.config.TargeterColour;
        if (ImGui.ColorEdit4("Targeter colour", ref tc)) { this.config.TargeterColour = tc; this.Changed(); }
        var ts = this.config.TargeterSize;
        if (ImGui.SliderFloat("Targeter marker size", ref ts, 1f, 10f)) { this.config.TargeterSize = ts; this.Changed(); }

        this.Toggle("Mark watched friends in the world", () => this.config.MarkFriends, v => this.config.MarkFriends = v);
        var fc = this.config.FriendColour;
        if (ImGui.ColorEdit4("Friend colour", ref fc)) { this.config.FriendColour = fc; this.Changed(); }
        var fs = this.config.FriendSize;
        if (ImGui.SliderFloat("Friend marker size", ref fs, 1f, 10f)) { this.config.FriendSize = fs; this.Changed(); }
    }

    private void Toggle(string label, Func<bool> get, Action<bool> set)
    {
        var value = get();
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            this.Changed();
        }
    }
}
