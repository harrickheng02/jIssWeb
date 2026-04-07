namespace JIssWeb.Common.Helpers;

public static class StringHelper
{
    public static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
