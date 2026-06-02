using System.Globalization;
using System.Text;
using JIssWeb.Model.Api.Controllers;

namespace JIssWeb.Model.Api.Services;

internal static class AuditFeedCsvBuilder
{
    internal static byte[] BuildUtf8WithBom(IReadOnlyList<ModerationAuditFeedItemDto> items)
    {
        var csv = BuildCsv(items);
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
    }

    internal static string BuildCsv(IReadOnlyList<ModerationAuditFeedItemDto> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("occurredAtUtc,actionLabel,operatorDisplayName,operatorSub,targetType,targetId,boardId,boardLabel,postId,reportId");
        foreach (var x in items)
        {
            sb.Append(CsvCell(x.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture)));
            sb.Append(',');
            sb.Append(CsvCell(x.ActionLabel));
            sb.Append(',');
            sb.Append(CsvCell(x.OperatorDisplayName));
            sb.Append(',');
            sb.Append(CsvCell(x.OperatorSub));
            sb.Append(',');
            sb.Append(CsvCell(x.TargetType));
            sb.Append(',');
            sb.Append(CsvCell(x.TargetId));
            sb.Append(',');
            sb.Append(CsvCell(x.BoardId));
            sb.Append(',');
            sb.Append(CsvCell(x.BoardLabel));
            sb.Append(',');
            sb.Append(CsvCell(x.PostId));
            sb.Append(',');
            sb.AppendLine(CsvCell(x.ReportId));
        }

        return sb.ToString();
    }

    private static string CsvCell(string? value)
    {
        var s = value ?? "";
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return s;
    }
}
