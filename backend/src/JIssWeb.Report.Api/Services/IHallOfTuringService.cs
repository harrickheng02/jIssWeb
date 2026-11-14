using JIssWeb.Report.Api.Models;

namespace JIssWeb.Report.Api.Services;

public interface IHallOfTuringService
{
    /// <summary>公开列表：仅已揭露（eliminated）的奖章账号；附匿名幸存者聚合计数。</summary>
    Task<HallOfTuringViewModel> HallAsync(CancellationToken ct = default);
    /// <summary>档案：仅对「奖章 + eliminated」的 persona 返回；否则 null（控制器转 404）。</summary>
    Task<ArenaProfileViewModel?> ProfileAsync(string personaId, CancellationToken ct = default);
    /// <summary>全局周级 AIQI 趋势（直接读 arena_aiqi_results，无 LLM）。</summary>
    Task<List<AiqiTrendPoint>> AiqiTrendAsync(int limitWeeks = 12, CancellationToken ct = default);
}
