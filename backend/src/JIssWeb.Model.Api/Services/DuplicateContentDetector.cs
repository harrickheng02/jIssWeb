using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using JIssWeb.Common.Options;
using JIssWeb.Model.Api.Models;
using JIssWeb.Model.Api.Mongo;
using JIssWeb.Model.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace JIssWeb.Model.Api.Services;

public interface IDuplicateContentDetector
{
    /// <summary>
    /// 判断本次内容是否与同作者时间窗内的历史内容重复，并记录本次指纹。
    /// 只存哈希，不存正文。
    /// </summary>
    Task<bool> DetectAndRecordAsync(ModerationRequest request, CancellationToken ct = default);
}

/// <summary>
/// 重复内容检测：sha256(归一化标题 + 归一化正文)，同作者 + 窗口内命中即重复。
/// 纯本地计算，零 LLM 成本、零内容外发。
/// </summary>
public sealed class DuplicateContentDetector : IDuplicateContentDetector
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private readonly IMongoCollection<ForumContentHashRecord> _hashes;
    private readonly ForumContentModerationOptions _options;
    private readonly ILogger<DuplicateContentDetector> _logger;

    public DuplicateContentDetector(
        IMongoClient mongoClient, IOptions<MongoSettings> mongoOptions,
        IOptions<ForumContentModerationOptions> options,
        ILogger<DuplicateContentDetector> logger)
    {
        var db = mongoClient.GetDatabase(mongoOptions.Value.DatabaseName);
        _hashes = db.GetCollection<ForumContentHashRecord>(ForumMongoSetup.ContentHashesCollectionName);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> DetectAndRecordAsync(ModerationRequest request, CancellationToken ct = default)
    {
        var hash = HashOf(request.Title, request.Body);
        var cutoff = DateTime.UtcNow.AddHours(-Math.Max(1, _options.DuplicateWindowHours));

        var duplicate = await _hashes.Find(x =>
                x.AuthorSubId == request.AuthorSubId
                && x.Hash == hash
                && x.TargetId != request.TargetId
                && x.CreatedAtUtc >= cutoff)
            .AnyAsync(ct);

        try
        {
            await _hashes.InsertOneAsync(new ForumContentHashRecord
            {
                AuthorSubId = request.AuthorSubId,
                Hash = hash,
                TargetType = request.TargetType,
                TargetId = request.TargetId,
                CreatedAtUtc = DateTime.UtcNow,
            }, cancellationToken: ct);
        }
        catch (MongoWriteException ex)
        {
            // 指纹写入失败只影响下次查重，不应影响发布。
            _logger.LogWarning(ex, "Failed to record content hash for {TargetId}.", request.TargetId);
        }

        return duplicate;
    }

    public static string HashOf(string? title, string body)
    {
        var normalized = Normalize(title) + "\n" + Normalize(body);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return WhitespaceRun.Replace(value.Trim().ToLowerInvariant(), " ");
    }
}
