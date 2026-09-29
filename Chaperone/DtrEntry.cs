using Chaperone.Core;
using Dalamud.Game.Gui.Dtr;

namespace Chaperone;

/// <summary>Server info bar entry with the live targeter count across watched friends. Text is rebuilt only when the count changes.</summary>
public sealed class DtrEntry : IDisposable
{
    private readonly Configuration config;
    private readonly ChaperoneEngine engine;
    private readonly IDtrBarEntry entry;
    private int lastCount = -1;
    private int lastSequence = -1;
    private bool lastShown;

    public DtrEntry(Configuration config, ChaperoneEngine engine, Action onClick)
    {
        this.config = config;
        this.engine = engine;
        this.entry = Services.DtrBar.Get("Chaperone");
        this.entry.OnClick = _ => onClick();
        this.entry.Tooltip = "Chaperone: players targeting your watched friends";
        this.entry.Shown = false;
    }

    /// <summary>Called by the plugin every framework update; cheap when nothing changed.</summary>
    public void Update()
    {
        var shown = this.config.ShowDtrEntry;
        if (shown != this.lastShown)
        {
            this.entry.Shown = shown;
            this.lastShown = shown;
        }

        if (!shown)
        {
            return;
        }

        // The frame only changes on a poll, so recount only then, not every rendered frame.
        var frame = this.engine.Frame;
        if (frame.Sequence == this.lastSequence)
        {
            return;
        }

        this.lastSequence = frame.Sequence;
        var count = 0;
        var subjects = frame.Subjects;
        for (var i = 0; i < subjects.Count; i++)
        {
            if (subjects[i].IsWatched)
            {
                count += subjects[i].Live.Count;
            }
        }

        if (count != this.lastCount)
        {
            this.entry.Text = $"Chaperone: {count}";
            this.lastCount = count;
        }
    }

    public void Dispose() => this.entry.Remove();
}
