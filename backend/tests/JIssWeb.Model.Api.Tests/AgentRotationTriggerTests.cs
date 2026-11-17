using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using MongoDB.Driver;
using Moq;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// 账号轮换器触发通道（V0.3 Phase 3）：结算后钩子与日频兜底。
/// </summary>
[Collection("AgentRotation")]
public sealed class AgentRotationTriggerTests
{
    private readonly AgentRotationFixture _fx;

    public AgentRotationTriggerTests(AgentRotationFixture fx) => _fx = fx;

    private static AgentRotationOptions Options(
        bool enabled = true, int min = 2, int max = 12, int maxPerRun = 2)
        => new()
        {
            Enabled = enabled,
            MinActiveAgents = min,
            MaxActiveAgents = max,
            MaxPerRun = maxPerRun,
            ReplacementNicknamePrefix = "观察者",
            NicknameRetryLimit = 5,
        };

    private async Task ResetAsync()
    {
        await _fx.ClearAsync();
        _fx.AgentUsers.Invocations.Clear();
        _fx.AgentUsers.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ProvisionedAgent($"agent-{Guid.NewGuid():N}", "test-token"));
        _fx.AgentUsers.Setup(x => x.DisableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private Task<long> RunCountAsync()
        => _fx.Runs().CountDocumentsAsync(FilterDefinition<AgentRotationRunRecord>.Empty);

    [Fact]
    public async Task Daily_run_uses_day_scoped_run_key()
    {
        await ResetAsync();
        await _fx.EnsureProfileAsync();
        var bg = _fx.CreateBackground(Options(min: 2, maxPerRun: 2));

        var run = await bg.RunDailyAsync();

        Assert.NotNull(run);
        Assert.Equal(AgentRotationTriggers.Daily, run!.Trigger);
        Assert.Equal($"daily:{DateTime.UtcNow:yyyy-MM-dd}", run.RunKey);
        Assert.Equal(2, run.CreatedCount);
        Assert.Equal(2, run.ActiveCountAfter);
    }

    [Fact]
    public async Task Second_daily_run_in_same_day_is_skipped()
    {
        await ResetAsync();
        await _fx.EnsureProfileAsync();
        var bg = _fx.CreateBackground(Options(min: 2, maxPerRun: 2));

        var first = await bg.RunDailyAsync();
        var second = await bg.RunDailyAsync();

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(1, await RunCountAsync());
    }

    [Fact]
    public async Task Disabled_background_service_returns_null_without_writes()
    {
        await ResetAsync();
        await _fx.EnsureProfileAsync();
        var bg = _fx.CreateBackground(Options(enabled: false));

        var run = await bg.RunDailyAsync();

        Assert.Null(run);
        Assert.Equal(0, await RunCountAsync());
        _fx.AgentUsers.Verify(
            x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reveal_hook_swallows_provisioning_failures()
    {
        await ResetAsync();
        await _fx.EnsureProfileAsync();
        _fx.AgentUsers.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("user api down"));

        var svc = _fx.CreateRotation(Options(min: 2, maxPerRun: 2));
        var run = await svc.EnsureAfterRevealAsync("2026-W48");

        Assert.NotNull(run);
        Assert.Equal(0, run!.CreatedCount);
        Assert.Equal(2, run.FailedCount);
        Assert.Equal(2, run.Failures.Count);
    }

    [Fact]
    public async Task Reveal_hook_is_idempotent_per_week_key()
    {
        await ResetAsync();
        await _fx.EnsureProfileAsync();
        var svc = _fx.CreateRotation(Options(min: 2, maxPerRun: 2));

        var first = await svc.EnsureAfterRevealAsync("2026-W49");
        var second = await svc.EnsureAfterRevealAsync("2026-W49");

        Assert.NotNull(first);
        Assert.Equal("reveal:2026-W49", first!.RunKey);
        Assert.Equal(2, first.CreatedCount);
        Assert.Null(second);
        Assert.Equal(1, await RunCountAsync());
    }

    [Fact]
    public async Task Reveal_hook_does_nothing_when_rotation_disabled()
    {
        await ResetAsync();
        await _fx.EnsureProfileAsync();
        var svc = _fx.CreateRotation(Options(enabled: false));

        var run = await svc.EnsureAfterRevealAsync("2026-W50");

        Assert.Null(run);
        Assert.Equal(0, await RunCountAsync());
    }
}
