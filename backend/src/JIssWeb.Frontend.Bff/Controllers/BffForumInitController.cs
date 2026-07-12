using JIssWeb.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JIssWeb.Frontend.Bff.Controllers;

[ApiController]
[Route("api/bff")]
[AllowAnonymous]
public class BffForumInitController(
    IHttpClientFactory httpClientFactory,
    IOptions<DownstreamServicesOptions> options,
    ILogger<BffForumInitController> logger) : ControllerBase
{
    private readonly DownstreamServicesOptions _opts = options.Value;

    [HttpGet("forum-init")]
    public async Task<ActionResult<ApiResult<ForumInitResponse>>> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? boardId = null,
        [FromQuery] string? q = null,
        [FromQuery] string? tag = null,
        [FromQuery] string? sort = null,
        [FromQuery] bool? featured = null)
    {
        var warnings = new List<string>();

        var boardsQuery = boardId != null ? $"?boardId={Uri.EscapeDataString(boardId)}" : "";
        var postsQuery = BuildPostsQuery(page, pageSize, boardId, q, tag, sort, featured);
        var tagsQuery = boardId != null ? $"?boardId={Uri.EscapeDataString(boardId)}" : "";

        var boardsTask = FetchJsonAsync<object[]>($"{_opts.ModelApiBaseUrl}/api/forum/boards{boardsQuery}", "boards", warnings);
        var announcementsTask = FetchJsonAsync<object[]>($"{_opts.ModelApiBaseUrl}/api/forum/announcements?limit=5", "announcements", warnings);
        var postsTask = FetchJsonAsync<object>($"{_opts.ModelApiBaseUrl}/api/forum/posts{postsQuery}", "posts", warnings);
        var tagsTask = FetchJsonAsync<string[]>($"{_opts.ModelApiBaseUrl}/api/forum/tags/popular{tagsQuery}", "popularTags", warnings);

        // unreadCount 已由 /bff/me 提供，forum-init 不重复请求
        await Task.WhenAll(boardsTask, announcementsTask, postsTask, tagsTask);

        var response = new ForumInitResponse
        {
            Boards = await boardsTask ?? [],
            Announcements = await announcementsTask ?? [],
            Posts = await postsTask,
            PopularTags = await tagsTask ?? [],
            Warnings = warnings.Count > 0 ? [.. warnings] : null,
        };

        return Ok(ApiResult<ForumInitResponse>.Ok(response));
    }

    private async Task<T?> FetchJsonAsync<T>(string url, string label, List<string> warnings)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            var resp = await client.GetAsync(url);
            var upstream = await resp.Content.ReadFromJsonAsync<ApiResult<T>>();
            if (resp.IsSuccessStatusCode && upstream?.Success == true)
                return upstream.Data;

            logger.LogWarning("BFF forum-init/{Label}: upstream returned {Status}", label, resp.StatusCode);
            warnings.Add(label);
            return default;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BFF forum-init/{Label}: request failed", label);
            warnings.Add(label);
            return default;
        }
    }

    private static string BuildPostsQuery(
        int page, int pageSize, string? boardId,
        string? q, string? tag, string? sort, bool? featured)
    {
        var parts = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(boardId)) parts.Add($"boardId={Uri.EscapeDataString(boardId)}");
        if (!string.IsNullOrWhiteSpace(q)) parts.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrWhiteSpace(tag)) parts.Add($"tag={Uri.EscapeDataString(tag)}");
        if (!string.IsNullOrWhiteSpace(sort)) parts.Add($"sort={Uri.EscapeDataString(sort)}");
        if (featured.HasValue) parts.Add($"featured={featured.Value.ToString().ToLower()}");
        return "?" + string.Join("&", parts);
    }
}

public record ForumInitResponse
{
    public object[] Boards { get; init; } = [];
    public object[] Announcements { get; init; } = [];
    public object? Posts { get; init; }
    public string[] PopularTags { get; init; } = [];
    public string[]? Warnings { get; init; }
}
