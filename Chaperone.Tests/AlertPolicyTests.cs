using Chaperone.Core;
using Xunit;

namespace Chaperone.Tests;

public class AlertPolicyTests
{
    private static PairState Pair(double dwell, ObservationFlags flags = ObservationFlags.None, bool watched = false)
    {
        var tracker = new PairTracker();
        tracker.BeginPoll();
        var state = tracker.Observe(new PairKey(PlayerKey.FromContentId(1), PlayerKey.FromContentId(2)), 0);
        state.DwellSeconds = dwell;
        state.Flags = flags;
        state.TargeterIsWatched = watched;
        return state;
    }

    [Fact]
    public void FiresOnceAtThresholdThenRespectsCooldown()
    {
        var policy = new AlertPolicy();
        var s = new PolicySettings { DwellThresholdSeconds = 3, PairCooldownSeconds = 60, GlobalFloorSeconds = 0 };
        var pair = Pair(2.9);
        Assert.False(policy.ShouldAlert(pair, s, 10_000));

        pair.DwellSeconds = 3.0;
        Assert.True(policy.ShouldAlert(pair, s, 10_000));
        policy.MarkAlerted(pair, 10_000);
        Assert.True(pair.Alerted);

        pair.DwellSeconds = 30;
        Assert.False(policy.ShouldAlert(pair, s, 69_999));
        Assert.True(policy.ShouldAlert(pair, s, 70_000));
    }

    [Fact]
    public void ThresholdZeroFiresImmediately()
    {
        var policy = new AlertPolicy();
        var s = new PolicySettings { DwellThresholdSeconds = 0, GlobalFloorSeconds = 0 };
        Assert.True(policy.ShouldAlert(Pair(0), s, 0));
    }

    [Fact]
    public void GlobalFloorBlocksBurst()
    {
        var policy = new AlertPolicy();
        var s = new PolicySettings { DwellThresholdSeconds = 0, GlobalFloorSeconds = 2 };
        var a = Pair(5);
        var b = Pair(5);
        Assert.True(policy.ShouldAlert(a, s, 1000));
        policy.MarkAlerted(a, 1000);
        Assert.False(policy.ShouldAlert(b, s, 2999));
        Assert.True(policy.ShouldAlert(b, s, 3000));
    }

    [Theory]
    [InlineData(ObservationFlags.Party)]
    [InlineData(ObservationFlags.Alliance)]
    [InlineData(ObservationFlags.Friend)]
    [InlineData(ObservationFlags.FreeCompany)]
    [InlineData(ObservationFlags.InCombat)]
    public void DefaultFiltersIgnoreKnownPeopleAndCombat(ObservationFlags flag)
    {
        var s = new PolicySettings();
        Assert.False(AlertPolicy.PassesFilters(Pair(10, flag), s));
    }

    [Fact]
    public void FiltersCanBeSwitchedOff()
    {
        var s = new PolicySettings
        {
            IgnoreParty = false, IgnoreAlliance = false, IgnoreFriends = false,
            IgnoreFreeCompany = false, IgnoreWatched = false, AlertInCombat = true,
        };
        var all = ObservationFlags.Party | ObservationFlags.Alliance | ObservationFlags.Friend | ObservationFlags.FreeCompany | ObservationFlags.InCombat;
        Assert.True(AlertPolicy.PassesFilters(Pair(10, all, watched: true), s));
    }

    [Fact]
    public void OtherWatchedPlayersAreIgnoredByDefault()
    {
        Assert.False(AlertPolicy.PassesFilters(Pair(10, watched: true), new PolicySettings()));
    }

    [Fact]
    public void StrangerPassesDefaultFilters()
    {
        Assert.True(AlertPolicy.PassesFilters(Pair(10), new PolicySettings()));
    }

    [Fact]
    public void ResetForgetsGlobalFloor()
    {
        var policy = new AlertPolicy();
        var s = new PolicySettings { DwellThresholdSeconds = 0, GlobalFloorSeconds = 10 };
        policy.MarkAlerted(Pair(1), 1000);
        policy.Reset();
        Assert.True(policy.ShouldAlert(Pair(1), s, 1001));
    }
}
