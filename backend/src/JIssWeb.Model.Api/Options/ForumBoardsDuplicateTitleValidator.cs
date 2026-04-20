using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Options;

internal sealed class ForumBoardsDuplicateTitleValidator : IValidateOptions<ForumBoardsOptions>
{
    private readonly ILogger<ForumBoardsDuplicateTitleValidator> _logger;

    public ForumBoardsDuplicateTitleValidator(ILogger<ForumBoardsDuplicateTitleValidator> logger)
    {
        _logger = logger;
    }

    public ValidateOptionsResult Validate(string? name, ForumBoardsOptions options)
    {
        var boards = options.Boards;
        if (boards is null || boards.Count < 2)
            return ValidateOptionsResult.Success;

        var dupGroups = boards
            .Select(b => b.Title?.Trim() ?? "")
            .Where(t => t.Length > 0)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        if (dupGroups.Count > 0)
        {
            var examples = string.Join("; ", dupGroups.Select(g => $"{g.Key} ({g.Count()} entries)"));
            _logger.LogWarning(
                "Forum:Boards has duplicate Title values (case-insensitive); ResolveBoardIdFromTitle picks the first match. Duplicates: {Examples}",
                examples);
        }

        return ValidateOptionsResult.Success;
    }
}
