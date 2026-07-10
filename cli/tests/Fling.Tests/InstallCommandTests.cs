using Fling.Commands;

namespace Fling.Tests;

public sealed class InstallCommandTests
{
    [Fact]
    public void ResolveExePath_ReturnsSelf_WhenNoFlingwExists()
    {
        var result = InstallCommand.ResolveExePath();
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
    }
}
