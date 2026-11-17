using System.Text.RegularExpressions;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using MongoDB.Driver;
using Moq;

namespace JIssWeb.Model.Api.Tests;

[CollectionDefinition("AgentRotation")]
public sealed class AgentRotationCollection : ICollectionFixture<AgentRotationFixture>
{
}

/// <summary>
/// 账号轮换器（V0.3）核心补齐逻辑。每个用例自行清库，避免跨用例污染。
/// </summary>
[Collection("AgentRotation")]
public sealed class AgentRotationTests
{
    private readonly AgentRotationFixture _fx;

    public AgentRotationTests(AgentRotationFixture fx) => _fx = fx;

    private static AgentRotationOptions Options(
        bool enabled = true, int min = 6, int max = 12, int maxPerRun = 2, string prefix = "观察者")
        => new()
        {
            Enabled = enabled,
            MinActiveAgents = min,
            MaxActiveAgents = max,
            MaxPerRun = maxPerRun,
            ReplacementNicknamePrefix = prefix,
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

    private Task<long> ActiveCountAsync()
        => _fx.Personas().CountDocumentsAsync(
            Builders<AgentPersonaRecord>.Filter.Eq(x => x.State, AgentPersonaState.Active));

    private Task<long> RunCountAsync()
        => _fx.Runs().CountDocumentsAsync(FilterDefinition<AgentRotationRunRecord>.Empty);

    [Fact]
    public async Task Disabled_switch_produces_zero_side_effects()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        await _fx.InsertPersonaAsync(AgentPersonaState.Active, profileId);

        var svc = _fx.CreateRotation(Options(enabled: false));
        var run = await svc.EnsurePoolAsync(AgentRotationTriggers.Manual, "manual:disabled");

        Assert.Null(run);
        Assert.Equal(0, await RunCountAsync());
        Assert.Equal(1, await ActiveCountAsync());
        _fx.AgentUsers.Verify(
            x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sufficient_pool_is_a_noop()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        await _fx.InsertPersonaAsync(AgentPersonaState.Active, profileId);

        var svc = _fx.CreateRotation(Options(min: 1, max: 12, maxPerRun: 2));
        var run = await svc.EnsurePoolAsync(AgentRotationTriggers.Manual, "manual:enough");

        Assert.NotNull(run);
        Assert.Equal(0, run!.CreatedCount);
        Assert.Equal(0, run.GapBefore);
        Assert.Equal(1, await ActiveCountAsync());
    }

    [Fact]
    public async Task Gap_is_filled_up_to_per_run_cap()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        for (var i = 0; i < 3; i++) await _fx.InsertPersonaAsync(AgentPersonaState.Active, profileId);

        var svc = _fx.CreateRotation(Options(min: 6, max: 12, maxPerRun: 2));
        var run = await svc.EnsurePoolAsync(AgentRotationTriggers.Manual, "manual:gap");

        Assert.NotNull(run);
        Assert.Equal(3, run!.GapBefore);
        Assert.Equal(2, run.CreatedCount);
        Assert.Equal(0, run.FailedCount);
        Assert.Equal(5, run.ActiveCountAfter);
        Assert.Equal(5, await ActiveCountAsync());
        Assert.NotNull(run.CompletedAtUtc);
    }

    [Fact]
    public async Task Ceiling_blocks_further_growth()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        for (var i = 0; i < 3; i++) await _fx.InsertPersonaAsync(AgentPersonaState.Active, profileId);

        var svc = _fx.CreateRotation(Options(min: 6, max: 3, maxPerRun: 2));
        var run = await svc.EnsurePoolAsync(AgentRotationTriggers.Manual, "manual:ceiling");

