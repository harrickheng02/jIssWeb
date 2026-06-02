using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JIssWeb.Model.Api.Services;

public sealed class EvidenceBundleInput
{
    public EvidenceManifest Manifest { get; init; } = new();
    public object Report { get; init; } = new();
    public object Target { get; init; } = new();
    public IReadOnlyList<object> ThreadAudit { get; init; } = Array.Empty<object>();
    public IReadOnlyList<EvidenceSanctionSummary> SanctionsSummary { get; init; } = Array.Empty<EvidenceSanctionSummary>();
}

public sealed class EvidenceManifest
{
    public int ExportVersion { get; init; } = 1;
    public string ReportId { get; init; } = "";
    public DateTime ExportedAtUtc { get; init; }
    public string ExportedBySub { get; init; } = "";
}

public sealed class EvidenceSanctionSummary
{
    public string Action { get; init; } = "";
    public string OperatorSub { get; init; } = "";
    public DateTime OccurredAtUtc { get; init; }
    public string? Reason { get; init; }
    public string? DurationPreset { get; init; }
    public string? TargetSub { get; init; }
}

public static class EvidenceZipBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static byte[] Build(EvidenceBundleInput input)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddJsonEntry(archive, "manifest.json", input.Manifest);
            AddJsonEntry(archive, "report.json", input.Report);
            AddJsonEntry(archive, "target.json", input.Target);
            AddJsonEntry(archive, "thread-audit.json", input.ThreadAudit);
            AddJsonEntry(archive, "sanctions-summary.json", input.SanctionsSummary);
            AddTextEntry(archive, "readme.txt", BuildReadme(input));
        }

        return ms.ToArray();
    }

    private static void AddJsonEntry(ZipArchive archive, string name, object payload)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        stream.Write(json, 0, json.Length);
    }

    private static void AddTextEntry(ZipArchive archive, string name, string text)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string BuildReadme(EvidenceBundleInput input)
    {
        var sb = new StringBuilder();
        sb.AppendLine("论坛举报证据包（运营复盘级）");
        sb.AppendLine("================================");
        sb.AppendLine();
        sb.AppendLine("【用途】");
        sb.AppendLine("本压缩包供版主/管理员在举报结案后离线存档与内部复盘使用。");
        sb.AppendLine("它将结案时的举报工单、被举报内容快照，以及关联的处置审计与处罚摘要");
        sb.AppendLine("打包在一起，便于在系统 retention 清理在线工单后仍能还原处理过程。");
        sb.AppendLine();
        sb.AppendLine("典型使用场景：");
        sb.AppendLine("- 有争议的结案留档，供后续内部核查或申诉复核");
        sb.AppendLine("- 工单在线记录过期删除前，导出一份本地/内网备份");
        sb.AppendLine("- 向上级或协作方说明「当时举报了什么、做了哪些处置」");
        sb.AppendLine();
        sb.AppendLine("【适用范围与限制】");
        sb.AppendLine("- 仅已结案（已处置/已驳回）的举报可导出；未结案调查过程不在此包内");
        sb.AppendLine("- 运营复盘级存档，不含哈希链/WORM 等法务级不可篡改存证");
        sb.AppendLine("- 含举报人/被举报人标识等内部信息，仅限授权人员保管，不得对外公开");
        sb.AppendLine("- 证据快照与在线工单按相同 retention 周期清理；过期后本包可能是唯一留存");
        sb.AppendLine();
        sb.AppendLine("【文件说明】（均为 UTF-8 编码）");
        sb.AppendLine("- manifest.json       导出元数据（版本、时间、导出人）");
        sb.AppendLine("- report.json         举报工单快照（举报人、理由、状态、结案时间等）");
        sb.AppendLine("- target.json         被举报帖/回复在结案时刻的内容快照（已删则为 tombstone）");
        sb.AppendLine("- thread-audit.json   关联操作审计（受理、结案、删帖、处罚等时间线；");
        sb.AppendLine("                      含同帖其他治理动作，便于还原帖级上下文）");
        sb.AppendLine("- sanctions-summary.json  本条举报关联的账号处罚摘要（按 reportId 过滤）");
        sb.AppendLine("- readme.txt          本说明文件");
        sb.AppendLine();
        sb.AppendLine("【本次导出】");
        sb.AppendLine($"ReportId: {input.Manifest.ReportId}");
        sb.AppendLine($"ExportedAtUtc: {input.Manifest.ExportedAtUtc:O}");
        sb.AppendLine($"ExportedBySub: {input.Manifest.ExportedBySub}");
        sb.AppendLine($"ExportVersion: {input.Manifest.ExportVersion}");
        return sb.ToString();
    }
}
