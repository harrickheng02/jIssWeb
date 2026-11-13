using JIssWeb.Accounting.Api.Services;
using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JIssWeb.Accounting.Api.Controllers;

[ApiController]
[Route("api/accounting/skills")]
public class SkillsController : ControllerBase
{
    private readonly ArenaSkillService _skillService;

    public SkillsController(ArenaSkillService skillService)
    {
        _skillService = skillService;
    }

    /// <summary>
    /// 技能目录（公开）。
    /// </summary>
    [HttpGet("catalog")]
    [AllowAnonymous]
    public IActionResult Catalog()
        => Ok(ApiResult<List<SkillCatalogItem>>.Ok(_skillService.Catalog().ToList()));

    /// <summary>
    /// 购买技能（本人）。扣费 + 库存登记；余额不足或 skillId 非法时拒绝。
    /// </summary>
    [HttpPost("purchase")]
    [Authorize]
    public async Task<IActionResult> Purchase([FromBody] SkillPurchaseRequest req)
    {
        var userId = User.GetUserId();
        var result = await _skillService.PurchaseAsync(userId, req.SkillId);
        if (!result.Success)
            return Ok(ApiResult<object>.Fail(result.Error ?? "purchase_failed"));
        return Ok(ApiResult<object>.Ok(new { skillId = req.SkillId }));
    }
}

public record SkillPurchaseRequest(string SkillId);
