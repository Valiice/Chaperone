using System.Numerics;
using System.Threading;
using Chaperone.Core;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Chaperone;

/// <summary>
/// Persisted settings. <see cref="Save"/> is debounced: it marks the config dirty and arms a two
/// second timer, so bursts of changes on the game thread collapse into one JSON write on a
/// thread-pool thread. <see cref="Flush"/> writes immediately if dirty (used on unload).
/// </summary>
public class Configuration : IPluginConfiguration, IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(2);

    [NonSerialized] private readonly object saveGate = new();
    [NonSerialized] private Timer? saveTimer;
    [NonSerialized] private int dirty;
    [NonSerialized] private bool disposed;

    /// <summary>Held by every Watchlist mutation and by Flush, which serializes the list on another thread.</summary>
    [NonSerialized] internal readonly object WatchlistLock = new();

    public int Version { get; set; } = 2;

    public List<WatchlistEntry> Watchlist { get; set; } = [];

    // Sampling
    public int PollIntervalMs { get; set; } = 200;
    public int GraceMs { get; set; } = 1500;
    public int HistorySize { get; set; } = 5;
    public bool PauseInPvp { get; set; } = true;

    // Alerts
    public double DwellThresholdSeconds { get; set; } = 3;
    public int PairCooldownSeconds { get; set; } = 60;
    public int GlobalFloorSeconds { get; set; } = 2;
    public bool AlertInCombat { get; set; }
    public bool LogSelf { get; set; }
    public bool ChatAlerts { get; set; }
    public bool SoundAlerts { get; set; } = true;
    public int SoundId { get; set; } = 6;
    public bool NotificationAlerts { get; set; }

    // Filters
    public bool IgnoreParty { get; set; } = true;
    public bool IgnoreAlliance { get; set; } = true;
    public bool IgnoreFriends { get; set; } = true;
    public bool IgnoreFreeCompany { get; set; } = true;
    public bool IgnoreWatched { get; set; } = true;

    // Window
    public bool OpenOnLogin { get; set; }
    public bool HideInCombat { get; set; } = true;
    public bool HideInDuty { get; set; } = true;
    public bool CloseWithEscape { get; set; } = true;
    public bool FocusOnHover { get; set; } = true;
    public bool ExamineOnAltClick { get; set; } = true;
    public bool ShowTimestamps { get; set; } = true;
    public bool MarkTargeters { get; set; }
    public Vector4 TargeterColour { get; set; } = new(1f, 0.3f, 0.3f, 1f);
    public float TargeterSize { get; set; } = 2f;
    public bool MarkFriends { get; set; }
    public Vector4 FriendColour { get; set; } = new(0.3f, 0.8f, 1f, 1f);
    public float FriendSize { get; set; } = 2f;
    public bool ShowDtrEntry { get; set; }

    public static Configuration Load(IDalamudPluginInterface pluginInterface)
    {
        var config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Watchlist ??= [];

        // Version 2: chat alerts default off. Configs saved by version 1 had them on.
        if (config.Version < 2)
        {
            config.ChatAlerts = false;
            config.Version = 2;
            config.Save();
        }

        config.Clamp();
        return config;
    }

    /// <summary>Keeps hand-edited or stale JSON inside the ranges the UI offers.</summary>
    public void Clamp()
    {
        this.PollIntervalMs = Math.Clamp(this.PollIntervalMs, 50, 1000);
        this.GraceMs = Math.Clamp(this.GraceMs, 0, 5000);
        this.HistorySize = Math.Clamp(this.HistorySize, 0, 50);
        this.DwellThresholdSeconds = Math.Clamp(this.DwellThresholdSeconds, 0, 60);
        this.PairCooldownSeconds = Math.Clamp(this.PairCooldownSeconds, 0, 600);
        this.GlobalFloorSeconds = Math.Clamp(this.GlobalFloorSeconds, 0, 60);
        this.SoundId = Math.Clamp(this.SoundId, 1, 16);
        this.TargeterSize = Math.Clamp(this.TargeterSize, 1f, 10f);
        this.FriendSize = Math.Clamp(this.FriendSize, 1f, 10f);
    }

    /// <summary>Copies the engine-relevant settings. Called once per poll; cheap field copies only.</summary>
    public void ApplyTo(EngineSettings settings)
    {
        settings.GraceMs = this.GraceMs;
        settings.PollIntervalMs = this.PollIntervalMs;
        settings.HistorySize = this.HistorySize;
        var p = settings.Policy;
        p.DwellThresholdSeconds = this.DwellThresholdSeconds;
        p.PairCooldownSeconds = this.PairCooldownSeconds;
        p.GlobalFloorSeconds = this.GlobalFloorSeconds;
        p.IgnoreParty = this.IgnoreParty;
        p.IgnoreAlliance = this.IgnoreAlliance;
        p.IgnoreFriends = this.IgnoreFriends;
        p.IgnoreFreeCompany = this.IgnoreFreeCompany;
        p.IgnoreWatched = this.IgnoreWatched;
        p.AlertInCombat = this.AlertInCombat;
        p.LogSelf = this.LogSelf;
    }

    public void Save()
    {
        Interlocked.Exchange(ref this.dirty, 1);
        lock (this.saveGate)
        {
            if (this.disposed)
            {
                // Dispose already flushed; a Save after that is dropped by design.
                return;
            }

            this.saveTimer ??= new Timer(_ => this.Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            this.saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Flush()
    {
        if (Interlocked.Exchange(ref this.dirty, 0) == 0)
        {
            return;
        }

        try
        {
            lock (this.WatchlistLock)
            {
                Services.PluginInterface.SavePluginConfig(this);
            }
        }
        catch (Exception e)
        {
            Services.Log.Warning(e, "Chaperone: configuration save failed, retrying");
            this.Save();
        }
    }

    public void Dispose()
    {
        lock (this.saveGate)
        {
            this.disposed = true;
            this.saveTimer?.Dispose();
            this.saveTimer = null;
        }

        this.Flush();
    }
}
