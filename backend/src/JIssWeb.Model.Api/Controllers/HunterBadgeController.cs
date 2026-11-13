using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Controllers;

/// <summary>
/// 图灵猎手徽章只读端点（#34）。只返回当前认证用户本人的徽章记录，
/// SHALL NOT 暴露其他用户徽章、targetUserId 或任何可反推被揭露账号的字段。
/// 公告写入端点不存在：聚合公告由后台服务直接写 forum_announcements（零门禁、写入即可见）。
/// </summary>
[ApiController]
[Route("api/arena/hunter-badge")]
[Authorize]
public sealed class HunterBadgeController : ControllerBase
{
    private readonly IMongoCollection<ArenaHunterBadgeRecord> _badges;

    public HunterBadgeController(IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _badges = db.GetCollection<ArenaHunterBadgeRecord>(ArenaMongoSetup.HunterBadgesCollectionName);
    }

    /// <summary>返回当前认证用户的徽章；无徽章时返回 data=null（200，非 404）。</summary>
    [HttpGet("me")]
    public async Task<ActionResult<ApiResult<HunterBadgeItemDto?>>> Me(CancellationToken cancellationToken)
    {
        string caller;
        try
        {
            caller = User.GetUserId(); // JWT sub 为权威标识；缺 sub / sub≠userId 时抛 UnauthorizedAccessException。
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(ApiResult<HunterBadgeItemDto?>.Fail("未认证", "UNAUTHORIZED"));
        }

        var badge = await _badges
            .Find(Builders<ArenaHunterBadgeRecord>.Filter.Eq(b => b.UserId, caller))
            .FirstOrDefaultAsync(cancellationToken);
        if (badge is null)
            return Ok(ApiResult<HunterBadgeItemDto?>.Ok(null));

        return Ok(ApiResult<HunterBadgeItemDto?>.Ok(new HunterBadgeItemDto
        {
            BadgeId = badge.BadgeId,
            TargetWeekKey = badge.TargetWeekKey,
            BountyPoints = badge.BountyPoints,
            AwardedAtUtc = badge.AwardedAtUtc,
        }));
    }
}

/// <summary>徽章只读投影：不含 UserId 之外的账号标识，SHALL NOT 含被揭露目标信息。</summary>
public sealed class HunterBadgeItemDto
{
    public string BadgeId { get; set; } = "";
    public string TargetWeekKey { get; set; } = "";
    public int BountyPoints { get; set; }
    public DateTime AwardedAtUtc { get; set; }
}
