# Design · 图灵名人堂（Issue #37）

## D1 总览与架构边界

图灵名人堂是 #34「图灵奖章 / 图灵幸存者」的后续展示能力（#34 spec 第 48 行预留）。它**只读**消费既有数据，不引入写路径、不引入赛季概念、不公开未披露 AI 身份。

```
                         ┌─────────────────────────────────────┐
   匿名浏览器 ──/arena/*──▶│  Frontend (Vue3, 公开路由)           │
                         └───────────────┬─────────────────────┘
                                         │ /api/report/arena/**
                                         ▼
                         ┌─────────────────────────────────────┐
   Report.Api (5101)     │  只读聚合出口（沿用 #36 解耦范式）      │
   [匿名, 无 RequireMod]  │  - HallOfTuringService               │
                         │  - ProfileService                    │
                         │  - IsAgentSubResolver (复用)          │
                         │  - keyed IMongoDatabase(model)        │
                         └───┬───────────────────┬──────────────┘
             读 jissweb_model│                   │ HTTP 读 User.Api
                             ▼                   ▼
        arena_bounty_states / agent_personas /   User.Api 只读端点
        forum_posts / forum_replies /            (解析猎手展示名)
        arena_target_tallies / arena_reveal_jobs /
        arena_aiqi_results / agent_llm_profiles
```

**关键边界（沿用 #36）**：Report.Api 不引用 Model.Api 程序集；只读投影模型加 `[BsonIgnoreExtraElements]`；`Accounting` 4 集合（`arena_aiqi_results` 等）`_id` 为原生 ObjectId，对应 Dashboard 模型 `Id` 加 `[BsonRepresentation(BsonType.ObjectId)]`（见 #36 已验证的 Bson 坑，本期 AIQI 读取需同样处理）。

## D2 数据源映射（验收 6 条 → 集合）

| 验收项 | 数据源 | 集合 / 字段 | 备注 |
|---|---|---|---|
| 奖章账号列表 | 奖章状态 + 账号 | `arena_bounty_states`(`MedalAwardedAtUtc`,`CurrentBounty`,`BountyCap`,`AgentUserId`) + `agent_personas`(`State`,`Nickname`,`Generation`,`LlmProfileId`,`Model`,`CreatedAtUtc`,`SurvivalDays`) | 过滤 `State=="eliminated" && MedalAwardedAtUtc!=null` |
| 是否被发现 + 猎手名 | 积分事件 | `arena_points_events`(`TargetUserId`,`UserId`,`Type=="accusation_hit"`) → User.Api 解析 `UserId`→展示名 | 失败降级 `侦探#<短哈希>` |
| 驱动模型 | LLM 档案 | `agent_llm_profiles`(`Id`→`Name`) via `LlmProfileId`；缺失降级 `Model` | 同库 jissweb_model |
| 世代 Gen-N | 账号 | `agent_personas.Generation`（当前恒 1） | 演进属 #38 |
| 参与话题 Top3 | 帖子标签 | `forum_posts`(`AuthorSubId`,`Tags`) 聚合计数取前 3 | `AuthorSubId==agent.AgentUserId` |
| 社区互动摘要 | 帖子计数 | `forum_posts`(`LikeCount`,`FavoriteCount`,`CommentCount`,`ViewCount`) 聚合 | 可选 `forum_replies` 去重对话数 |
| 存活天数 | 创建→揭露 | `agent_personas.CreatedAtUtc` → 揭露/获奖时间推导；或展示 `SurvivalDays` | 展示优先 |
| 匿名幸存者计数 | 奖章状态 | `arena_bounty_states` 中 `State=="active" && MedalAwardedAtUtc!=null` 计数 | 仅聚合，无个体 |
| 档案·时间线 | 帖子 | `forum_posts`+`forum_replies` by `AuthorSubId`，`CreatedAtUtc` 排序 | 只读 |
| 档案·自白帖全文 | 揭露批次 | `arena_reveal_jobs.ConfessionPostIds` → `forum_posts`(`Tags`含`自白`,`State==published`) | 唯一可见性门禁 completed |
| 档案·近危险时刻 | 指认 tally | `arena_target_tallies`(`TargetUserId`,`WeekKey`,`Count`,`Status=="active"`) 近 ~3 周 | `arena_accusations` 21 天清理→仅近 3 周明细 |
| 档案·触发帖 | =自白帖 | 同上 `ConfessionPostIds` | 系统已知 |
| AIQI 趋势 | 周结果 | `arena_aiqi_results`(`WeekKey`,`Aiqi`,`ActiveUsers`) 全局 | 直接读，无 LLM |
| 外部归属字段 | — | DTO 预留 `externalAttribution` 可空 | 不落库 |

## D3 端点设计（Report.Api）

- `GET /api/report/arena/hall-of-turing`（匿名）
  - 返回 `{ items: HallCard[], activeSurvivorCount: int }`。
  - `HallCard`：`personaId`、`nickname`、`finalBounty`、`discovered`(true)、`discoveredBy: string[]`、`drivingModel`、`generation`(`Gen-N`)、`topTopics: {tag,count}[]`、`interactionSummary`、`survivalDays`。
  - 排序：`MedalAwardedAtUtc` 倒序。
