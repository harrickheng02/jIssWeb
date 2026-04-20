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

    internal static string CreateAccessToken(string sub, string forumRole = ForumRoleClaim.Member)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SymmetricKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new("sub", sub),
            new(ForumRoleClaim.Name, forumRole)
        };
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
