using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Tests;

[Collection("ForumMe")]
public sealed class ForumReportRetentionPurgerTests : IClassFixture<ForumMeIntegrationFixture>
{
    private readonly ForumMeIntegrationFixture _fx;

    public ForumReportRetentionPurgerTests(ForumMeIntegrationFixture fx) => _fx = fx;

    [Fact]
    public async Task PurgeStaleClosed_deletes_old_closed_keeps_recent_and_pending()
    {
        var mongo = _fx.Factory.Services.GetRequiredService<IMongoClient>();
        var coll = mongo.GetDatabase(_fx.DatabaseName).GetCollection<ForumReportRecord>(ForumMongoSetup.ReportsCollectionName);
        var now = DateTime.UtcNow;
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

        await coll.InsertManyAsync(new[]
        {
            new ForumReportRecord
            {
                Id = "ret-old-resolved-" + suffix,
                ReporterSub = "rep-a-" + suffix,
                TargetType = "post",
                TargetId = "me-post-a",
                PostId = "me-post-a",
                BoardId = "general",
                BoardTitle = "综合",
                Status = ForumReportStatuses.Resolved,
                CreatedAtUtc = now.AddDays(-400),
                UpdatedAtUtc = now.AddDays(-200),
                HandledBySub = "mod",
                HandledAtUtc = now.AddDays(-200),
            },
            new ForumReportRecord
            {
                Id = "ret-new-resolved-" + suffix,
                ReporterSub = "rep-b-" + suffix,
                TargetType = "post",
                TargetId = "me-post-b",
                PostId = "me-post-b",
                BoardId = "general",
                BoardTitle = "综合",
                Status = ForumReportStatuses.Resolved,
                CreatedAtUtc = now.AddDays(-10),
                UpdatedAtUtc = now.AddDays(-5),
                HandledBySub = "mod",
                HandledAtUtc = now.AddDays(-5),
            },
            new ForumReportRecord
            {
                Id = "ret-pending-" + suffix,
                ReporterSub = "rep-c-" + suffix,
                TargetType = "post",
                TargetId = "me-post-tech",
                PostId = "me-post-tech",
                BoardId = "tech",
                BoardTitle = "技术",
                Status = ForumReportStatuses.Pending,
                CreatedAtUtc = now.AddDays(-400),
                UpdatedAtUtc = now.AddDays(-400),
                HandledBySub = null,
                HandledAtUtc = null,
            },
        });

        var deleted = await ForumReportRetentionPurger.PurgeStaleClosedAsync(coll, closedRetentionDays: 120, utcNow: now);

        Assert.Equal(1, deleted);

        var ids = await coll.Find(Builders<ForumReportRecord>.Filter.Empty).Project(x => x.Id).ToListAsync();
        Assert.Contains("ret-new-resolved-" + suffix, ids);
        Assert.Contains("ret-pending-" + suffix, ids);
        Assert.DoesNotContain("ret-old-resolved-" + suffix, ids);
    }
}
