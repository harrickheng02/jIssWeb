using System.Net.Http.Headers;
using JIssWeb.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JIssWeb.Frontend.Bff.Controllers;

[ApiController]
[Route("api/bff")]
[Authorize]
public class BffMeController(
    IHttpClientFactory httpClientFactory,
    IOptions<DownstreamServicesOptions> options,
    ILogger<BffMeController> logger) : ControllerBase
{
    private readonly DownstreamServicesOptions _opts = options.Value;

    [HttpGet("me")]
    public async Task<ActionResult<ApiResult<MeResponse>>> Get()
    {
        var bearer = Request.Headers.Authorization.ToString();
        var warnings = new List<string>();

        var profileTask = FetchProfileAsync(bearer, warnings);
        var forumTask = FetchForumStateAsync(bearer, warnings);

        await Task.WhenAll(profileTask, forumTask);

        var response = new MeResponse
        {
            Profile = await profileTask,
            Forum = await forumTask
        };

        var result = ApiResult<MeResponse>.Ok(response);
        if (warnings.Count > 0)
            return Ok(new { result.Success, result.Data, Warnings = warnings });

        return Ok(result);
    }

    private async Task<ProfileData?> FetchProfileAsync(string bearer, List<string> warnings)
    {
        try
        {
            var client = CreateAuthorizedClient(bearer);
            var resp = await client.GetAsync($"{_opts.CustomerApiBaseUrl}/api/profile");
            var upstream = await resp.Content.ReadFromJsonAsync<ApiResult<ProfileData>>();
            if (resp.IsSuccessStatusCode && upstream?.Success == true)
                return upstream.Data;

            logger.LogWarning("BFF me/profile: upstream returned {Status}", resp.StatusCode);
            warnings.Add("profile");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BFF me/profile: request failed");
            warnings.Add("profile");
            return null;
        }
    }

    private async Task<ForumState?> FetchForumStateAsync(string bearer, List<string> warnings)
    {
        try
        {
            var client = CreateAuthorizedClient(bearer);
            var resp = await client.GetAsync($"{_opts.ModelApiBaseUrl}/api/forum/notifications/unread-count");
            var upstream = await resp.Content.ReadFromJsonAsync<ApiResult<UnreadCountData>>();
            if (resp.IsSuccessStatusCode && upstream?.Success == true)
                return new ForumState { UnreadCount = upstream.Data?.Count ?? 0 };

            logger.LogWarning("BFF me/forum: upstream returned {Status}", resp.StatusCode);
            warnings.Add("forum");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BFF me/forum: request failed");
            warnings.Add("forum");
            return null;
        }
    }

    private HttpClient CreateAuthorizedClient(string bearer)
    {
        var client = httpClientFactory.CreateClient();
        if (!string.IsNullOrWhiteSpace(bearer))
            client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(bearer);
        return client;
    }
}

public record MeResponse
{
    public ProfileData? Profile { get; init; }
    public ForumState? Forum { get; init; }
}

public record ProfileData
{
    public string? Nickname { get; init; }
    public string? Gender { get; init; }
    public string? BirthDate { get; init; }
}

public record ForumState
{
    public int UnreadCount { get; init; }
}

internal record UnreadCountData
{
    public int Count { get; init; }
}
