using JIssWeb.Common;
using JIssWeb.Model.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Controllers;

[ApiController]
[Route("api/forum")]
public class ForumConfigController : ControllerBase
{
    private readonly IOptions<ForumBoardsOptions> _options;

    public ForumConfigController(IOptions<ForumBoardsOptions> options)
    {
        _options = options;
    }

    [HttpGet("boards")]
    [AllowAnonymous]
    public ActionResult<ApiResult<List<ForumBoardItemDto>>> Boards()
    {
        var list = (_options.Value.Boards ?? new List<ForumBoardEntry>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Title))
            .Select(x => new ForumBoardItemDto { Id = x.Id.Trim(), Title = x.Title.Trim() })
            .ToList();
        return Ok(ApiResult<List<ForumBoardItemDto>>.Ok(list));
    }
}

public class ForumBoardItemDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
}
