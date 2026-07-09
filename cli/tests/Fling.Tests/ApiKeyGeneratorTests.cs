using Fling.Net;

namespace Fling.Tests;

public sealed class ApiKeyGeneratorTests
{
    [Fact]
    public void Generate_ReturnsBase64UrlWithNoPadding()
    {
        var key = ApiKeyGenerator.Generate();

        Assert.DoesNotContain("+", key);
        Assert.DoesNotContain("/", key);
        Assert.DoesNotContain("=", key);
    }

    [Fact]
    public void Generate_Is32BytesWorthOfEntropy()
    {
        var key = ApiKeyGenerator.Generate(32);

        // 32 bytes → ceil(32/3)*4 = 44 base64 chars, minus up to 2 padding chars
        Assert.InRange(key.Length, 42, 43);
    }

    [Fact]
    public void Generate_UniqueAcrossCalls()
    {
        var keys = Enumerable.Range(0, 100).Select(_ => ApiKeyGenerator.Generate()).ToHashSet();

        Assert.Equal(100, keys.Count);
    }
}
