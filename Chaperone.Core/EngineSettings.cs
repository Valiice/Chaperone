namespace Chaperone.Core;

public sealed class EngineSettings
{
    public int GraceMs { get; set; } = 1500;

    /// <summary>The sampler's poll interval. A pair unseen for several intervals was gated, not flickering.</summary>
    public int PollIntervalMs { get; set; } = 200;
    public int HistorySize { get; set; } = 5;
    public PolicySettings Policy { get; set; } = new();
}
