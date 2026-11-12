using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JIssWeb.Frontend.Bff.Tests;

public class BffAuthTests : IClassFixture<BffWebAppFactory>
{
    private readonly BffWebAppFactory _factory;
    private readonly HttpClient _client;

    public BffAuthTests(BffWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Login ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithAccessTokenAndSetsCookie()
    {
        _factory.UserApiMock
            .Given(Request.Create().WithPath("/api/auth/login").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    success = true,
                    data = new
                    {
                        accessToken = "at_abc",
                        accessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
                        refreshToken = "rt_xyz",
                        refreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(7)
                    }
                }));

        _client.DefaultRequestHeaders.Add("X-BFF-Source", "web");

        var resp = await _client.PostAsJsonAsync("/api/bff/auth/login",
            new { email = "user@test.com", password = "P@ssword1" },
            GetJsonOptions());

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("at_abc", body.GetProperty("data").GetProperty("accessToken").GetString());

        // Cookie must be set
        Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies, c => c.Contains("bff_refresh") &&
            c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Login_MissingBffSourceHeader_Returns400()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bff/auth/login")
        {
            Content = JsonContent.Create(new { email = "a@b.com", password = "X" })
        };
        // Do NOT add X-BFF-Source header
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_TransparentlyReturnsUpstreamError()
    {
        _factory.UserApiMock
            .Given(Request.Create().WithPath("/api/auth/login").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(401)
                .WithBodyAsJson(new { success = false, message = "密码错误", code = "INVALID_CREDENTIALS" }));

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bff/auth/login")
        {
            Content = JsonContent.Create(new { email = "a@b.com", password = "wrong" })
        };
        req.Headers.Add("X-BFF-Source", "web");
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
    }

    // ── Token（静默恢复）────────────────────────────────────────────────────

    [Fact]
    public async Task Token_NoCookie_Returns401AndClearsCookie()
    {
        var resp = await _client.GetAsync("/api/bff/auth/token");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies, c => c.Contains("bff_refresh") &&
            c.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Token_ExpiredRefreshToken_Returns401()
    {
        _factory.UserApiMock
            .Given(Request.Create().WithPath("/api/auth/refresh").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(401)
                .WithBodyAsJson(new { success = false, message = "refreshToken 无效", code = "REFRESH_INVALID" }));

        var handler = new HttpClientHandler { UseCookies = true };
        var cookieClient = _factory.CreateClient();
        cookieClient.DefaultRequestHeaders.Add("Cookie", "bff_refresh=expired_token");

        var resp = await cookieClient.GetAsync("/api/bff/auth/token");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_NoCookie_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bff/auth/refresh");
        req.Headers.Add("X-BFF-Source", "web");
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Refresh_ValidCookie_Returns200WithNewAccessToken()
    {
        _factory.UserApiMock
            .Given(Request.Create().WithPath("/api/auth/refresh").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    success = true,
                    data = new
                    {
                        accessToken = "at_new",
                        accessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
                        refreshToken = "rt_new",
                        refreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(7)
                    }
                }));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", "bff_refresh=some_valid_token");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bff/auth/refresh");
        req.Headers.Add("X-BFF-Source", "web");
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("at_new", body.GetProperty("data").GetProperty("accessToken").GetString());
    }

    // ── Revoke ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_NoCookie_Returns200Idempotent()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bff/auth/revoke");
        req.Headers.Add("X-BFF-Source", "web");
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Revoke_WithCookie_ClearsCookieAndCallsUpstream()
    {
        _factory.UserApiMock
            .Given(Request.Create().WithPath("/api/auth/revoke").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { success = true, data = "revoked" }));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", "bff_refresh=some_valid_token");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bff/auth/revoke");
        req.Headers.Add("X-BFF-Source", "web");
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies, c => c.Contains("bff_refresh") &&
            c.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
    }

    // ── Establish（验证邮箱 / 重置密码后建立 Cookie 会话）────────────────────

    [Fact]
    public async Task Establish_ValidRefreshToken_Returns200AndSetsCookie()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bff/auth/establish")
        {
            Content = JsonContent.Create(new { refreshToken = "rt_from_verify" })
        };
        req.Headers.Add("X-BFF-Source", "web");
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies, c => c.Contains("bff_refresh") &&
            c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Establish_EmptyRefreshToken_Returns400()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bff/auth/establish")
        {
            Content = JsonContent.Create(new { refreshToken = "" })
        };
        req.Headers.Add("X-BFF-Source", "web");
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Establish_MissingBffSourceHeader_Returns400()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/bff/auth/establish")
        {
            Content = JsonContent.Create(new { refreshToken = "rt_xyz" })
        };
        // No X-BFF-Source header
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── CSRF ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/bff/auth/login")]
    [InlineData("/api/bff/auth/refresh")]
    [InlineData("/api/bff/auth/revoke")]
    [InlineData("/api/bff/auth/establish")]
    public async Task PostEndpoints_WithoutBffSourceHeader_Returns400(string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { })
        };
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private static JsonSerializerOptions GetJsonOptions() =>
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
