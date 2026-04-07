using System.Security.Claims;
using System;

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
}
