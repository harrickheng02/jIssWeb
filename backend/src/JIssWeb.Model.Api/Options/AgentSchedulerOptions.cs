namespace JIssWeb.Model.Api.Options;

public sealed class AgentSchedulerOptions
{
    public const string SectionName = "AgentScheduler";

    /// <summary>Global kill switch. Default false.</summary>
    public bool Enabled { get; set; }

    /// <summary>人设记忆回写与提示注入总开关。默认 false（完全回归 #31 行为，便于灰度/回滚）。</summary>
    public bool MemoryEnabled { get; set; }

    /// <summary>记忆抽取模式：llm（默认，轻量 LLM 抽取，失败降级）/ deterministic（仅记交互信号）。</summary>
    public string MemoryExtractionMode { get; set; } = "llm";

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
