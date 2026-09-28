using System.Diagnostics;
using Chaperone.Core;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace Chaperone.Sensors;

/// <summary>
/// Framework-thread poll. Walks the object table once per interval, fills the reusable snapshot
/// with ids, flags and cached names, and hands it to the engine. Reads other players' hard
/// target from ClientStructs (Character.TargetId), never from Dalamud's look-at field.
/// Five consecutive failures stop the sampler; the window offers a restart.
/// </summary>
public sealed unsafe class ObjectTableSampler : IDisposable
{
    private const ulong NoTargetObjectId = 0xE0000000;
    private const int MaxConsecutiveFailures = 5;

    private static readonly ConditionFlag[] PauseConditions =
    [
        ConditionFlag.BetweenAreas,
        ConditionFlag.BetweenAreas51,
        ConditionFlag.WatchingCutscene,
        ConditionFlag.WatchingCutscene78,
        ConditionFlag.OccupiedInCutSceneEvent,
    ];

    private readonly Configuration config;
    private readonly ChaperoneEngine engine;
    private readonly Action<AlertEvent> onAlert;
    private readonly Snapshot snapshot = new();
    private readonly NameCache names = new();
    private readonly PollThrottle throttle = new();
    private readonly HashSet<string> loggedErrors = new();
    private int consecutiveFailures;

    public ObjectTableSampler(Configuration config, ChaperoneEngine engine, Action<AlertEvent> onAlert)
    {
        this.config = config;
        this.engine = engine;
        this.onAlert = onAlert;
        Services.Framework.Update += this.OnFrameworkUpdate;
    }

    public bool Stopped { get; private set; }

    public string? StopReason { get; private set; }

    public string GateReason { get; private set; } = "not started";

    /// <summary>Cost of the whole poll (object table walk plus engine), for the debug tab.</summary>
    public PollStats TotalStats { get; } = new();

    public int NameCacheCount => this.names.Count;

    public ulong DebugTargetHardId { get; private set; }

    public ulong DebugTargetLookAtId { get; private set; }

    /// <summary>Players this poll whose hard target and look-at target differ. Non-zero is expected sometimes.</summary>
    public int DebugMismatchCount { get; private set; }

    public void Restart()
    {
        this.Stopped = false;
        this.StopReason = null;
        this.consecutiveFailures = 0;
        this.throttle.Reset();
    }

    /// <summary>Logout: drop cached names and engine state.</summary>
    public void Reset()
    {
        this.names.Clear();
        this.engine.Reset();
        this.throttle.Reset();
    }

    public void Dispose() => Services.Framework.Update -= this.OnFrameworkUpdate;

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (this.Stopped)
        {
            return;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!this.throttle.ShouldRun(nowMs, this.config.PollIntervalMs))
        {
            return;
        }

