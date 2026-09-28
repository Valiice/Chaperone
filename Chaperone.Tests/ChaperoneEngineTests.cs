using Chaperone.Core;
using Xunit;
using static Chaperone.Tests.SnapshotBuilder;

namespace Chaperone.Tests;

public class ChaperoneEngineTests
{
    // Distinct literal names, so the allocation test measures many live pairs without building
    // strings per poll.
    private static readonly string[] CrowdNames =
    [
        "Crowd Member01",
        "Crowd Member02",
        "Crowd Member03",
        "Crowd Member04",
        "Crowd Member05",
        "Crowd Member06",
        "Crowd Member07",
        "Crowd Member08",
        "Crowd Member09",
        "Crowd Member10",
        "Crowd Member11",
        "Crowd Member12",
        "Crowd Member13",
        "Crowd Member14",
        "Crowd Member15",
        "Crowd Member16",
        "Crowd Member17",
        "Crowd Member18",
        "Crowd Member19",
        "Crowd Member20",
        "Crowd Member21",
        "Crowd Member22",
        "Crowd Member23",
        "Crowd Member24",
        "Crowd Member25",
        "Crowd Member26",
        "Crowd Member27",
        "Crowd Member28",
        "Crowd Member29",
        "Crowd Member30",
        "Crowd Member31",
        "Crowd Member32",
        "Crowd Member33",
        "Crowd Member34",
        "Crowd Member35",
        "Crowd Member36",
        "Crowd Member37",
        "Crowd Member38",
        "Crowd Member39",
        "Crowd Member40",
        "Crowd Member41",
        "Crowd Member42",
        "Crowd Member43",
        "Crowd Member44",
        "Crowd Member45",
        "Crowd Member46",
        "Crowd Member47",
        "Crowd Member48",
        "Crowd Member49",
        "Crowd Member50",
    ];

    private static ChaperoneEngine Engine(double threshold = 3, int floor = 0) =>
        new(new EngineSettings { Policy = new PolicySettings { DwellThresholdSeconds = threshold, GlobalFloorSeconds = floor } });

    [Fact]
    public void StrangerTargetingFriendShowsRowThenAlertsAtThreshold()
    {
        var engine = Engine();
        var friend = Entry("Some Friend", 5);
        var list = new List<WatchlistEntry> { friend };
        var snap = new Snapshot();

        for (long t = 0; t <= 3000; t += 1000)
        {
            Fill(snap, t);
            Add(snap, 20, "Some Friend", 5, contentId: 555);
            Add(snap, 30, "Some Stranger", 7, target: 20);
            engine.Poll(snap, list);

            var view = Assert.Single(engine.Frame.Subjects);
            Assert.True(view.IsNearby);
            Assert.True(view.IsWatched);
            var row = Assert.Single(view.Live);
            Assert.Equal("Some Stranger", row.Name);
            Assert.Equal(t / 1000d, row.DwellSeconds, 3);
            Assert.Equal(t == 3000 ? 1 : 0, engine.Frame.Alerts.Count);
        }

        var alert = engine.Frame.Alerts[0];
        Assert.Equal("Some Stranger", alert.TargeterName);
        Assert.Equal("Some Friend", alert.SubjectName);
        Assert.Equal(5u, alert.SubjectWorldId);
    }

    [Fact]
    public void FriendNotNearbyIsListedButEmpty()
    {
        var engine = Engine();
        var list = new List<WatchlistEntry> { Entry("Some Friend", 5) };
        engine.Poll(New(0), list);
        var view = Assert.Single(engine.Frame.Subjects);
        Assert.False(view.IsNearby);
        Assert.Empty(view.Live);
        Assert.Equal("Some Friend", view.Name);
    }

    [Fact]
    public void LearnsContentIdAndMatchesRenamedFriend()
    {
        var engine = Engine();
        var friend = Entry("Some Friend", 5);
        var list = new List<WatchlistEntry> { friend };
        var snap = new Snapshot();

        Fill(snap, 0);
        Add(snap, 20, "Some Friend", 5, contentId: 555);
        engine.Poll(snap, list);
        Assert.Equal(555ul, friend.ContentId);
        Assert.True(engine.Frame.WatchlistChanged);

        Fill(snap, 200);
        Add(snap, 20, "New Name", 5, contentId: 555);
        engine.Poll(snap, list);
        Assert.False(engine.Frame.WatchlistChanged);
        Assert.True(engine.Frame.Subjects[0].IsNearby);
    }

