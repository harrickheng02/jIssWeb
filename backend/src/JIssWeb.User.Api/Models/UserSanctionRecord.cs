namespace JIssWeb.User.Api.Models;

public static class UserSanctionTypes
{
    public const string Warning = "warning";
    public const string Mute = "mute";
}

public static class UserSanctionDurationPresets
{
    public const string Hours24 = "24h";
    public const string Days7 = "7d";
    public const string Days30 = "30d";

    public static bool TryParse(string? raw, out string preset)
    {
        preset = (raw ?? "").Trim().ToLowerInvariant();
        return preset switch
        {
            Hours24 or Days7 or Days30 => true,
            _ => false,
        };
    }

    public static DateTime ComputeExpiresUtc(DateTime startsAtUtc, string preset) =>
        preset switch
        {
            Hours24 => startsAtUtc.AddHours(24),
            Days7 => startsAtUtc.AddDays(7),
            Days30 => startsAtUtc.AddDays(30),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown duration preset"),
        };
}

public class UserSanctionRecord
{
    public string Id { get; set; } = "";
    public string Sub { get; set; } = "";
    public string Type { get; set; } = "";
    public string Reason { get; set; } = "";
    public string OperatorSub { get; set; } = "";
    public string? ReportId { get; set; }
    public string? DurationPreset { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedBySub { get; set; }
    public string? RevokeReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
