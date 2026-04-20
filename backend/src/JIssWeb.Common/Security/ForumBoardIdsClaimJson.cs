using System.Text.Json;

namespace JIssWeb.Common.Security;

public static class ForumBoardIdsClaimJson
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Serializes board ids for the <see cref="ForumBoardIdsClaim"/> value (JSON array of strings).
    /// </summary>
    public static string Serialize(IReadOnlyList<string> boardIds)
    {
        var list = boardIds.Select(s => (s ?? "").Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return JsonSerializer.Serialize(list);
    }

    public static bool TryDeserialize(string raw, out List<string> boardIds)
    {
        boardIds = new List<string>();
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(raw.Trim(), JsonOptions);
            if (arr is null)
                return false;
            foreach (var x in arr)
            {
                var t = (x ?? "").Trim();
                if (t.Length > 0)
                    boardIds.Add(t);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
