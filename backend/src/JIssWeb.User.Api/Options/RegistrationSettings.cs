namespace JIssWeb.User.Api.Options;

/// <summary>
/// 注册准入护栏配置。与 <c>EmailVerification</c> 段刻意分开：后者管「发信频率」，
/// 本段管「是否允许进人」——两者失效时的处置动作不同（调限流 vs 关总闸），
/// 混在一起会让运营者在紧急时找错开关。
/// </summary>
public sealed class RegistrationSettings
{
    public const string SectionName = "Registration";

    /// <summary>注册总闸。默认关闭：部署后默认不接受新注册，需运营者显式开启。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>全局每日成功注册上限。0 表示不限制。</summary>
    public int DailyCap { get; set; } = 50;

    /// <summary>单 IP 每日成功注册上限。0 表示不限制。</summary>
    public int PerIpDailyCap { get; set; } = 3;

    /// <summary>
    /// 日界时区。容器默认 UTC，「今天」的分界会落在北京时间早上 8 点，
    /// 运营者看到的今日注册数会在上午突然归零，故显式指定。
    /// </summary>
    public string TimeZoneId { get; set; } = "Asia/Shanghai";
}
