namespace Chaperone.Core;

/// <summary>Reusable per-poll buffer. Cleared and refilled every poll; never reallocated in steady state.</summary>
public sealed class Snapshot
{
    public PlayerObservation[] Players = new PlayerObservation[256];
    public int Count;

    /// <summary>The user's own target (soft target first, then hard target), 0 if none or not a player.</summary>
    public ulong LocalTargetId;

    public ulong LocalGameObjectId;
    public long NowMs;

    public void Clear(long nowMs)
    {
        this.Count = 0;
        this.LocalTargetId = 0;
        this.LocalGameObjectId = 0;
        this.NowMs = nowMs;
    }

    public ref PlayerObservation Add()
    {
        if (this.Count == this.Players.Length)
        {
            Array.Resize(ref this.Players, this.Players.Length * 2);
        }

        return ref this.Players[this.Count++];
    }
}
