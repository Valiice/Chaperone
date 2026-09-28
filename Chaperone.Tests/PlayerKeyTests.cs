using Chaperone.Core;
using Xunit;

namespace Chaperone.Tests;

public class PlayerKeyTests
{
    [Fact]
    public void ContentIdWinsWhenPresent()
    {
        var a = PlayerKey.From(42, "Some Body", 33);
        var b = PlayerKey.From(42, "Other Name", 99);
        Assert.Equal(a, b);
        Assert.Equal(PlayerKey.FromContentId(42), a);
    }

    [Fact]
    public void NameKeyIsStableAndWorldSensitive()
    {
        var a = PlayerKey.From(0, "Some Body", 33);
        var b = PlayerKey.From(0, "Some Body", 33);
        var c = PlayerKey.From(0, "Some Body", 34);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.False(a.IsNone);
    }

    [Fact]
    public void NameKeyNeverCollidesWithContentIdSpace()
    {
        // Name keys set the top bit; content ids never do.
        var nameKey = PlayerKey.FromName("Some Body", 33);
        Assert.True((nameKey.Value & 0x8000_0000_0000_0000) != 0);
    }

    [Fact]
    public void NoneIsZero()
    {
        Assert.True(PlayerKey.None.IsNone);
        Assert.Equal(0ul, PlayerKey.None.Value);
    }
}
