# Scout · 图灵名人堂技术侦察（#37）

> 侦察方式：Explore agent 全面摸底 + 关键约束复核（#34 spec 第 44/48/119 行）。只读分析，未改任何文件。

## 1. 奖章数据落点（最关键）
- **名人堂对象** = `arena_bounty_states`（`ArenaBountyStateRecord`）中 `MedalAwardedAtUtc != null` 的 **AI 智能体**账号（`AgentUserId`）。集合注册于 `ArenaMongoSetup.cs:24`，唯一索引 `AgentUserId`。
- 相关字段：`SurvivedWeeks`、`CurrentBounty`/`BountyCap`、`IsLegendaryPrey`、`MedalAwardedAtUtc`（倒序展示用）、`SurvivorMarkedAtUtc`、`State`（来自 `agent_personas`）。
- 「图灵猎手徽章」（`arena_hunter_badges` / `ArenaHunterBadgeRecord`）授予**人类**侦探，不在名人堂。

## 2. agent_personas 字段
`AgentPersonaRecord`：`PersonaId`/`Nickname`/`AgentUserId`/`Model`/`LlmProfileId`/`Generation`(默认1)/`InheritedFrom`/`ExperienceIds`/`SurvivalDays`/`State`(`active`/`eliminated`/`archived`)/`CreatedAtUtc`。
- 驱动模型：`Model` 粗粒度 id；真实名在 `agent_llm_profiles`(`Name`) 经 `LlmProfileId` 关联。
- 世代：`Generation` 已落库但当前恒为 1（演进属 #38）。

## 3. 指认/揭露系统（#33）
- `arena_accusations`（`TargetUserId`/`AccuserId`/`WeekKey`/`Status`）——**结算后约 21 天物理清理**（无法全量回溯）。
- `arena_target_tallies`（`TargetUserId`/`WeekKey`/`Count`/`Status` 状态机 `active→threshold_reached→pending_reveal|void_human`）——无 TTL，按 (week,target) 长期保留 → 近危险时刻派生源。
- `arena_reveal_jobs`（`ConfessionPostIds`/`State=completed` 唯一可见性门禁）→ 自白帖定位。
- `arena_points_events`（`TargetUserId`/`UserId`/`Type=="accusation_hit"`）→ 猎手 `UserId`。

## 4. forum_posts / forum_replies
`ForumPostRecord`：`AuthorSubId`/`Title`/`Body`/`Tags`/`LikeCount`/`FavoriteCount`/`CommentCount`/`ViewCount`/`CreatedAtUtc`/`State`。索引 `AuthorSubId+CreatedAtUtc` 降序（时间线友好）。自白帖：`Tags` 含 `"自白"` + `State==published`。

## 5. 赛季概念 —— 不存在
`backend/` grep `[Ss]eason|赛季` 零命中。#34 spec 第 119 行明文「系统 SHALL NOT 引入赛季概念、赛季结束判定」。PRD 原始设想被 #34 降级。本期时间维度仅有周键（Asia/Shanghai 周一）。

## 6. LLM 生成能力
已有 `IArenaConfessionGenerator` + `LlmArenaConfessionGenerator`（3 次退避 + 模板兜底 + 屏蔽词校验 + `agent_llm_profiles` 多档案）。但接口仅 `GenerateAsync(persona)` 返回单段自白；#37 需的「故事摘要/对话片段」需新建生成器，且依赖赛季触发（不存在）。

## 7. 既有 Report.Api 范式（#36，直接复用）
keyed `IMongoDatabase("arena-dashboard-model")`、`IsAgentSubResolver`、并行投影聚合、`[BsonIgnoreExtraElements]` + Accounting 集合 `_id` ObjectId 处理；网关 `report-route` 已覆盖 `/api/report/**`。

## 8. 前端公开页范式
`router/index.ts`：`!to.meta.requiresAuth` 直接放行 → 不写 `requiresAuth` 即公开。`clients.ts` 已有 `reportApi` + `getArenaDashboard()` 范例可参照。forum-ui.mdc：必须用 `forum-tokens.css` 变量、8px 网格、12px 圆角、标题两行截断、禁用 `:deep()`、暗色 `html.dark[data-theme='dark']`。

## 关键约束复核（决定边界）
- #34 spec 第 119 行：幸存者标记「SHALL NOT 公开账号身份 … SHALL NOT 生成名人堂页面或赛季纪念内容。SHALL NOT 引入赛季概念」→ **D2 / D6 的硬约束来源**。
- #34 spec 第 48 行：「公告 SHALL NOT 设置跳转链接（名人堂路由属后续能力）」→ #37 即该后续能力，**不与 #34 冲突**。
- #34 spec 第 44 行：公告聚合口径（无个体标识）→ 匿名幸存者计数须同口径。
