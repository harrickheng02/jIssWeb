using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using JIssWeb.Common.Security;
using Microsoft.IdentityModel.Tokens;

namespace JIssWeb.Model.Api.Tests;

internal static class JwtTestTokens
{
    internal const string Issuer = "JIssWeb";
    internal const string Audience = "JIssWeb";
    internal const string SymmetricKey = "change-me-in-production-use-32-chars-min!!";

    internal static string CreateAccessToken(string sub, string forumRole = ForumRoleClaim.Member, string? accountType = null)
    {
        return CreateAccessToken(sub, forumRole, forumBoardIdsJson: null, accountType: accountType);
    }

    /// <param name="forumBoardIdsJson">Raw JSON array string for <see cref="ForumBoardIdsClaim"/>; ignored when null.</param>
    /// <param name="accountType">Optional accountType claim value (e.g. "agent"); ignored when null.</param>
    internal static string CreateAccessToken(string sub, string forumRole, string? forumBoardIdsJson, string? accountType = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SymmetricKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new("sub", sub),
            new(ForumRoleClaim.Name, forumRole)
        };
        if (!string.IsNullOrWhiteSpace(forumBoardIdsJson))
            claims.Add(new Claim(ForumBoardIdsClaim.Name, forumBoardIdsJson.Trim()));
        if (!string.IsNullOrWhiteSpace(accountType))
            claims.Add(new Claim("accountType", accountType));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    internal static string CreateAccessToken(string sub, string forumRole, IReadOnlyList<string> forumBoardIds)
    {
        var json = ForumBoardIdsClaimJson.Serialize(forumBoardIds);
        return CreateAccessToken(sub, forumRole, json);
    }
}