        try
        {
            this.Poll(nowMs);
            this.consecutiveFailures = 0;
        }
        catch (Exception e)
        {
            this.consecutiveFailures++;
            if (this.loggedErrors.Add(e.Message))
            {
                Services.Log.Error(e, "Chaperone: poll failed");
            }

            if (this.consecutiveFailures >= MaxConsecutiveFailures)
            {
                this.Stopped = true;
                this.StopReason = e.Message;
                this.GateReason = "stopped";
                Services.Log.Error($"Chaperone: sampler stopped after {MaxConsecutiveFailures} failures: {e.Message}");
            }
        }
    }

    private void Poll(long nowMs)
    {
        if (!Services.ClientState.IsLoggedIn)
        {
            this.GateReason = "not logged in";
            return;
        }

        var local = Services.ObjectTable.LocalPlayer;
        if (local == null)
        {
            this.GateReason = "no local player";
            return;
        }

        if (Services.Condition.Any(PauseConditions))
        {
            this.GateReason = "loading or cutscene";
            return;
        }

        if (this.config.PauseInPvp && Services.ClientState.IsPvP)
        {
            this.GateReason = "pvp";
            return;
        }

        if (Services.ClientState.IsGPosing)
        {
            this.GateReason = "gpose";
            return;
        }

        var target = Services.TargetManager.SoftTarget ?? Services.TargetManager.Target;
        var localTargetId = target is { ObjectKind: ObjectKind.Pc } ? target.GameObjectId : 0;

        if (this.config.Watchlist.Count == 0 && localTargetId == 0)
        {
            this.GateReason = "idle: nothing to watch";

            // The engine does not run while idle, so clear the last frame rather than leave a
            // frozen subject in the window, markers and server bar. An empty watchlist has no
            // history worth keeping.
            if (this.engine.Frame.Subjects.Count > 0 || this.engine.Frame.LivePairs > 0)
            {
                this.engine.Reset();
            }

            return;
        }

        this.GateReason = "running";
        var started = Stopwatch.GetTimestamp();

        var localChar = (Character*)local.Address;
        var localHomeWorld = localChar->HomeWorld;
        var localHasFc = localChar->FreeCompanyTag[0] != 0;
        var localOnHomeWorld = localChar->CurrentWorld == localChar->HomeWorld;

        this.snapshot.Clear(nowMs);
        this.snapshot.LocalGameObjectId = local.GameObjectId;
        this.snapshot.LocalTargetId = localTargetId;
        this.names.BeginPoll();
        this.DebugMismatchCount = 0;
        this.DebugTargetHardId = 0;
        this.DebugTargetLookAtId = 0;

        foreach (var obj in Services.ObjectTable.PlayerObjects)
        {
            if (obj is not IPlayerCharacter pc)
            {
                continue;
            }

            var chara = (Character*)pc.Address;
            if (chara == null)
            {
                continue;
            }

            ref var o = ref this.snapshot.Add();
            o.GameObjectId = pc.GameObjectId;
            o.ObjectIndex = pc.ObjectIndex;
            o.ContentId = chara->ContentId;
            o.HomeWorldId = chara->HomeWorld;
            o.Name = this.names.Get(pc);

            o.HardTargetId = NormalizeTargetId((ulong)chara->TargetId);
            o.LookAtTargetId = NormalizeTargetId(pc.TargetObjectId);
            if (o.HardTargetId != o.LookAtTargetId)
            {
                this.DebugMismatchCount++;
            }

            var flags = ObservationFlags.None;
            if (pc.GameObjectId == local.GameObjectId)
            {
                flags |= ObservationFlags.LocalPlayer;
            }
            else
            {
                if (chara->IsFriend) flags |= ObservationFlags.Friend;
                if (chara->IsPartyMember) flags |= ObservationFlags.Party;
                if (chara->IsAllianceMember) flags |= ObservationFlags.Alliance;
                if (localHasFc && localOnHomeWorld && chara->HomeWorld == localHomeWorld
                    && chara->FreeCompanyTag.SequenceEqual(localChar->FreeCompanyTag))
                {
                    flags |= ObservationFlags.FreeCompany;
                }
            }

            if (chara->InCombat) flags |= ObservationFlags.InCombat;
            o.Flags = flags;

            if (pc.GameObjectId == localTargetId)
            {
                this.DebugTargetHardId = o.HardTargetId;
                this.DebugTargetLookAtId = o.LookAtTargetId;
            }
        }

        this.names.EndPoll();
        this.config.ApplyTo(this.engine.Settings);
        this.engine.Poll(this.snapshot, this.config.Watchlist);

        if (this.engine.Frame.WatchlistChanged)
        {
            this.config.Save();
        }

        var alerts = this.engine.Frame.Alerts;
        for (var i = 0; i < alerts.Count; i++)
        {
            // One bad alert must neither skip the rest nor count toward the sampler's failure stop.
            try
            {
                this.onAlert(alerts[i]);
            }
            catch (Exception e)
            {
                if (this.loggedErrors.Add(e.Message))
                {
                    Services.Log.Error(e, "Chaperone: alert delivery failed");
                }
            }
        }

        this.TotalStats.Record(nowMs, Stopwatch.GetElapsedTime(started).TotalMicroseconds);
    }

    /// <summary>"No target" is 0xE0000000 in the hard target field and 0 in the look-at field; both fold to 0.</summary>
    private static ulong NormalizeTargetId(ulong id) => id == 0 || (id & 0xFFFF_FFFF) == NoTargetObjectId ? 0 : id;
}
