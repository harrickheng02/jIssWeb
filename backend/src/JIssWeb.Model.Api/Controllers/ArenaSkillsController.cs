using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Common.Security;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JIssWeb.Model.Api.Controllers;

/// <summary>
/// 图灵场技能「效果」端点（论坛域，#35）。购买/扣费在 Accounting.Api。
/// 行为扫描与公开质询都要求调用者持有对应未核销技能，否则 403（SKILL_NOT_OWNED）。
/// 响应严守隐私红线：行为扫描只返回词云（无 targetUserId）；公开质询只返回生成的帖子 id。
/// </summary>
[ApiController]
[Route("api/arena/skills")]
[Authorize]
public sealed class ArenaSkillsController : ControllerBase
{
    private readonly ArenaSkillEffectsService _svc;

    public ArenaSkillsController(ArenaSkillEffectsService svc) => _svc = svc;

    /// <summary>
    /// 行为扫描：聚合目标近 7 日帖/回复词云。一次性消耗 behavior_scan。
    /// </summary>
    [HttpGet("behavior-scan")]
    public async Task<IActionResult> BehaviorScan([FromQuery] string targetUserId)
    {
        var caller = User.GetUserId();
        if (string.IsNullOrWhiteSpace(targetUserId) || targetUserId == caller)
            return BadRequest(ApiResult<object>.Fail("无效的目标用户", "INVALID_TARGET"));

        if (!await _svc.HasUnconsumedSkillAsync(caller, DetectiveSkillCatalog.BehaviorScan))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<object>.Fail("未持有行为扫描技能", "SKILL_NOT_OWNED"));

        var cloud = await _svc.BehaviorScanAsync(targetUserId);
        await _svc.ConsumeSkillAsync(caller, DetectiveSkillCatalog.BehaviorScan);
        return Ok(ApiResult<List<WordCloudItem>>.Ok(cloud));
    }

    /// <summary>
    /// 公开质询：生成公开提问帖 + 24h 回应任务（agent 调度感知）+ 核销 public_inquiry。
    /// </summary>
    [HttpPost("public-inquiry")]
    public async Task<IActionResult> PublicInquiry([FromBody] PublicInquiryRequest req)
    {
        var caller = User.GetUserId();
        if (req is null || string.IsNullOrWhiteSpace(req.TargetUserId) || req.TargetUserId == caller)
            return BadRequest(ApiResult<object>.Fail("无效的目标用户", "INVALID_TARGET"));
        if (string.IsNullOrWhiteSpace(req.Question))
            return BadRequest(ApiResult<object>.Fail("质询内容不能为空", "INVALID_INPUT"));

        if (!await _svc.HasUnconsumedSkillAsync(caller, DetectiveSkillCatalog.PublicInquiry))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResult<object>.Fail("未持有公开质询技能", "SKILL_NOT_OWNED"));

        var postId = await _svc.PublicInquiryAsync(caller, req.TargetUserId, req.Question.Trim());
        return Ok(ApiResult<PublicInquiryResponse>.Ok(new PublicInquiryResponse(postId)));
    }
}
