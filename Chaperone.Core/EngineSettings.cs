namespace Chaperone.Core;

public sealed class EngineSettings
{
    public int GraceMs { get; set; } = 1500;
    public int HistorySize { get; set; } = 5;
    public PolicySettings Policy { get; set; } = new();
}
