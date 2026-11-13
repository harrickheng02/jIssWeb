using JIssWeb.Accounting.Api.Mongo;
using JIssWeb.Accounting.Api.Models;
using JIssWeb.Common;
using MongoDB.Driver;

namespace JIssWeb.Accounting.Api.Services;

public record SkillPurchaseResult(bool Success, string? Error = null);

/// <summary>
/// 侦探技能购买：扣费 + 库存登记。余额不足或 skillId 非法时拒绝，不写库存、不扣费。
/// 余额扣减用条件更新（balance >= price）保证原子，避免并发超扣。
/// </summary>
public sealed class ArenaSkillService
{
    private readonly IMongoDatabase _db;
    private readonly IMongoCollection<UserPointsLedgerRecord> _ledger;
    private readonly IMongoCollection<UserPointsTransactionRecord> _transactions;
    private readonly IMongoCollection<UserSkillInventoryRecord> _inventory;

    public ArenaSkillService(IMongoDatabase db)
    {
        _db = db;
        _ledger = db.GetCollection<UserPointsLedgerRecord>(AccountingMongoSetup.LedgerCollectionName);
        _transactions = db.GetCollection<UserPointsTransactionRecord>(AccountingMongoSetup.TransactionsCollectionName);
        _inventory = db.GetCollection<UserSkillInventoryRecord>(AccountingMongoSetup.SkillInventoryCollectionName);
    }

    public IReadOnlyCollection<SkillCatalogItem> Catalog()
        => DetectiveSkillCatalog.Prices
            .Select(kv => new SkillCatalogItem(kv.Key, SkillName(kv.Key), kv.Value))
            .ToList();

    public async Task<SkillPurchaseResult> PurchaseAsync(string userId, string skillId, CancellationToken ct = default)
    {
        if (!DetectiveSkillCatalog.IsValidSkill(skillId))
            return new SkillPurchaseResult(false, "invalid_skill");

        var price = DetectiveSkillCatalog.PriceOf(skillId);

        // 条件扣减：仅当 balance >= price 时扣减，ModifiedCount==0 视为余额不足。
        var res = await _ledger.UpdateOneAsync(
            Builders<UserPointsLedgerRecord>.Filter.Eq(x => x.UserId, userId)
                & Builders<UserPointsLedgerRecord>.Filter.Gte(x => x.Balance, price),
            Builders<UserPointsLedgerRecord>.Update
                .Inc(x => x.Balance, -price).Set(x => x.UpdatedAtUtc, DateTime.UtcNow),
            cancellationToken: ct);

        if (res.ModifiedCount == 0)
            return new SkillPurchaseResult(false, "insufficient_balance");

        var updated = await _ledger.Find(Builders<UserPointsLedgerRecord>.Filter.Eq(x => x.UserId, userId))
            .FirstOrDefaultAsync(ct);
        var balanceAfter = updated?.Balance ?? 0;

        await _transactions.InsertOneAsync(new UserPointsTransactionRecord
        {
            TxId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Type = $"skill_purchase:{skillId}",
            Delta = -price,
            BalanceAfter = balanceAfter,
            OccurredAtUtc = DateTime.UtcNow,
        }, cancellationToken: ct);

        await _inventory.InsertOneAsync(new UserSkillInventoryRecord
        {
            UserId = userId,
            SkillId = skillId,
            GrantedAtUtc = DateTime.UtcNow,
            Consumed = false,
        }, cancellationToken: ct);

        return new SkillPurchaseResult(true);
    }

    internal static string SkillName(string skillId) => skillId switch
    {
        DetectiveSkillCatalog.BehaviorScan => "行为扫描",
        DetectiveSkillCatalog.DeathShield => "免死护盾",
        DetectiveSkillCatalog.PublicInquiry => "公开质询",
        _ => skillId,
    };
}

public record SkillCatalogItem(string SkillId, string Name, int Price);
