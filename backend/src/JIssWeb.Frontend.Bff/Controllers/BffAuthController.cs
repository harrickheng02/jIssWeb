using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using JIssWeb.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace JIssWeb.Frontend.Bff.Controllers;

[ApiController]
[Route("api/bff/auth")]
public class BffAuthController(
    IHttpClientFactory httpClientFactory,
    IConnectionMultiplexer redis,
    IOptions<DownstreamServicesOptions> options,
    IWebHostEnvironment env,
    ILogger<BffAuthController> logger) : ControllerBase
{
    private readonly DownstreamServicesOptions _opts = options.Value;
    private const string RefreshTokenCookieName = "bff_refresh";
    private const string LockKeyPrefix = "bff:refresh:lock:";
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(10);

    // ── Login ──────────────────────────────────────────────────────────────

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<AccessTokenResponse>>> Login(
        [FromBody] LoginRequest request)
    {
        var client = httpClientFactory.CreateClient();
        HttpResponseMessage resp;
        try
        {
            resp = await client.PostAsJsonAsync(
                $"{_opts.UserApiBaseUrl}/api/auth/login",
                new { request.Email, request.Password });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BFF login: upstream User API unreachable");
            return StatusCode(502, ApiResult.Fail("上游服务暂时不可用", "UPSTREAM_ERROR"));
        }

        var upstream = await resp.Content.ReadFromJsonAsync<ApiResult<AuthTokenPair>>();
        if (upstream is null)
            return StatusCode(502, ApiResult.Fail("上游响应解析失败", "UPSTREAM_PARSE_ERROR"));

        if (!upstream.Success || upstream.Data is null)
            return StatusCode((int)resp.StatusCode, ApiResult.Fail(upstream.Message ?? "登录失败", upstream.Code));

        SetRefreshCookie(upstream.Data.RefreshToken);

        return Ok(ApiResult<AccessTokenResponse>.Ok(new AccessTokenResponse
        {
            AccessToken = upstream.Data.AccessToken,
            AccessTokenExpiresAtUtc = upstream.Data.AccessTokenExpiresAtUtc
        }));
    }

    // ── Establish（验证邮箱 / 重置密码后建立 Cookie 会话）────────────────

    [HttpPost("establish")]
    [AllowAnonymous]
    public ActionResult<ApiResult<string>> Establish([FromBody] EstablishRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(ApiResult.Fail("无效的刷新令牌", "INVALID_TOKEN"));

        SetRefreshCookie(request.RefreshToken);
        return Ok(ApiResult<string>.Ok("ok"));
    }

    // ── Token（静默恢复）──────────────────────────────────────────────────

    [HttpGet("token")]
    [AllowAnonymous]
    [EnableRateLimiting("bff-auth")]
    public async Task<ActionResult<ApiResult<AccessTokenResponse>>> Token()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            ClearRefreshCookie();
            return Unauthorized(ApiResult.Fail("未登录", "UNAUTHORIZED"));
        }

        return await DoRefresh(refreshToken);
    }

    // ── Refresh ───────────────────────────────────────────────────────────

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("bff-auth")]
    public async Task<ActionResult<ApiResult<AccessTokenResponse>>> Refresh()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            ClearRefreshCookie();
            return Unauthorized(ApiResult.Fail("未登录", "UNAUTHORIZED"));
        }

        return await DoRefresh(refreshToken);
    }

    // ── Revoke ────────────────────────────────────────────────────────────

    [HttpPost("revoke")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResult<string>>> Revoke()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        ClearRefreshCookie();

        if (string.IsNullOrWhiteSpace(refreshToken))
            return Ok(ApiResult<string>.Ok("ok"));

        try
        {
            var client = httpClientFactory.CreateClient();
            // 转发 Bearer（若前端还持有 access token）
            var bearer = Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(bearer))
                client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(bearer);

            await client.PostAsJsonAsync(
                $"{_opts.UserApiBaseUrl}/api/auth/revoke",
                new { RefreshToken = refreshToken });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BFF revoke: upstream call failed; cookie already cleared");
        }

        return Ok(ApiResult<string>.Ok("revoked"));
    }

    // ── Shared helpers ────────────────────────────────────────────────────

    private async Task<ActionResult<ApiResult<AccessTokenResponse>>> DoRefresh(string refreshToken)
    {
        var lockKey = LockKeyPrefix + HashPrefix(refreshToken);
        var db = redis.GetDatabase();
        bool redisAvailable = true;
        bool acquired = false;

        try
        {
            acquired = await db.StringSetAsync(lockKey, "1", LockTtl, When.NotExists);
        }
        catch (RedisException ex)
        {
            // Redis 不可用时降级跳过分布式锁，记录警告后继续执行
            logger.LogWarning(ex, "BFF refresh: Redis unavailable, proceeding without distributed lock");
            redisAvailable = false;
            acquired = true;
        }

        if (!acquired)
        {
            // 并发 refresh：另一个请求正在持有锁，等待后让前端重试，不清 cookie
            await Task.Delay(300);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResult.Fail("会话刷新冲突，请重试", "REFRESH_CONFLICT"));
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            HttpResponseMessage resp;
            try
            {
                resp = await client.PostAsJsonAsync(
                    $"{_opts.UserApiBaseUrl}/api/auth/refresh",
                    new { RefreshToken = refreshToken });
            }
            catch (Exception ex)
            {
                // 瞬时故障（User API 不可用）：保留 cookie，让客户端稍后重试
                logger.LogWarning(ex, "BFF refresh: upstream User API unreachable");
                return StatusCode(502, ApiResult.Fail("上游服务暂时不可用", "UPSTREAM_ERROR"));
            }

            var upstream = await resp.Content.ReadFromJsonAsync<ApiResult<AuthTokenPair>>();
            if (upstream is null)
            {
                // 响应解析失败属于瞬时故障，保留 cookie
                return StatusCode(502, ApiResult.Fail("上游响应解析失败", "UPSTREAM_PARSE_ERROR"));
            }

            if (!upstream.Success || upstream.Data is null)
            {
                // refresh token 明确无效（已过期/已撤销）才清 cookie
                ClearRefreshCookie();
                return Unauthorized(ApiResult.Fail(upstream.Message ?? "会话已过期", upstream.Code ?? "REFRESH_INVALID"));
            }

            SetRefreshCookie(upstream.Data.RefreshToken);

            return Ok(ApiResult<AccessTokenResponse>.Ok(new AccessTokenResponse
            {
                AccessToken = upstream.Data.AccessToken,
                AccessTokenExpiresAtUtc = upstream.Data.AccessTokenExpiresAtUtc
            }));
        }
        finally
        {
            if (redisAvailable)
            {
                try { await db.KeyDeleteAsync(lockKey); }
                catch (RedisException) { /* best-effort cleanup */ }
            }
        }
    }

    private void SetRefreshCookie(string refreshToken)
    {
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = !env.IsDevelopment(),
            Path = "/api/bff/auth",
            MaxAge = TimeSpan.FromSeconds(_opts.RefreshTokenCookieMaxAgeSeconds)
        });
    }

    private void ClearRefreshCookie()
    {
        Response.Cookies.Append(RefreshTokenCookieName, "", new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = !env.IsDevelopment(),
            Path = "/api/bff/auth",
            MaxAge = TimeSpan.Zero
        });
    }

    private static string HashPrefix(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}

public record LoginRequest(string Email, string Password);
public record EstablishRequest(string RefreshToken);

public record AccessTokenResponse
{
    public string AccessToken { get; init; } = "";
    public DateTime AccessTokenExpiresAtUtc { get; init; }
}

public record AuthTokenPair
{
    public string AccessToken { get; init; } = "";
    public DateTime AccessTokenExpiresAtUtc { get; init; }
    public string RefreshToken { get; init; } = "";
    public DateTime RefreshTokenExpiresAtUtc { get; init; }
}