    [Fact]
    public void ContentIdArrivingLateDoesNotSplitPair()
    {
        var engine = Engine(threshold: 100);
        var list = new List<WatchlistEntry> { Entry("Some Friend", 5, 555) };
        var snap = new Snapshot();

        Fill(snap, 0);
        Add(snap, 20, "Some Friend", 5, contentId: 555);
        Add(snap, 30, "Some Stranger", 7, target: 20, contentId: 0);
        engine.Poll(snap, list);

        Fill(snap, 200);
        Add(snap, 20, "Some Friend", 5, contentId: 555);
        Add(snap, 30, "Some Stranger", 7, target: 20, contentId: 9);
        engine.Poll(snap, list);

        var view = engine.Frame.Subjects[0];
        Assert.Single(view.Live);
        Assert.Equal(0.2, view.Live[0].DwellSeconds, 3);
        Assert.Equal(1, engine.Frame.LivePairs);
    }

    [Fact]
    public void CurrentTargetBecomesTransientSubjectAndRetargetDropsIt()
    {
        var engine = Engine(threshold: 100);
        var list = new List<WatchlistEntry>();
        var snap = new Snapshot();

        // Targeting A; stranger S also targets A.
        Fill(snap, 0, localTarget: 40);
        Add(snap, 40, "Person A", 5, contentId: 41);
        Add(snap, 50, "Person B", 5, contentId: 51);
        Add(snap, 30, "Some Stranger", 7, target: 40);
        engine.Poll(snap, list);
        var view = Assert.Single(engine.Frame.Subjects);
        Assert.True(view.IsCurrentTarget);
        Assert.False(view.IsWatched);
        Assert.Single(view.Live);
        Assert.Equal(PlayerKey.FromName("Person A", 5), engine.CurrentTargetKey);

        // Stranger stops; A gets history.
        Fill(snap, 200, localTarget: 40);
        Add(snap, 40, "Person A", 5, contentId: 41);
        Add(snap, 50, "Person B", 5, contentId: 51);
        Add(snap, 30, "Some Stranger", 7);
        engine.Poll(snap, list);
        Fill(snap, 2000, localTarget: 40);
        Add(snap, 40, "Person A", 5, contentId: 41);
        Add(snap, 50, "Person B", 5, contentId: 51);
        Add(snap, 30, "Some Stranger", 7);
        engine.Poll(snap, list);
        Assert.Single(engine.Frame.Subjects[0].History);

        // Retarget to B: B is the only subject and has no history. Target A again: history is gone.
        Fill(snap, 2200, localTarget: 50);
        Add(snap, 40, "Person A", 5, contentId: 41);
        Add(snap, 50, "Person B", 5, contentId: 51);
        Add(snap, 30, "Some Stranger", 7, target: 40);
        engine.Poll(snap, list);
        view = Assert.Single(engine.Frame.Subjects);
        Assert.Equal("Person B", view.Name);
        Assert.Empty(view.Live);
        Assert.Empty(view.History);
        Assert.Equal(0, engine.Frame.LivePairs);

        Fill(snap, 2400, localTarget: 40);
        Add(snap, 40, "Person A", 5, contentId: 41);
        Add(snap, 50, "Person B", 5, contentId: 51);
        engine.Poll(snap, list);
        Assert.Empty(engine.Frame.Subjects[0].History);

        // No target at all: no subjects.
        Fill(snap, 2600);
        Add(snap, 40, "Person A", 5, contentId: 41);
        engine.Poll(snap, list);
        Assert.Empty(engine.Frame.Subjects);
        Assert.True(engine.CurrentTargetKey.IsNone);
    }

    [Fact]
    public void CurrentTargetContentIdArrivingLateKeepsRows()
    {
        var engine = Engine(threshold: 100);
        var list = new List<WatchlistEntry>();
        var snap = new Snapshot();

        Fill(snap, 0, localTarget: 40);
        Add(snap, 40, "Person A", 5, contentId: 0);
        Add(snap, 30, "Some Stranger", 7, target: 40);
        engine.Poll(snap, list);

        Fill(snap, 200, localTarget: 40);
        Add(snap, 40, "Person A", 5, contentId: 41);
        Add(snap, 30, "Some Stranger", 7, target: 40);
        engine.Poll(snap, list);

        var row = Assert.Single(Assert.Single(engine.Frame.Subjects).Live);
        Assert.Equal(0.2, row.DwellSeconds, 3);
        Assert.Equal(PlayerKey.FromName("Person A", 5), engine.CurrentTargetKey);
    }

