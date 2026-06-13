namespace JIssWeb.Model.Api.Options;

public sealed class ForumBlockedWordsOptions
{
    public const string SectionName = "Forum:BlockedWords";

    public const string HandlingReject = "reject";
    public const string HandlingLocal = "local";

    public bool Enabled { get; set; }
    public List<string> Words { get; set; } = new();
    /// <summary>When a blocked word matches: <c>reject</c> (400) or <c>local</c> (client-only storage, no persist).</summary>
    public string Handling { get; set; } = HandlingLocal;
}
