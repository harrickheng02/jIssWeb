namespace JIssWeb.Common.Security;

/// <summary>JWT claim for human vs authorized agent accounts (see openspec agent-account-protocol / token-identity-consistency).</summary>
public static class AccountTypeClaim
{
    public const string Name = "accountType";

    public const string Human = "human";
    public const string Agent = "agent";

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var v = value.Trim().ToLowerInvariant();
        return v is Human or Agent;
    }
}
