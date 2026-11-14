using JIssWeb.Report.Api.Services;
using Xunit;

namespace JIssWeb.Report.Api.Tests;

/// <summary>
/// 猎手标识脱敏解析器测试（#37，T12 / D8.1）。
/// 验收：默认实现不暴露原始 UserId，输出确定性「侦探#6位小写十六进制」代号；
/// 空输入降级为侦探#unknown。
/// </summary>
public class MaskedHunterNameResolverTests
{
    private readonly MaskedHunterNameResolver _resolver = new();

    [Fact]
    public void Resolve_非空输入_输出侦探井号加6位小写十六进制()
    {
        var name = _resolver.Resolve("user-hunter-a");

        Assert.StartsWith("侦探#", name);
        var hex = name["侦探#".Length..];
        Assert.Equal(6, hex.Length);
        Assert.Equal(hex, hex.ToLowerInvariant());
        Assert.All(hex, c => Assert.True("0123456789abcdef".Contains(c)));
    }

    [Fact]
    public void Resolve_相同输入_输出确定性一致()
    {
        var a = _resolver.Resolve("user-hunter-a");
        var b = _resolver.Resolve("user-hunter-a");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Resolve_不同输入_输出不同代号()
    {
        var a = _resolver.Resolve("user-hunter-a");
        var b = _resolver.Resolve("user-hunter-b");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Resolve_空输入_降级为侦探井unknown()
    {
        Assert.Equal("侦探#unknown", _resolver.Resolve(""));
        Assert.Equal("侦探#unknown", _resolver.Resolve("   "));
        Assert.Equal("侦探#unknown", _resolver.Resolve(null!));
    }
}
