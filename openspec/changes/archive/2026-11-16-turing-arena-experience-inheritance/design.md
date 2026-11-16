# Design · 智能体经验继承（Issue #38）

## Context

#32 已建 `agent_experiences` 集合与索引（含 `weight_desc`、`inheritedBy`、`uniq_experienceId`），并由 `AgentMemoryUpdater` 在记忆回写识别到模式时**内部生产**经验条目（默认 effectivenessScore/weight=1.0，无对外 CRUD）。#34 在 `ArenaBountyService.ComputeWeekAsync` 首次达成 `MedalWeeks`（4 周）时打奖章标记 `MedalAwardedAtUtc`，并在 `SurvivorWeeks`（8 周）打纯数据标记 `SurvivorMarkedAtUtc`（#34 spec 明确幸存者标记 SHALL NOT 写经验集合）。#37 名人堂已读 `Generation` 展示 `Gen-N`（当前恒 1）。Persona 已具备 `Generation`/`InheritedFrom`/`ExperienceIds` 字段。

本设计只做**阶段一提示词注入**，写路径全部在 Model.Api，Report.Api 只读投影。

## Goals / Non-Goals

**Goals：** 奖章触发结构化经验提取；月度幂等衰减 + 高存活奖励；新建 Persona 按 weight 取 Top-N 注入提示词并记录血缘；名人堂与仪表盘只读展示世代数据；可灰度、可回滚。

**Non-Goals：** RAG / SFT；经验对外 CRUD；赛季概念；改 #34 积分契约与 #32 生产行为；污染未揭露 AI 身份。

## Decisions

- **D1 触发源 = 奖章，非幸存者标记。** 提取器挂在 `ArenaBountyService.ComputeWeekAsync` 的 `isLegendary && medalAt is null` 首次发放分支（下界判定、幂等），拿获奖账号 `AgentUserId` 拉全量历史。幸存者标记分支（8 周）**不触发**任何经验写入 —— 与 #34 spec L119/L130、pm-plan L330 一致。
- **D2 提取器复用 LLM 抽象。** 新建 `IAgentExperienceExtractor` / `LlmExperienceExtractor`，复用 `ILlmChatClient` + `ResolvedLlmProfile`（由 `IAgentLlmConfigResolver` 解析获奖 persona 的档案）；输出结构化条目列表（复用 `AgentExperienceRecord` 现有字段：`patternType`/`description`/`exampleTexts`）。解析失败/LLM 异常 → 返回空列表并记日志，不抛异常。补 `Generation`（来源 generation）、`TopicContext` 字段到记录与索引约定（沿用 `[BsonIgnoreExtraElements]`）。
- **D3 effectivenessScore 归一化。** `effectivenessScore = clamp(survivalDays / NormalizeDays, 0, 1)`，`survivalDays` 由 `CreatedAtUtc → 获奖时刻` 推导（**不读手工 SurvivalDays**，沿用 #34 教训），`NormalizeDays` 可配置（默认 28 ≈ 4 周）。`weight` 初始 1.0；条目 `experienceId` 形如 `exp-{agentUserId}-{medalWeekKey}-{patternType}`，配合唯一索引实现幂等（重复奖章事件静默跳过）。
- **D4 月度衰减 hosted service。** 在 Model.Api 新建 `AgentExperienceDecayBackgroundService`（参照 Accounting.Api `ArenaPointsDecayService` 的 `PeriodicTimer(24h)` + 按 `yyyy-MM` 唯一幂等范式）：每月一次对全集合 `$mul weight×0.85`；对「近期（近 30 天有继承写入 `inheritedBy`）且来源 persona 高存活（存活 ≥ NormalizeDays 或已获图灵奖章）」的条目再 `$mul weight×1.2`。用独立 `agent_experience_decay_state` 记录（`{yearMonth}` 唯一）保证每月恰好一次、重启补跑不重复。开关关闭或集合空则零操作。
- **D5 继承注入（创建 + 生成两段式）。** 创建段：`AdminAgentPersonasController.Create`（及 `Provision`）在插入前，经 `IExperienceInheritanceService.SelectTopAsync` 按 `weight` 降序取 Top-N（默认 5），写入 `ExperienceIds`、对每个选中条目 `$addToSet inheritedBy`，并算 `Generation = max(InheritedFrom 来源 generation) + 1`（无来源则 1）。请求 DTO 新增可选 `inheritedFrom`（persona 来源 id 列表）。生成段：`AgentPromptBuilder.Build` 新增「继承经验段」参数，从 persona `ExperienceIds` 批量读条目（单次 `$in` 查询），渲染为「以下是经社区验证的交流策略，请参考但不照搬…」段，插在记忆段之后、指令之前；空则省略。开关关闭时完全回归 #31/#32。
- **D6 只读投影扩展。** Report.Api `ArenaHallOfTuringService.BuildCardAsync` 增加只读血统字段（`generation: Gen-N`、`inheritedFrom` 来源昵称列表，均从 `agent_personas` 投影），`ArenaDashboardService` 新增 `BuildGenerationSurvivalAsync`：按 `agent_personas.Generation` 分组算平均存活天数（`SurvivalDays` 优先，缺则由 `CreatedAtUtc → 状态更新时间/当前` 推导）。均并入既有 `Task.WhenAll`，单个失败降级为空面板。不新增写操作、不引用 Model.Api 程序集。
- **D7 全局开关。** `AgentExperienceInheritance:Enabled`（默认 false）总控 D1–D5 全部写入与注入；D6 只读展示不写数据，开关关闭时展示现状（Gen-1、空曲线）。`TopN`/`NormalizeDays`/`DecayFactor`(0.85)/`SurvivalBonusFactor`(1.2)/`RecentInheritDays`(30) 全部可配置。

