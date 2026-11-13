using System.Text;
using System.Text.Json;
using JIssWeb.Common;
using JIssWeb.Common.Helpers;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using JIssWeb.Model.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JIssWeb.Model.Api.Controllers;

/// <summary>
/// 图灵场静默指认端点。所有业务分支（成功、重复、目标不存在、自指认、参数错误、限流、鉴权失败、内部异常）
/// 一律返回字节级一致的 200（含响应头），不暴露目标身份、判定结果或记录条数（spec：统一响应 + 零感知）。
/// 鉴权失败不依赖 [Authorize]，而是读取 sub，缺失即静默 no-op，避免 401 泄露分支。
/// </summary>
[ApiController]
[Route("api/arena")]
public class ArenaAccusationsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IArenaAccusationService _service;
    private readonly IArenaAccusationRateLimiter _rateLimiter;

    public ArenaAccusationsController(
        IArenaAccusationService service,
        IArenaAccusationRateLimiter rateLimiter)
    {
        _service = service;
        _rateLimiter = rateLimiter;
    }

    [HttpPost("accusations")]
    public async Task<IActionResult> Post()
    {
        try
        {
            var sub = TryGetSub();
            if (sub is null)
                return UniformOk();

            string? targetUserId = null;
            try
            {
                using var reader = new StreamReader(Request.Body);
                var raw = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<ArenaAccusationRequest>(raw, JsonOptions);
                targetUserId = req?.TargetUserId;
            }
            catch
            {
                // 参数校验失败 → 统一 200。
                return UniformOk();
            }

            if (string.IsNullOrWhiteSpace(targetUserId))
                return UniformOk();

            // 自指认：静默忽略，不写记录、不泄露。
            if (string.Equals(sub, targetUserId, StringComparison.Ordinal))
                return UniformOk();

            if (!_rateLimiter.Allow(sub))
                return UniformOk();

            await _service.AccuseAsync(sub, targetUserId);
        }
        catch
        {
            // 任意内部异常 → 仍统一 200，不泄露分支（spec：业务分支字节级一致）。
        }

        return UniformOk();
    }

    private static IActionResult UniformOk() => new OkObjectResult(ApiResult<object>.Ok(new object()));

    private string? TryGetSub()
    {
        try
        {
            return User.GetUserId();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

public class ArenaAccusationRequest
{
    public string? TargetUserId { get; set; }
}
