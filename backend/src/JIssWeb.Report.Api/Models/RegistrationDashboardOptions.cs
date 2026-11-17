namespace JIssWeb.Report.Api.Models;

/// <summary>
/// Report.Api 侧的注册护栏只读配置镜像。与 User.Api 的 Registration 段同名，
/// 但不引用其程序集——Report.Api 只做只读投影。
/// </summary>
public sealed class RegistrationDashboardOptions
{
    public const string SectionName = "Registration";

    public string TimeZoneId { get; set; } = "Asia/Shanghai";
    public int DailyCap { get; set; } = 50;
}
