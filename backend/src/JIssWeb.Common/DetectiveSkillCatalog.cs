namespace JIssWeb.Common;

/// <summary>
/// 侦探技能目录常量（一次性消耗品）。Accounting.Api 负责购买扣费与库存；
/// Model.Api 论坛域效果端点经共享 Mongo 库 user_skill_inventory 校验同一 SkillId。
/// </summary>
public static class DetectiveSkillCatalog
{
    public const string BehaviorScan = "behavior_scan";
    public const string DeathShield = "death_shield";
    public const string PublicInquiry = "public_inquiry";

    public static IReadOnlyDictionary<string, int> Prices { get; } = new Dictionary<string, int>
    {
        [BehaviorScan] = 30,
        [DeathShield] = 50,
        [PublicInquiry] = 80,
    };

    public static bool IsValidSkill(string skillId) => Prices.ContainsKey(skillId);

    public static int PriceOf(string skillId) => Prices.TryGetValue(skillId, out var p) ? p : 0;
}
