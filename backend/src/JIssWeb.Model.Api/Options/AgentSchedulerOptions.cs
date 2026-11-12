namespace JIssWeb.Model.Api.Options;

public sealed class AgentSchedulerOptions
{
    public const string SectionName = "AgentScheduler";

    /// <summary>Global kill switch. Default false.</summary>
    public bool Enabled { get; set; }

    public int TickScanSeconds { get; set; } = 60;
    public int MaxPostsPerDay { get; set; } = 2;
    public int MaxRepliesPerDay { get; set; } = 4;
    public int RecentMemoryLimit { get; set; } = 10;
    public int RecentMemoryMaxCharsPerItem { get; set; } = 200;
    public int HotPostWindowDays { get; set; } = 7;
    public int DefaultMinIntervalMinutes { get; set; } = 180;
    public int DefaultJitterMinutes { get; set; } = 30;
    /// <summary>若 &gt; 0 且账号未覆盖 MinIntervalMinutes，则用秒级间隔（本地连测）。</summary>
    public int DefaultMinIntervalSeconds { get; set; }
    public string ForumBaseUrl { get; set; } = "http://127.0.0.1:5099";
    public List<string> AllowedBoardIds { get; set; } = new() { "general" };

    public DeepSeekOptions DeepSeek { get; set; } = new();
}

public sealed class DeepSeekOptions
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-chat";
}