    [Fact]
    public void TargetingAWatchedFriendDoesNotDuplicateOrLogSelf()
    {
        var engine = Engine(threshold: 0);
        var list = new List<WatchlistEntry> { Entry("Some Friend", 5, 555) };
        var snap = new Snapshot();

        Fill(snap, 0, localTarget: 20);
        Add(snap, 20, "Some Friend", 5, contentId: 555);
        engine.Poll(snap, list);
        var view = Assert.Single(engine.Frame.Subjects);
        Assert.True(view.IsCurrentTarget);
        Assert.True(view.IsWatched);
        Assert.Empty(view.Live);
        Assert.Empty(engine.Frame.Alerts);

        engine.Settings.Policy.LogSelf = true;
        Fill(snap, 200, localTarget: 20);
        Add(snap, 20, "Some Friend", 5, contentId: 555);
        engine.Poll(snap, list);
        Assert.Single(engine.Frame.Subjects[0].Live);
        Assert.Equal("Local Player", engine.Frame.Subjects[0].Live[0].Name);
        Assert.Empty(engine.Frame.Alerts);
    }

    [Fact]
    public void WatchedFriendsTargetingEachOtherShowRowsButNeverAlert()
    {
        var engine = Engine(threshold: 0);
        var list = new List<WatchlistEntry> { Entry("Friend One", 5, 11), Entry("Friend Two", 5, 22) };
        var snap = new Snapshot();

        for (long t = 0; t <= 5000; t += 1000)
        {
            Fill(snap, t);
            Add(snap, 20, "Friend One", 5, contentId: 11, target: 21);
            Add(snap, 21, "Friend Two", 5, contentId: 22, target: 20);
            engine.Poll(snap, list);
            Assert.Empty(engine.Frame.Alerts);
        }

        Assert.Equal(2, engine.Frame.Subjects.Count);
        Assert.Single(engine.Frame.Subjects[0].Live);
        Assert.Single(engine.Frame.Subjects[1].Live);
    }

    [Fact]
    public void ResetAndForgetSubjectClearState()
    {
        var engine = Engine(threshold: 100);
        var friend = Entry("Some Friend", 5, 555);
        var list = new List<WatchlistEntry> { friend };
        var snap = new Snapshot();
        Fill(snap, 0);
        Add(snap, 20, "Some Friend", 5, contentId: 555);
        Add(snap, 30, "Some Stranger", 7, target: 20);
        engine.Poll(snap, list);
        Assert.Equal(1, engine.Frame.LivePairs);

        engine.ForgetSubject(friend.Key);
        Fill(snap, 200);
        engine.Poll(snap, new List<WatchlistEntry>());
        Assert.Equal(0, engine.Frame.LivePairs);
        Assert.Empty(engine.Frame.Subjects);

        engine.Poll(snap, list);
        engine.Reset();
        Assert.Equal(0, engine.Frame.LivePairs);
        Assert.Empty(engine.Frame.Subjects);
        Assert.Empty(engine.Frame.Alerts);
    }

