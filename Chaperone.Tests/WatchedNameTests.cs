using Chaperone.Core;
using Xunit;

namespace Chaperone.Tests;

public class WatchedNameTests
{
    [Fact]
    public void ParsesNameWithWorld()
    {
        Assert.True(WatchedName.TryParse("Some Body@Alpha", "Omega", out var result));
        Assert.Equal("Some Body", result.Name);
        Assert.Equal("Alpha", result.World);
    }

    [Fact]
    public void FallsBackToCurrentWorld()
    {
        Assert.True(WatchedName.TryParse("  some   body ", "Omega", out var result));
        Assert.Equal("some body", result.Name);
        Assert.Equal("Omega", result.World);
    }

    [Theory]
    [InlineData("Single")]
    [InlineData("Three Part Name")]
    [InlineData("a@b@c")]
    [InlineData("Some Body@")]
    [InlineData("")]
    public void RejectsBadInput(string input)
    {
        Assert.False(WatchedName.TryParse(input, "Omega", out _));
    }

    [Fact]
    public void EmptyFallbackWithNoWorldFails()
    {
        Assert.False(WatchedName.TryParse("Some Body", "", out _));
    }

    [Fact]
    public void EntryMatchesByContentIdOrNameAndWorld()
    {
        var entry = new WatchlistEntry { Name = "Some Body", World = "Alpha", WorldId = 33, ContentId = 0 };
        Assert.True(entry.Matches("Some Body", 33, 0));
        Assert.True(entry.Matches("some body", 33, 0));
        Assert.False(entry.Matches("Some Body", 34, 0));
        entry.ContentId = 777;
        Assert.True(entry.Matches("Renamed Person", 34, 777));
        Assert.True(entry.Matches("Some Body", 33, 0));
    }
}