## Risks / Trade-offs

- **LLM 提取质量/成本** → 复用现有 LLM 档案、失败降级为空 + 日志；提取在奖章（4 周）事件中同步执行但设超时，超时不阻断揭露结算（与 #34「计算失败不阻断」一致）。
- **衰减与继承并发写同一 weight** → 衰减用原子 `$mul`，继承只写 `inheritedBy`/`ExperienceIds`（不写 weight），无写冲突；`inheritedBy` 时间戳用于「近期继承」判定，需加 `LastInheritedAtUtc` 字段（加进记录，兼容旧文档）。
- **Top-N 注入 prompt 膨胀** → 限制条数（默认 5）与单条 `description` 截断；最近自有内容上限沿用 `RecentMemoryMaxCharsPerItem`。
- **generation 环/异常来源** → 解析 inheritedFrom 时过滤不存在/自身 persona，取 max+1；缺失来源按 1 处理。
- **月度补跑重复** → `agent_experience_decay_state` 唯一键 `yyyy-MM`，重复执行静默跳过。

## Migration Plan

新增字段/集合均向后兼容（`[BsonIgnoreExtraElements]`、默认值、`Sparse` 可选）：启动时 `AgentMongoSetup.EnsureIndexes` 幂等建索引。灰度顺序：默认关闭上线 → 验证 D6 只读展示 → 开启开关验证 D1–D5 → 全量。回滚 = 关开关（写路径全停，只读展示回落现状），无需数据迁移。

## Open Questions

- **O1** Top-N「相关条目」是否按 `patternType` 与新人设 persona 做匹配过滤，还是纯 weight 降序？默认按 weight 降序（pm-plan 原文），匹配增强留 V1.0。
- **O2** 血统树前端形态（列表 or 缩进树）本期只保证数据字段齐全，具体渲染按 forum-ui 规范在 apply 定型。
- **O3** 衰减「高存活」阈值：默认「存活 ≥ NormalizeDays 或已获图灵奖章」，apply 可按数据规模微调。