    [Fact]
    public void SteadyStatePollAllocatesNothing()
    {
        var engine = Engine(threshold: 2);
        var list = new List<WatchlistEntry> { Entry("Friend One", 5, 11), Entry("Friend Two", 5, 22) };
        var snap = new Snapshot();

        void Build(long t)
        {
            Fill(snap, t, localTarget: 60);
            Add(snap, 20, "Friend One", 5, contentId: 11);
            Add(snap, 21, "Friend Two", 5, contentId: 22);
            Add(snap, 60, "Some Target", 5, contentId: 66);
            for (ulong i = 100; i < 150; i++)
            {
                Add(snap, i, CrowdNames[i - 100], 9, contentId: i, target: i % 3 == 0 ? 20ul : i % 3 == 1 ? 60ul : 0ul);
            }
        }

        // Warm up: first sightings allocate views, pair states and history lists.
        for (long t = 0; t < 5000; t += 200)
        {
            Build(t);
            engine.Poll(snap, list);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (long t = 5000; t < 45_000; t += 200)
        {
            Build(t);
            engine.Poll(snap, list);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(engine.Frame.LivePairs >= 30);
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void PairCooldownSurvivesPairEndingAndRestarting()
    {
        var engine = Engine(threshold: 3);
        var list = new List<WatchlistEntry> { Entry("Some Friend", 5, 555) };
        var snap = new Snapshot();

        int Run(long from, long to, bool targeting)
        {
            var alerts = 0;
            for (var t = from; t <= to; t += 200)
            {
                Fill(snap, t);
                Add(snap, 20, "Some Friend", 5, contentId: 555);
                Add(snap, 30, "Some Stranger", 7, target: targeting ? 20ul : 0ul);
                engine.Poll(snap, list);
                alerts += engine.Frame.Alerts.Count;
            }

            return alerts;
        }

        // First alert at the threshold.
        Assert.Equal(1, Run(0, 3000, targeting: true));

        // Absent for longer than the grace period: the pair ends into history.
        Assert.Equal(0, Run(3200, 5000, targeting: false));
        Assert.Equal(0, engine.Frame.LivePairs);
        Assert.Single(engine.Frame.Subjects[0].History);

        // Back and holding well past the threshold, still inside the 60 s cooldown: no alert.
        Assert.Equal(0, Run(5200, 10_000, targeting: true));
        Assert.Equal(1, engine.Frame.LivePairs);
        Assert.True(engine.Frame.Subjects[0].Live[0].Alerted);

        // Leave again, then return after the cooldown has passed: a second alert fires.
        Assert.Equal(0, Run(10_200, 12_000, targeting: false));
        Assert.Equal(1, Run(70_000, 73_000, targeting: true));
    }

    [Fact]
    public void ReturningTargeterLeavesHistoryAndIsLiveAgain()
    {
        var engine = Engine(threshold: 100);
        var list = new List<WatchlistEntry> { Entry("Some Friend", 5, 555) };
        var snap = new Snapshot();

        Fill(snap, 0);
        Add(snap, 20, "Some Friend", 5, contentId: 555);
        Add(snap, 30, "Some Stranger", 7, target: 20);
        engine.Poll(snap, list);

        foreach (var t in new long[] { 200, 2000 })
        {
            Fill(snap, t);
            Add(snap, 20, "Some Friend", 5, contentId: 555);
            Add(snap, 30, "Some Stranger", 7);
            engine.Poll(snap, list);
        }

        var view = engine.Frame.Subjects[0];
        Assert.Empty(view.Live);
        Assert.Equal("Some Stranger", Assert.Single(view.History).Name);

        Fill(snap, 2200);
        Add(snap, 20, "Some Friend", 5, contentId: 555);
        Add(snap, 30, "Some Stranger", 7, target: 20);
        engine.Poll(snap, list);

        view = engine.Frame.Subjects[0];
        Assert.Empty(view.History);
        Assert.Equal("Some Stranger", Assert.Single(view.Live).Name);
    }

    [Fact]
    public void WatchingTheCurrentTargetPreservesLivePairsAndHistory()
    {
        var engine = Engine(threshold: 2);
        var list = new List<WatchlistEntry>();
        var snap = new Snapshot();
        var alertCount = 0;

        // Person A (content id 41) is the current, unwatched target; a stranger targets them long
        // enough to alert once.
        for (long t = 0; t <= 3000; t += 1000)
        {
            Fill(snap, t, localTarget: 40);
            Add(snap, 40, "Person A", 5, contentId: 41);
            Add(snap, 30, "Some Stranger", 7, target: 40);
            engine.Poll(snap, list);
            alertCount += engine.Frame.Alerts.Count;
        }

        Assert.Equal(1, alertCount);
        var before = Assert.Single(engine.Frame.Subjects);
        Assert.True(before.IsCurrentTarget);
        Assert.False(before.IsWatched);
        var dwellBeforeWatch = Assert.Single(before.Live).DwellSeconds;

        // Add Person A to the watchlist while still targeted. The transient subject must become
        // the watched one in place: same live pair, same dwell, no re-alert.
        list.Add(Entry("Person A", 5, 41));
        Fill(snap, 3200, localTarget: 40);
        Add(snap, 40, "Person A", 5, contentId: 41);
        Add(snap, 30, "Some Stranger", 7, target: 40);
        engine.Poll(snap, list);

        var view = Assert.Single(engine.Frame.Subjects);
        Assert.True(view.IsWatched);
        Assert.True(view.IsCurrentTarget);
        Assert.Empty(engine.Frame.Alerts);
        var row = Assert.Single(view.Live);
        Assert.Equal("Some Stranger", row.Name);
        Assert.True(row.DwellSeconds > dwellBeforeWatch);
        Assert.Equal(1, engine.Frame.LivePairs);

        // Keep polling: dwell keeps accumulating on the same pair and the friend never re-alerts.
        Fill(snap, 3400, localTarget: 40);
        Add(snap, 40, "Person A", 5, contentId: 41);
        Add(snap, 30, "Some Stranger", 7, target: 40);
        engine.Poll(snap, list);

        view = Assert.Single(engine.Frame.Subjects);
        row = Assert.Single(view.Live);
        Assert.Equal(3.4, row.DwellSeconds, 3);
        Assert.Empty(engine.Frame.Alerts);
    }
}
