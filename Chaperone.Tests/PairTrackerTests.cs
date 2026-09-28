using Chaperone.Core;
using Xunit;

namespace Chaperone.Tests;

public class PairTrackerTests
{
    private static readonly PairKey Key = new(PlayerKey.FromContentId(1), PlayerKey.FromContentId(2));

    [Fact]
    public void DwellAccumulatesAcrossConsecutivePolls()
    {
        var t = new PairTracker();
        var ended = new List<PairState>();
        foreach (var now in new long[] { 0, 200, 400 })
        {
            t.BeginPoll();
            t.Observe(Key, now);
            t.EndPoll(now, 1500, ended);
        }

        Assert.Empty(ended);
        Assert.Equal(0.4, t.Live[Key].DwellSeconds, 3);
        Assert.Equal(0, t.Live[Key].StartedMs);
    }

    [Fact]
    public void FlickerShorterThanGraceKeepsPairLive()
    {
        var t = new PairTracker();
        var ended = new List<PairState>();
        t.BeginPoll(); t.Observe(Key, 0); t.EndPoll(0, 1500, ended);
        t.BeginPoll(); t.Observe(Key, 200); t.EndPoll(200, 1500, ended);
        // Not observed at 400..1400.
        foreach (var now in new long[] { 400, 600, 800, 1000, 1200, 1400 })
        {
            t.BeginPoll();
            t.EndPoll(now, 1500, ended);
        }

        Assert.Empty(ended);
        Assert.Single(t.Live);

        t.BeginPoll(); t.Observe(Key, 1600); t.EndPoll(1600, 1500, ended);
        Assert.Equal(1.6, t.Live[Key].DwellSeconds, 3);
    }

    [Fact]
    public void GapLongerThanGraceEndsPair()
    {
        var t = new PairTracker();
        var ended = new List<PairState>();
        t.BeginPoll(); t.Observe(Key, 0); t.EndPoll(0, 1500, ended);
        t.BeginPoll(); t.EndPoll(1500, 1500, ended);
        Assert.Empty(ended);
        t.BeginPoll(); t.EndPoll(1501, 1500, ended);
        Assert.Single(ended);
        Assert.Empty(t.Live);
        Assert.Equal(Key, ended[0].Key);
    }

    [Fact]
    public void EndSubjectRemovesAllPairsForThatSubject()
    {
        var t = new PairTracker();
        var ended = new List<PairState>();
        var subjectA = PlayerKey.FromContentId(10);
        var subjectB = PlayerKey.FromContentId(11);
        t.BeginPoll();
        t.Observe(new PairKey(PlayerKey.FromContentId(1), subjectA), 0);
        t.Observe(new PairKey(PlayerKey.FromContentId(2), subjectA), 0);
        t.Observe(new PairKey(PlayerKey.FromContentId(3), subjectB), 0);
        t.EndPoll(0, 1500, ended);

        t.EndSubject(subjectA, ended);
        Assert.Equal(2, ended.Count);
        Assert.Single(t.Live);
    }

    [Fact]
    public void ReleasedStatesAreReusedNotLeaked()
    {
        var t = new PairTracker();
        var ended = new List<PairState>();
        t.BeginPoll(); var first = t.Observe(Key, 0); t.EndPoll(0, 100, ended);
        t.BeginPoll(); t.EndPoll(500, 100, ended);
        Assert.Same(first, ended[0]);
        t.Release(ended[0]);
        ended.Clear();

        t.BeginPoll(); var second = t.Observe(Key, 1000); t.EndPoll(1000, 100, ended);
        Assert.Same(first, second);
        Assert.Equal(0, second.DwellSeconds);
        Assert.False(second.Alerted);
        Assert.Equal(1000, second.StartedMs);
    }
}
