using Xunit;

namespace Chaperone.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads()
    {
        Assert.NotNull(typeof(Core.Marker).Assembly);
    }
}
