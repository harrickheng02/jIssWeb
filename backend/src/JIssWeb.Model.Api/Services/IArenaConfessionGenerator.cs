using JIssWeb.Model.Api.Models;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 图灵场自白帖生成器（G7）。LLM 生成路径失败时 3 次退避 → 静态模板兜底，
/// SHALL NOT 跳过揭露。生成结果须经 <see cref="IForumBlockedWordFilter"/> 校验后才发布（D8 P0）。
/// </summary>
public interface IArenaConfessionGenerator
{
    /// <summary>
    /// 为被揭露的 AI 账号生成自白帖正文。
    /// </summary>
    /// <param name="persona">被揭露账号的人设（含 Prompt / LlmProfileId）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>正文与来源（llm / static_fallback）。</returns>
    Task<ArenaConfessionGeneration> GenerateAsync(AgentPersonaRecord persona, CancellationToken ct = default);
}

/// <summary>自白帖生成结果：正文 + 来源标识。</summary>
public sealed record ArenaConfessionGeneration(string Body, string Source)
{
    /// <summary>LLM 生成。</summary>
    public const string Llm = "llm";
    /// <summary>LLM 失败 / 无档案，回退到静态模板。</summary>
    public const string StaticFallback = "static_fallback";
}