        Assert.NotNull(run);
        Assert.Equal(3, run!.GapBefore);
        Assert.Equal(0, run.CreatedCount);
        Assert.Equal(3, await ActiveCountAsync());
    }

    [Fact]
    public async Task Replacement_carries_lineage_and_increments_generation()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        var eliminated = await _fx.InsertPersonaAsync(
            AgentPersonaState.Eliminated, profileId, nickname: "旧世代", generation: 2,
            updatedAtUtc: DateTime.UtcNow.AddHours(-1));
        await _fx.InsertPersonaAsync(AgentPersonaState.Active, profileId);

        var svc = _fx.CreateRotation(Options(min: 2, max: 12, maxPerRun: 1));
        var run = await svc.EnsurePoolAsync(AgentRotationTriggers.Reveal, "reveal:2026-W46");

        Assert.NotNull(run);
        Assert.Equal(1, run!.CreatedCount);
        var created = await _fx.Personas().Find(x => x.RotationRunKey == "reveal:2026-W46").FirstOrDefaultAsync();
        Assert.NotNull(created);
        Assert.Contains(eliminated.PersonaId, created!.InheritedFrom);
        Assert.Equal(3, created.Generation);
        Assert.Equal("reveal:2026-W46", created.RotationRunKey);
        Assert.Equal(profileId, created.LlmProfileId);
        // 调度状态同步初始化
        var state = await _fx.SchedulerStates().Find(x => x.Id == created.AgentUserId).FirstOrDefaultAsync();
        Assert.NotNull(state);
    }

    [Fact]
    public async Task No_lineage_source_still_yields_valid_persona()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();

        var svc = _fx.CreateRotation(Options(min: 1, max: 12, maxPerRun: 1));
        var run = await svc.EnsurePoolAsync(AgentRotationTriggers.Daily, "daily:2026-11-17");

        Assert.NotNull(run);
        Assert.Equal(1, run!.CreatedCount);
        var created = await _fx.Personas().Find(x => x.RotationRunKey == "daily:2026-11-17").FirstOrDefaultAsync();
        Assert.NotNull(created);
        Assert.Empty(created!.InheritedFrom);
        Assert.Equal(1, created.Generation);
    }

    [Fact]
    public async Task Nickname_does_not_leak_lineage()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        var eliminated = await _fx.InsertPersonaAsync(
            AgentPersonaState.Eliminated, profileId, nickname: "老前辈", generation: 5,
            updatedAtUtc: DateTime.UtcNow);

        var svc = _fx.CreateRotation(Options(min: 1, max: 12, maxPerRun: 1));
        await svc.EnsurePoolAsync(AgentRotationTriggers.Manual, "manual:nick");

        var created = await _fx.Personas().Find(x => x.RotationRunKey == "manual:nick").FirstOrDefaultAsync();
        Assert.NotNull(created);
        Assert.Matches(new Regex("^观察者[A-Z2-7]{4}$"), created!.Nickname);
        Assert.DoesNotContain("老前辈", created.Nickname);
        Assert.DoesNotContain("Gen-", created.Nickname);
        Assert.NotEqual(eliminated.Nickname, created.Nickname);
    }

    [Fact]
    public async Task Eliminated_persona_stays_eliminated()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        var eliminated = await _fx.InsertPersonaAsync(
            AgentPersonaState.Eliminated, profileId, updatedAtUtc: DateTime.UtcNow);

        var svc = _fx.CreateRotation(Options(min: 1, max: 12, maxPerRun: 1));
        await svc.EnsurePoolAsync(AgentRotationTriggers.Manual, "manual:revive");

        var after = await _fx.Personas().Find(x => x.Id == eliminated.AgentUserId).FirstOrDefaultAsync();
        Assert.NotNull(after);
        Assert.Equal(AgentPersonaState.Eliminated, after!.State);

        var created = await _fx.Personas().Find(x => x.RotationRunKey == "manual:revive").FirstOrDefaultAsync();
        Assert.NotNull(created);
        Assert.NotEqual(eliminated.AgentUserId, created!.AgentUserId);
        _fx.AgentUsers.Verify(x => x.DisableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Duplicate_run_key_does_not_double_create()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        var svc = _fx.CreateRotation(Options(min: 4, max: 12, maxPerRun: 2));

        var first = await svc.EnsurePoolAsync(AgentRotationTriggers.Reveal, "reveal:2026-W47");
        var second = await svc.EnsurePoolAsync(AgentRotationTriggers.Reveal, "reveal:2026-W47");

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(2, first!.CreatedCount);
        Assert.Equal(2, await ActiveCountAsync());
        Assert.Equal(1, await RunCountAsync());
    }

    [Fact]
    public async Task Single_failure_does_not_abort_batch()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        _fx.AgentUsers.SetupSequence(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProvisionedAgent?)null)
            .ReturnsAsync(() => new ProvisionedAgent($"agent-{Guid.NewGuid():N}", "token"));

        var svc = _fx.CreateRotation(Options(min: 2, max: 12, maxPerRun: 2));
        var run = await svc.EnsurePoolAsync(AgentRotationTriggers.Manual, "manual:partial");

        Assert.NotNull(run);
        Assert.Equal(1, run!.CreatedCount);
        Assert.Equal(1, run.FailedCount);
        Assert.Single(run.Failures);
        Assert.Equal(1, await ActiveCountAsync());
    }

    [Fact]
    public async Task Persona_write_failure_disables_new_account()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        var occupied = await _fx.InsertPersonaAsync(AgentPersonaState.Active, profileId);
        _fx.AgentUsers.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProvisionedAgent(occupied.AgentUserId, "token"));

        var svc = _fx.CreateRotation(Options(min: 2, max: 12, maxPerRun: 1));
        var run = await svc.EnsurePoolAsync(AgentRotationTriggers.Manual, "manual:compensate");

        Assert.NotNull(run);
        Assert.Equal(0, run!.CreatedCount);
        Assert.Equal(1, run.FailedCount);
        _fx.AgentUsers.Verify(x => x.DisableAsync(occupied.AgentUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Null_interests_on_legacy_source_does_not_break_provisioning()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        var eliminated = await _fx.InsertPersonaAsync(
            AgentPersonaState.Eliminated, profileId, updatedAtUtc: DateTime.UtcNow);
        // 旧文档可能缺 Interests / PostingStyle 字段，反序列化后为 null。
        await _fx.Personas().UpdateOneAsync(
            x => x.Id == eliminated.AgentUserId,
            Builders<AgentPersonaRecord>.Update
                .Set(x => x.Interests, (List<string>?)null)
                .Set(x => x.PostingStyle, (AgentPostingStyle?)null));

        var svc = _fx.CreateRotation(Options(min: 1, max: 12, maxPerRun: 1));
        var run = await svc.EnsurePoolAsync(AgentRotationTriggers.Manual, "manual:legacy");

        Assert.NotNull(run);
        Assert.Equal(1, run!.CreatedCount);
        Assert.Equal(0, run.FailedCount);
    }

    [Fact]
    public async Task Status_reports_live_water_level()
    {
        await ResetAsync();
        var profileId = await _fx.EnsureProfileAsync();
        await _fx.InsertPersonaAsync(AgentPersonaState.Active, profileId);
        await _fx.InsertPersonaAsync(AgentPersonaState.Eliminated, profileId);

        var svc = _fx.CreateRotation(Options(min: 6, max: 12, maxPerRun: 2));
        var status = await svc.GetStatusAsync();

        Assert.True(status.Enabled);
        Assert.Equal(1, status.ActiveCount);
        Assert.Equal(6, status.MinActiveAgents);
        Assert.Equal(12, status.MaxActiveAgents);
        Assert.Equal(2, status.MaxPerRun);
        Assert.Equal(5, status.Gap);
    }
}
