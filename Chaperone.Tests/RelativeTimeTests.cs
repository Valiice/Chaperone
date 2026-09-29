using Chaperone.Core;
using Xunit;

namespace Chaperone.Tests;

public class RelativeTimeTests
{
    [Theory]
    [InlineData(0, "0s ago")]
    [InlineData(999, "0s ago")]
    [InlineData(1_000, "1s ago")]
    [InlineData(59_999, "59s ago")]
    [InlineData(60_000, "1m ago")]
    [InlineData(3_599_999, "59m ago")]
    [InlineData(3_600_000, "1h ago")]
    [InlineData(90_000_000, "25h ago")]
    public void FormatsAgoInLargestWholeUnit(long elapsedMs, string expected)
    {
        Assert.Equal(expected, RelativeTime.Ago(elapsedMs));
    }

    [Fact]
    public void NegativeElapsedClampsToZero()
    {
        Assert.Equal("0s ago", RelativeTime.Ago(-5_000));
    }

    [Theory]
    [InlineData(0.0, "0s")]
    [InlineData(2.4, "2s")]
    [InlineData(2.6, "3s")]
    [InlineData(125.0, "2m 5s")]
    [InlineData(3_725.0, "1h 2m")]
    public void FormatsDwell(double seconds, string expected)
    {
        Assert.Equal(expected, RelativeTime.Dwell(seconds));
    }
}
