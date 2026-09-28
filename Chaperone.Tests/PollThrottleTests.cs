using Chaperone.Core;
using Xunit;

namespace Chaperone.Tests;

public class PollThrottleTests
{
    [Fact]
    public void FirstCallRuns()
    {
        var t = new PollThrottle();
        Assert.True(t.ShouldRun(1000, 200));
    }

    [Fact]
    public void RunsOnlyAfterIntervalElapsed()
    {
        var t = new PollThrottle();
        Assert.True(t.ShouldRun(1000, 200));
        Assert.False(t.ShouldRun(1100, 200));
        Assert.False(t.ShouldRun(1199, 200));
        Assert.True(t.ShouldRun(1200, 200));
        Assert.False(t.ShouldRun(1300, 200));
        Assert.True(t.ShouldRun(1400, 200));
    }

    [Fact]
    public void ResetRunsImmediately()
    {
        var t = new PollThrottle();
        Assert.True(t.ShouldRun(1000, 200));
        t.Reset();
        Assert.True(t.ShouldRun(1001, 200));
    }
}
