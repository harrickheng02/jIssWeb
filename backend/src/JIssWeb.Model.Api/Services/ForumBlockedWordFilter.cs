using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Services;

public sealed class ForumBlockedWordFilter : IForumBlockedWordFilter
{
    private readonly IOptions<ForumBlockedWordsOptions> _options;

    public ForumBlockedWordFilter(IOptions<ForumBlockedWordsOptions> options) => _options = options;

    public bool IsBlocked(string? title, string? body) => Evaluate(title, body) != BlockedWordEvaluation.Pass;

    public BlockedWordEvaluation Evaluate(string? title, string? body)
    {
        var opt = _options.Value;
        if (!opt.Enabled || opt.Words is null || opt.Words.Count == 0)
            return BlockedWordEvaluation.Pass;

        var trimmedTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        var trimmedBody = string.IsNullOrWhiteSpace(body) ? null : body.Trim();

        foreach (var word in opt.Words)
        {
            if (string.IsNullOrWhiteSpace(word))
                continue;
            var w = word.Trim();
            if (ContainsIgnoreCase(trimmedTitle, w) || ContainsIgnoreCase(trimmedBody, w))
            {
                return string.Equals(opt.Handling, ForumBlockedWordsOptions.HandlingReject, StringComparison.OrdinalIgnoreCase)
                    ? BlockedWordEvaluation.Reject
                    : BlockedWordEvaluation.Local;
            }
        }

        return BlockedWordEvaluation.Pass;
    }

    private static bool ContainsIgnoreCase(string? text, string word)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        return text.Contains(word, StringComparison.OrdinalIgnoreCase);
    }
}
