using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using JIssWeb.Common.Security;

namespace JIssWeb.Model.Api.Tests;

public class ForumRoleGovernanceTests : IClassFixture<ForumMeIntegrationFixture>
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumRoleGovernanceTests(ForumMeIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task Governance_debug_without_bearer_returns_401()
    {
        var r1 = await _fx.Client.GetAsync("/api/forum/__debug/moderator");
        var r2 = await _fx.Client.GetAsync("/api/forum/__debug/admin");
        Assert.Equal(HttpStatusCode.Unauthorized, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
    }

    [Fact]
    public async Task Moderator_debug_requires_moderator_or_admin_token()
    {
        var modUrl = "/api/forum/__debug/moderator";
        var rMember = await _fx.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, modUrl)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a", ForumRoleClaim.Member)) }
        });
        Assert.Equal(HttpStatusCode.Forbidden, rMember.StatusCode);
        var bodyMember = await rMember.Content.ReadAsStringAsync();
        using var docMember = JsonDocument.Parse(bodyMember);
        Assert.False(docMember.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("FORBIDDEN", docMember.RootElement.GetProperty("code").GetString());

        var rMod = await _fx.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, modUrl)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a", ForumRoleClaim.Moderator)) }
        });
        Assert.Equal(HttpStatusCode.OK, rMod.StatusCode);

        var rAdmin = await _fx.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, modUrl)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a", ForumRoleClaim.Admin)) }
        });
        Assert.Equal(HttpStatusCode.OK, rAdmin.StatusCode);
    }

    [Fact]
    public async Task Admin_debug_requires_admin_token()
    {
        var adminUrl = "/api/forum/__debug/admin";
        var rMod = await _fx.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, adminUrl)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a", ForumRoleClaim.Moderator)) }
        });
        Assert.Equal(HttpStatusCode.Forbidden, rMod.StatusCode);

        var rAdmin = await _fx.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, adminUrl)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a", ForumRoleClaim.Admin)) }
        });
        Assert.Equal(HttpStatusCode.OK, rAdmin.StatusCode);
    }

    [Fact]
    public async Task Invalid_forumRole_in_token_returns_401()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/forum/__debug/moderator")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateAccessToken("user-a", "superuser")) }
        };
        var r = await _fx.Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
