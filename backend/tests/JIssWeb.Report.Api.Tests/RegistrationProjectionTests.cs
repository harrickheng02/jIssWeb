using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using JIssWeb.Report.Api.Models;
using JIssWeb.Report.Api.Services;

namespace JIssWeb.Report.Api.Tests;

/// <summary>
/// 注册流入面板：Report.Api 只从用户库 users 集合做只读投影，
/// 不依赖 User.Api 的可用性。
/// </summary>
[Collection("ReportDashboard")]
public sealed class RegistrationProjectionTests
{
    private readonly ReportDashboardFixture _fx;

    public RegistrationProjectionTests(ReportDashboardFixture fx) => _fx = fx;

    private ArenaDashboardService CreateService(int dailyCap = 50, bool withUserDb = true)
        => new(
            _fx.Database(),
            Options.Create(new ArenaDashboardRevealOptions { RevealHourLocal = 22, TimeZoneId = "Asia/Shanghai" }),
            new IsAgentSubResolver(_fx.Database()),
            withUserDb ? _fx.UserDatabase() : null,
            Options.Create(new RegistrationDashboardOptions
            {
                TimeZoneId = "Asia/Shanghai",
                DailyCap = dailyCap
            }));

    private async Task AddUserAsync(DateTime createdAtUtc, bool verified)
    {
        await _fx.UserDatabase().GetCollection<BsonDocument>("users").InsertOneAsync(new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId().ToString(),
            ["Email"] = $"{Guid.NewGuid():N}@example.com",
            ["CreatedAtUtc"] = createdAtUtc,
            ["EmailVerifiedAtUtc"] = verified ? (BsonValue)DateTime.UtcNow : BsonNull.Value,
        });
    }

    [Fact]
    public async Task Dashboard_projects_today_inflow_and_pending_verification()
    {
        await _fx.ClearUsersAsync();
        var now = DateTime.UtcNow;
        await AddUserAsync(now, verified: true);
        await AddUserAsync(now, verified: true);
        await AddUserAsync(now, verified: false);
        await AddUserAsync(now.AddDays(-3), verified: false); // 旧账号：不算今日，但计入待验证

        var vm = await CreateService().BuildAsync();

        Assert.Equal(3, vm.Registration.TodayRegistered);
        Assert.Equal(2, vm.Registration.TodayVerified);
        Assert.Equal(2, vm.Registration.PendingVerification);
    }

    [Fact]
    public async Task Dashboard_reports_cap_and_remaining_quota()
    {
        await _fx.ClearUsersAsync();
        await AddUserAsync(DateTime.UtcNow, verified: false);
        await AddUserAsync(DateTime.UtcNow, verified: false);

        var vm = await CreateService(dailyCap: 5).BuildAsync();

        Assert.Equal(5, vm.Registration.DailyCap);
        Assert.Equal(3, vm.Registration.RemainingToday);
    }

    [Fact]
    public async Task Unlimited_cap_reports_minus_one()
    {
        await _fx.ClearUsersAsync();

        var vm = await CreateService(dailyCap: 0).BuildAsync();

        Assert.Equal(0, vm.Registration.DailyCap);
        Assert.Equal(-1, vm.Registration.RemainingToday);
    }

    [Fact]
    public async Task Missing_user_database_degrades_to_zero_without_throwing()
    {
        var vm = await CreateService(withUserDb: false).BuildAsync();

        Assert.Equal(0, vm.Registration.TodayRegistered);
        Assert.Equal(0, vm.Registration.TodayVerified);
        Assert.Equal(0, vm.Registration.PendingVerification);
    }
}
