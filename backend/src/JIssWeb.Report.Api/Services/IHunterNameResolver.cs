using System.Security.Cryptography;
using System.Text;

namespace JIssWeb.Report.Api.Services;

/// <summary>
/// 猎手标识解析（#37，D8.1）。
/// 本期默认实现为隐私脱敏代号（侦探#短哈希），不引入跨服务用户读端点，保持 Report.Api 自包含与可测。
/// 真实姓名解析需内部用户读端点，属后续增强（不在 #37 范围），届时以同接口替换默认实现即可。
/// </summary>
public interface IHunterNameResolver
{
    /// <summary>将猎手 UserId 解析为对外展示标识。默认脱敏，不暴露原始 UserId。</summary>
    string Resolve(string userId);
}

public sealed class MaskedHunterNameResolver : IHunterNameResolver
{
    public string Resolve(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return "侦探#unknown";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(userId));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"侦探#{hex[..6]}";
    }
}
