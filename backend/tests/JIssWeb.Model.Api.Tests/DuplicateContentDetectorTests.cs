using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

/// <summary>
/// AI 版主阶段一：重复内容检测（零 LLM 成本）。
/// 同作者 + 同归一化哈希 + 时间窗内 → 重复；只写哈希不写正文。
/// </summary>
public class DuplicateContentDetectorTests : IAsyncLifetime
{
    private readonly ContentModerationFixture _fx = new();

    public Task InitializeAsync() => _fx.InitializeAsync();

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    private DuplicateContentDetector CreateDetector(int windowHours = 24)
    {
        var mongo = new OptionsStub<MongoSettings>(new MongoSettings { DatabaseName = _fx.DatabaseName });
        return new DuplicateContentDetector(
            _fx.Client, mongo,
            new OptionsStub<ForumContentModerationOptions>(new ForumContentModerationOptions { DuplicateWindowHours = windowHours }),
            NullLogger<DuplicateContentDetector>.Instance);
    }

    private IMongoCollection<ForumContentHashRecord> Hashes()
        => _fx.Db().GetCollection<ForumContentHashRecord>(ForumMongoSetup.ContentHashesCollectionName);

    private static ModerationRequest Req(string id, string author, string body = "重复的内容 body")
        => new("post", id, id, "general", author, "标题", body);

    [Fact]
    public async Task First_submission_is_not_duplicate_but_records_fingerprint()
    {
        var detector = CreateDetector();

        Assert.False(await detector.DetectAndRecordAsync(Req("dup-1", "author-a")));
        Assert.Equal(1, await Hashes().CountDocumentsAsync(FilterDefinition<ForumContentHashRecord>.Empty));
    }

    [Fact]
    public async Task Same_author_same_content_within_window_is_duplicate()
    {
        var detector = CreateDetector();
        await detector.DetectAndRecordAsync(Req("dup-2", "author-b"));

        Assert.True(await detector.DetectAndRecordAsync(Req("dup-3", "author-b")));
    }

    [Fact]
    public async Task Different_author_same_content_is_not_duplicate()
    {
        var detector = CreateDetector();
        await detector.DetectAndRecordAsync(Req("dup-4", "author-c"));

        Assert.False(await detector.DetectAndRecordAsync(Req("dup-5", "author-d")));
    }

    [Fact]
    public async Task Outside_window_is_not_duplicate()
    {
        var detector = CreateDetector(windowHours: 1);
        await Hashes().InsertOneAsync(new ForumContentHashRecord
        {
            AuthorSubId = "author-e",
            Hash = DuplicateContentDetector.HashOf("标题", "重复的内容 body"),
            TargetType = "post",
            TargetId = "dup-old",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-3),
        });

        Assert.False(await detector.DetectAndRecordAsync(Req("dup-6", "author-e")));
    }

    [Fact]
    public async Task Whitespace_and_case_are_normalized()
    {
        var detector = CreateDetector();
        await detector.DetectAndRecordAsync(Req("dup-7", "author-f", "Hello   WORLD\n正文"));

        Assert.True(await detector.DetectAndRecordAsync(Req("dup-8", "author-f", "  hello world 正文 ")));
    }

    [Fact]
    public async Task Same_target_repeated_check_does_not_self_match()
    {
        var detector = CreateDetector();
        await detector.DetectAndRecordAsync(Req("dup-9", "author-g"));

        Assert.False(await detector.DetectAndRecordAsync(Req("dup-9", "author-g")));
    }

    [Fact]
    public async Task Gate_flags_duplicate_without_hiding_content()
    {
        var detector = CreateDetector();
        var gate = new ContentModerationGate(
            _fx.Client,
            new OptionsStub<MongoSettings>(new MongoSettings { DatabaseName = _fx.DatabaseName }),
            _fx.Moderator,
            new OptionsStub<ForumContentModerationOptions>(new ForumContentModerationOptions { Enabled = true }),
            NullLogger<ContentModerationGate>.Instance,
            detector);

        _fx.Moderator.Verdict = ModerationVerdict.Ok;
        await gate.EvaluateAsync(Req("dup-10", "author-h"));
        var second = await gate.EvaluateAsync(Req("dup-11", "author-h"));

        Assert.Equal("published", second.State);
        var row = await _fx.Queue().Find(x => x.TargetId == "dup-11").FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Equal(ForumModerationSources.Duplicate, row!.Source);
        Assert.Equal(ForumModerationQueueStatuses.Flagged, row.Status);
    }
}
