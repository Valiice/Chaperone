using Chaperone.Core;
using Xunit;

namespace Chaperone.Tests;

public class HistoryBufferTests
{
    private static readonly PlayerKey Subject = PlayerKey.FromContentId(100);

    private static HistoryEntry Entry(ulong id, long seen) =>
        new(PlayerKey.FromContentId(id), $"P{id}", 1, 0, seen, 1);

    [Fact]
    public void NewestFirstAndCapped()
    {
        var h = new HistoryBuffer();
        for (ulong i = 1; i <= 7; i++)
        {
            h.Push(Subject, Entry(i, (long)i), capacity: 5);
        }

        var list = h.For(Subject);
        Assert.Equal(5, list.Count);
        Assert.Equal(PlayerKey.FromContentId(7), list[0].Targeter);
        Assert.Equal(PlayerKey.FromContentId(3), list[4].Targeter);
    }

    [Fact]
    public void PushingSameTargeterReplacesAndMovesToFront()
    {
        var h = new HistoryBuffer();
        h.Push(Subject, Entry(1, 10), 5);
        h.Push(Subject, Entry(2, 20), 5);
        h.Push(Subject, Entry(1, 30), 5);
        var list = h.For(Subject);
        Assert.Equal(2, list.Count);
        Assert.Equal(30, list[0].LastSeenMs);
        Assert.Equal(PlayerKey.FromContentId(2), list[1].Targeter);
    }

    [Fact]
    public void RemoveAndClearWork()
    {
        var h = new HistoryBuffer();
        h.Push(Subject, Entry(1, 10), 5);
        h.Push(Subject, Entry(2, 20), 5);
        h.Remove(Subject, PlayerKey.FromContentId(1));
        Assert.Single(h.For(Subject));
        h.Clear(Subject);
        Assert.Empty(h.For(Subject));
        Assert.Empty(h.For(PlayerKey.FromContentId(999)));
    }

    [Fact]
    public void CapacityZeroKeepsNothing()
    {
        var h = new HistoryBuffer();
        h.Push(Subject, Entry(1, 10), 0);
        Assert.Empty(h.For(Subject));
    }

    [Fact]
    public void ForgetRemovesSubjectAndPushStillWorks()
    {
        var h = new HistoryBuffer();
        h.Push(Subject, Entry(1, 10), 5);
        h.Forget(Subject);
        Assert.Empty(h.For(Subject));
        h.Push(Subject, Entry(2, 20), 5);
        Assert.Single(h.For(Subject));
    }
}
