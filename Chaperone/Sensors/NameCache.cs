using Dalamud.Game.ClientState.Objects.SubKinds;

namespace Chaperone.Sensors;

/// <summary>
/// Extracts a player's name from the game once per game object id and hands out the same string
/// afterwards. Entries not touched during a poll are evicted at its end, so a slot reused by
/// another player gets a fresh extraction.
/// </summary>
public sealed class NameCache
{
    private sealed class Entry
    {
        public string Name = "";
        public int LastPoll;
    }

    private readonly Dictionary<ulong, Entry> entries = new();
    private readonly List<ulong> scratch = new();
    private int poll;

    public int Count => this.entries.Count;

    public void BeginPoll() => this.poll++;

    public string Get(IPlayerCharacter pc)
    {
        var id = pc.GameObjectId;
        if (this.entries.TryGetValue(id, out var entry))
        {
            entry.LastPoll = this.poll;
            return entry.Name;
        }

        entry = new Entry { Name = pc.Name.TextValue, LastPoll = this.poll };
        this.entries[id] = entry;
        return entry.Name;
    }

    public void EndPoll()
    {
        this.scratch.Clear();
        foreach (var (id, entry) in this.entries)
        {
            if (entry.LastPoll != this.poll)
            {
                this.scratch.Add(id);
            }
        }

        for (var i = 0; i < this.scratch.Count; i++)
        {
            this.entries.Remove(this.scratch[i]);
        }
    }

    public void Clear() => this.entries.Clear();
}
