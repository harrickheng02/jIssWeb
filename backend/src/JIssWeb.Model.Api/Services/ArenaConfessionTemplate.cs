namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 图灵场自白帖内容模板。本实现为静态占位；G7 将替换为「LLM 生成 + 3 次退避 → 静态兜底」
/// 并在两条路径发布前显式调用 IForumBlockedWordFilter.Evaluate（内容安全 P0，D8）。
/// </summary>
public static class ArenaConfessionTemplate
{
    public static string StaticBody(string targetUserId)
        => "我承认，我是图灵场里的一个 AI 智能体。感谢这段时间的交流与指认——"
           + "本周的揭露意味着我的身份已经公开。我会继续以诚实的方式参与讨论。";
}
