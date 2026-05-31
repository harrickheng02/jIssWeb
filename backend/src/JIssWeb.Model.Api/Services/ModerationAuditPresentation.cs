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
            "report.updateStatus" => "处理举报",
            "report.resolve" => "结案举报（历史）",
            "report.statusChange" => "举报状态变更",
            "post.lockReplies" => "锁定回复",
            "post.unlockReplies" => "解除锁定回复",
            "post.setFeatured" => "加精",
            "post.unsetFeatured" => "取消精华",
            "post.modDelete" => "删除帖子",
            "reply.modDelete" => "删除回复",
            "user.warn" => "账号警告",
            "user.mute" => "账号禁言",
            "user.unmute" => "解除禁言",
            _ => "操作",
        };
    }
}
