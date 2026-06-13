namespace JIssWeb.Model.Api.Services;

public enum BlockedWordEvaluation
{
    Pass,
    Reject,
    Local,
}

public interface IForumBlockedWordFilter
{
    bool IsBlocked(string? title, string? body);

    BlockedWordEvaluation Evaluate(string? title, string? body);
}
