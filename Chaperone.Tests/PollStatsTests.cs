using Chaperone.Core;
using Xunit;

namespace Chaperone.Tests;

public class PollStatsTests
{
    [Fact]
    public void TracksLastAverageAndMaxOverOneSecond()
    {
        var s = new PollStats();
        s.Record(1000, 10);
        s.Record(1200, 30);
        s.Record(1400, 20);
        Assert.Equal(20, s.Last);
        Assert.Equal(20, s.AverageLastSecond, 3);
        Assert.Equal(30, s.MaxLastSecond);

        // 1000 and 1200 fall out of the window at 2300.
        s.Record(2300, 5);
        Assert.Equal(12.5, s.AverageLastSecond, 3);
        Assert.Equal(20, s.MaxLastSecond);
    }

    [Fact]
    public void EmptyStatsAreZero()
    {
        var s = new PollStats();
        Assert.Equal(0, s.Last);
        Assert.Equal(0, s.AverageLastSecond);
        Assert.Equal(0, s.MaxLastSecond);
    }

    [Fact]
    public void SnapshotAddGrowsAndClears()
    {
        var snap = new Snapshot();
        snap.Clear(5);
        for (var i = 0; i < 300; i++)
        {
            ref var o = ref snap.Add();
            o.GameObjectId = (ulong)i;
        }

        Assert.Equal(300, snap.Count);
        Assert.Equal(5, snap.NowMs);
        snap.Clear(6);
        Assert.Equal(0, snap.Count);
        Assert.True(snap.Players.Length >= 300);
    }
}