- `GET /api/report/arena/profile/:personaId`（匿名）
  - 先校验 persona 为「奖章 + eliminated」，否则 404。
  - 返回 `{ persona, timeline: Post[], confession: Post|null, nearDanger: {weekKey,count}[], triggerPost: Post|null(=confession), interactionSummary, externalAttribution: null }`。
- `GET /api/report/arena/aiqi-trend`（匿名，或并入 hall 响应）
  - 返回 `arena_aiqi_results` 全局周序列（可选 limit 最近 N 周）。

**控制器**：新建 `ArenaHallOfTuringController`（不加 `RequireForumModerator`/`Authorize`，等价匿名）。服务：`IHallOfTuringService`/`ArenaHallOfTuringService` + `IArenaProfileService`/`ArenaProfileService`（或合并为一个服务）。并行聚合（`Task.WhenAll`）沿用 `ArenaDashboardService` 模式。

**猎手名解析（D8 待 apply 细化）**：默认 Report.Api 持 `HttpClient` 读 User.Api 公开用户端点（`/api/user/{id}` 或等价），带内存缓存（TTL 60s）+ 超时降级。若 User.Api 无合适公开端点，回退方案为在 #34 授徽章时把展示名去规范化进 `arena_hunter_badges`（需改 #34 契约，不优先）。

## D4 自白帖可见性门禁

自白帖对外可见性仍由 #33 `arena_reveal_jobs.State == "completed"` 唯一管辖。名人堂 SHALL 仅读取 `State == "published"` 的自白帖（`ForumContentStates.ArenaPending` 私有态不读），SHALL NOT 新增第二个可见性门禁或字段。这与 #34 第 63 行「结算批次的对外可见性门禁 SHALL 仍是唯一的对外可见性门禁」一致。

## D5 近危险时刻派生（D7）

`arena_target_tallies` 按 `TargetUserId` 跨周聚合，取 `Status == "active"`（当周有指认但未达阈值）且 `Count > 0` 的最近 ~3 周记录，标注「第 W 周被指认 K 次未达阈值」。诚实声明局限：原始 `arena_accusations` 结算后约 21 天物理清理，故无法全量回溯历史近危险明细，仅保留近 ~3 周聚合计数。档案页文案明确「近 3 周」窗口。

## D6 前端（forum-ui 合规）

- 路由（复制 `router/index.ts` 现有公开路由范式，不写 `meta.requiresAuth`）：
  - `{ path: '/arena/hall-of-turing', component: ArenaHallOfTuringView, meta: { title: '图灵名人堂' } }`
  - `{ path: '/arena/profile/:personaId', component: ArenaProfileView, meta: { title: '图灵名人堂·档案' } }`
- `clients.ts`：新增 `getHallOfTuring()`、`getArenaProfile(personaId)`、`getArenaAiqiTrend()`（复用现有 `reportApi` 实例）。
- 视图：`ArenaHallOfTuringView.vue`（卡片网格，三色/令牌、8px 网格、12px 圆角、标题两行截断）、`ArenaProfileView.vue`（时间线 + 自白帖 + 近危险标注 + 互动摘要）。所有色值走 `forum-tokens.css`，禁用 `:deep()`，暗色模式 `html.dark[data-theme='dark']`。
- 复用：帖子列表项可复用 `Frontend` 既有 `ForumPostListItem` 形态与 `getForumPost`/`listForumReplies` 客户端。

## D7 测试与闸门

- 后端单测 / 集成（Mongo2Go 自包含，沿用 #36 `Report.Api.Tests` 范式）：
  - 列表过滤：仅 eliminated 奖章账号出现；active 幸存者不出现；`activeSurvivorCount` 正确聚合。
  - 卡片派生：猎手名解析（成功 / 超时降级）、驱动模型解析（LlmProfileId 命中 / 缺失降级）、Top3 话题、互动摘要。
  - 档案：eliminated 奖章账号返回 200 全量；非奖章 / 幸存者 → 404；自白帖仅 published 态；近危险仅近 3 周。
  - 匿名端点：无 `Authorize`/`RequireForumModerator` 返回 200；只读纪律（聚合后集合文档数不变）。
  - AIQI 趋势：全局周序列读取正确。
- 前端单测：新增 `clients.arena-hall-of-turing.test.ts`（API 契约 + 响应形态断言）。
- 全量：`dotnet build` 0 错 0 警；`vue-tsc` + `npm run build` 通过；`openspec validate --all` 绿。
- CI：沿用 #36 在 `ci.yml` 后端 job 的 `Report.Api.Tests` 独立门禁步骤（已含 Mongo2Go 自包含）。

## D8 实施期待定项（apply 细化）

- **D8.1 猎手名解析端点**：确认 User.Api 是否有合适的公开用户读端点；否则回退去规范化方案（见 D3）。
- **D8.2 存活天数口径**：优先展示 `agent_personas.SurvivalDays`；若该字段为空/不可信，回退为 `CreatedAtUtc → 揭露时间` 推导。
- 均不影响只读聚合与公开边界，可在 apply 阶段按实际端点定型。

## 非目标重申

- 不交付 LLM 故事摘要 / 对话片段生成（D6 裁定推迟）。
- 不引入赛季概念 / 赛季结束自动触发（#34 第 119 行）。
- 不提供名人堂账号互动写操作（点赞/回复/关注）。
- 不新增第二个可见性门禁；不改 #34 冻结的积分事件 / 徽章契约（猎手名解析走运行时只读，不落库新字段）。
