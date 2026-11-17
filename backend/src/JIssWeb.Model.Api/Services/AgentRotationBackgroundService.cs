using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Services;

/// <summary>
/// 账号轮换器定时兜底（V0.3）。以 <c>AgentRotation:IntervalHours</c> 为周期执行一次
/// <c>daily:&lt;yyyy-MM-dd&gt;</c> 补齐，并在启动时补跑一次。
/// 作用：覆盖「结算事件通道被 MaxPerRun 截断而未填满」与「结算钩子未执行」两种情形；
/// 与事件通道的重复由 <c>agent_rotation_runs.runKey</c> 唯一索引挡住。
/// </summary>
public sealed class AgentRotationBackgroundService : BackgroundService
{
    private readonly IAgentRotationService _rotation;
    private readonly IOptions<AgentRotationOptions> _options;
    private readonly ILogger<AgentRotationBackgroundService> _logger;

    public AgentRotationBackgroundService(
        IAgentRotationService rotation,
        IOptions<AgentRotationOptions> options,
        ILogger<AgentRotationBackgroundService> logger)
    {
        _rotation = rotation;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("AgentRotationBackgroundService 未启用（AgentRotation:Enabled=false），跳过调度。");
            return;
        }

        await SafeRunDailyAsync(stoppingToken).ConfigureAwait(false);

        var interval = TimeSpan.FromHours(Math.Max(1, _options.Value.IntervalHours));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await SafeRunDailyAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停机。
        }
    }

    /// <summary>执行一次当日兜底补齐；当日已执行过时返回 <c>null</c>。</summary>
    public async Task<AgentRotationRunRecord?> RunDailyAsync(CancellationToken ct = default)
    {
        if (!_options.Value.Enabled) return null;
        var dayKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return await _rotation.EnsurePoolAsync(
            AgentRotationTriggers.Daily, $"{AgentRotationTriggers.Daily}:{dayKey}", null, ct).ConfigureAwait(false);
    }

    private async Task SafeRunDailyAsync(CancellationToken ct)
    {
        try
        {
            await RunDailyAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 兜底通道失败不得让 HostedService 崩溃（默认行为会停 host）。
            _logger.LogError(ex, "Agent rotation daily fallback failed.");
        }
    }
}
