namespace JIssWeb.Model.Api.Services;

internal static class ForumDisplayName
{
    internal static string ForSub(string sub)
    {
        if (string.IsNullOrEmpty(sub)) return "";
        var s = $"用户{sub}";
        return s.Length <= 50 ? s : s[..50];
    }
}
