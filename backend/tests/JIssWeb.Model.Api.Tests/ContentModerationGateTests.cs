using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// AI 版主阶段一内容门禁：开关、日上限、三档处置、超时 fail-open。
/// 铁律——任何不确定都必须放行，绝不阻断发布。
/// </summary>
public class ContentModerationGateTests : IAsyncLifetime
{
    private readonly ContentModerationFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task Disabled_makes_no_llm_call_and_no_queue_write()
    {
        var gate = _fx.CreateGate(new ForumContentModerationOptions { Enabled = false });
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.99, "广告");

        var decision = await gate.EvaluateAsync(ContentModerationFixture.Request());

        Assert.Equal("published", decision.State);
        Assert.False(decision.Enqueued);
        Assert.Equal(0, _fx.Moderator.Calls);
        Assert.Equal(0, await _fx.Queue().CountDocumentsAsync(FilterDefinition<ForumModerationQueueRecord>.Empty));
    }

    [Fact]
    public async Task High_confidence_violation_hides_content_and_enqueues_pending()
    {
        var gate = _fx.CreateGate();
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.95, "含推广链接");

        var decision = await gate.EvaluateAsync(ContentModerationFixture.Request("p-hide"));

        Assert.Equal("pending_review", decision.State);
        Assert.True(decision.Enqueued);

        var row = await _fx.Queue().Find(x => x.TargetId == "p-hide").FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Equal(ForumModerationQueueStatuses.Pending, row!.Status);
        Assert.Equal(ForumModerationSources.Llm, row.Source);
        Assert.Equal("post", row.TargetType);
        Assert.Equal("general", row.BoardId);
        Assert.Equal("author-1", row.AuthorSubId);
        Assert.Equal(ForumModerationVerdicts.Spam, row.Verdict);
        Assert.Equal(0.95, row.Confidence!.Value, 3);
        Assert.Equal("含推广链接", row.Reason);
    }

    [Fact]
    public async Task Medium_confidence_violation_keeps_content_public_and_flags()
    {
        var gate = _fx.CreateGate();
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.72, "疑似推广");

        var decision = await gate.EvaluateAsync(ContentModerationFixture.Request("p-flag"));

        Assert.Equal("published", decision.State);
        Assert.True(decision.Enqueued);

        var row = await _fx.Queue().Find(x => x.TargetId == "p-flag").FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Equal(ForumModerationQueueStatuses.Flagged, row!.Status);
    }

    [Fact]
    public async Task Verdict_outside_auto_hide_whitelist_is_only_flagged()
    {
        var gate = _fx.CreateGate();
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Other, 0.97, "其他违规");

        var decision = await gate.EvaluateAsync(ContentModerationFixture.Request("p-other"));

        Assert.Equal("published", decision.State);
        var row = await _fx.Queue().Find(x => x.TargetId == "p-other").FirstOrDefaultAsync();
        Assert.Equal(ForumModerationQueueStatuses.Flagged, row!.Status);
    }

    [Fact]
    public async Task Clean_content_passes_without_queue_write()
    {
        var gate = _fx.CreateGate();
        _fx.Moderator.Verdict = ModerationVerdict.Ok;

        var decision = await gate.EvaluateAsync(ContentModerationFixture.Request("p-clean"));

        Assert.Equal("published", decision.State);
        Assert.False(decision.Enqueued);
        Assert.Equal(1, _fx.Moderator.Calls);
        Assert.Equal(0, await _fx.Queue().CountDocumentsAsync(FilterDefinition<ForumModerationQueueRecord>.Empty));
    }

    [Fact]
    public async Task Timeout_fails_open_and_does_not_enqueue()
    {
        var gate = _fx.CreateGate(new ForumContentModerationOptions { Enabled = true, TimeoutMs = 50 });
        _fx.Moderator.DelayMs = 800;
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.99, "慢响应");

        var decision = await gate.EvaluateAsync(ContentModerationFixture.Request("p-slow"));

        Assert.Equal("published", decision.State);
        Assert.False(decision.Enqueued);
        Assert.Equal(0, await _fx.Queue().CountDocumentsAsync(FilterDefinition<ForumModerationQueueRecord>.Empty));
    }

    [Fact]
    public async Task Classifier_exception_fails_open()
    {
        var gate = _fx.CreateGate();
        _fx.Moderator.Throw = new InvalidOperationException("boom");

        var decision = await gate.EvaluateAsync(ContentModerationFixture.Request("p-boom"));

        Assert.Equal("published", decision.State);
        Assert.False(decision.Enqueued);
    }

    [Fact]
    public async Task Daily_cap_exhausted_passes_without_calling_llm()
    {
        var gate = _fx.CreateGate(new ForumContentModerationOptions { Enabled = true, DailyLlmCallCap = 2 });
        await _fx.SeedDailyCallsAsync(2);
        _fx.Moderator.Verdict = new ModerationVerdict(ForumModerationVerdicts.Spam, 0.99, "广告");

        var decision = await gate.EvaluateAsync(ContentModerationFixture.Request("p-capped"));

        Assert.Equal("published", decision.State);
        Assert.False(decision.Enqueued);
        Assert.Equal(0, _fx.Moderator.Calls);
    }

    [Fact]
    public async Task Cap_counter_increments_per_call_and_survives_new_gate_instance()
    {
        var options = new ForumContentModerationOptions { Enabled = true, DailyLlmCallCap = 3 };
        var first = _fx.CreateGate(options);
        _fx.Moderator.Verdict = ModerationVerdict.Ok;
        await first.EvaluateAsync(ContentModerationFixture.Request("p-1"));
        await first.EvaluateAsync(ContentModerationFixture.Request("p-2"));

        // 新实例模拟进程重启：计数来自集合，不依赖内存。
        var second = _fx.CreateGate(options);
        await second.EvaluateAsync(ContentModerationFixture.Request("p-3"));
        var capped = await second.EvaluateAsync(ContentModerationFixture.Request("p-4"));

        Assert.Equal("published", capped.State);
        Assert.Equal(3, _fx.Moderator.Calls);
        var row = await _fx.DailyCalls().Find(x => x.Day == DateTime.UtcNow.ToString("yyyy-MM-dd")).FirstOrDefaultAsync();
        Assert.Equal(3, row!.Count);
    }
}
