using System;
using System.Security.Claims;
using JIssWeb.Common.Security;

namespace JIssWeb.Common.Helpers;

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue("sub");
        var userId = principal.FindFirstValue("userId");
        if (string.IsNullOrWhiteSpace(sub))
            throw new UnauthorizedAccessException("invalid_token_sub_missing");
        if (!string.IsNullOrWhiteSpace(userId) && !string.Equals(sub, userId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("invalid_token_identity_mismatch");
        return sub;
    }

    /// <summary>
    /// Effective forum role: missing <c>forumRole</c> claim counts as <see cref="ForumPrincipalRole.Member"/>.
    /// Any non-empty claim value outside <c>member</c>/<c>moderator</c>/<c>admin</c> MUST be rejected earlier in the JWT bearer
    /// <c>OnTokenValidated</c> pipeline (<see cref="T:JIssWeb.Common.Hosting.WebApiHostExtensions"/>); this helper assumes that contract so unknown strings fall back to Member only as defense-in-depth.
    /// </summary>
    public static ForumPrincipalRole GetForumPrincipalRole(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ForumRoleClaim.Name);
        if (string.IsNullOrWhiteSpace(raw))
            return ForumPrincipalRole.Member;
        return raw.Trim().ToLowerInvariant() switch
        {
            ForumRoleClaim.Moderator => ForumPrincipalRole.Moderator,
            ForumRoleClaim.Admin => ForumPrincipalRole.Admin,
            ForumRoleClaim.Member => ForumPrincipalRole.Member,
            _ => ForumPrincipalRole.Member
        };
    }
}
