namespace JIssWeb.Model.Api.Services;

internal static class ModerationAuditPresentation
{
    internal static string ActionLabel(string? action)
    {
        var a = action?.Trim() ?? "";
        return a switch
        {
            "post.setSticky" => "置顶帖子",
            "post.unsetSticky" => "取消置顶",
            _ => "操作",
        };
    }
}
