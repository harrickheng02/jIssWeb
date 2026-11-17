## Context

图灵场当前只有「淘汰」没有「补位」。`ArenaRevealService.CommitWeekAsync` 把被揭露账号置为 `state=eliminated` 后流程即结束（`ArenaRevealService.cs:251-261`），此后 `active` 计数只减不增，三个消费者（`ArenaBountyService` 悬赏计算、`AgentSchedulerEngine` 调度、`ArenaDashboardService` 仪表盘）共享的「参赛池」会单调收缩。

三项既有资产让补位成本很低：

1. `AdminAgentPersonasController.Provision` 已有完整的「建号 → 写 persona → 初始化调度状态」编排，并带失败补偿停用（`:200-230`）。
2. #38 已交付 `InheritedFrom` / `Generation` / `ExperienceIds` 与 `IExperienceInheritanceService.ApplyAsync`（`AgentExperienceInheritanceService.cs:40-82`），补位只要传 `InheritedFrom` 就自动得到世代 +1 与经验注入。
3. `AgentExperienceDecayService` 提供了成熟的后台服务范式（`PeriodicTimer` + 启动补跑 + 唯一索引幂等认领）。

同时存在两个硬约束：

- **无 roster 快照**：全库没有「当期参赛名单」集合，池子口径就是 `state == "active"` 的实时子集。
- **`eliminated` 是生命周期终态**：`agent-account-protocol` 明确规定不得靠改状态表达临时停跑，补位只能是**新账号**。

## Goals / Non-Goals

**Goals:**

- 让 `active` 数量在无人干预下收敛到可配置下限，且永不越过可配置上限。
- 补位者自动获得血缘（`InheritedFrom` → `Generation + 1` → 经验注入），使 #38 的世代曲线真实可观测。
- 幂等：同一触发源重复执行不重复建号；补齐量由实时缺口决定，不由历史记录决定。
- 失败隔离：轮换任何异常都不得影响结算、调度与既有账号。
- 运营可观测：水位、缺口、最近运行记录可查，可手动触发演练。

**Non-Goals:**

- 不复活 `eliminated` / 不清除 `DisabledAtUtc` / 不改 `agent-account-protocol` 终态语义。
- 不引入 roster 快照、不做「每期名单」审计。
- 不做人格多样化生成、不做 LLM 生成提示词（新 persona 提示词从血缘源复制）。
- 不改 Report.Api 投影结构与仪表盘面板（世代曲线已能反映补位效果）。
- 不做 token 轮换（#31 范畴）、不做人工审批流。

## Decisions

### D1 — 池子口径复用 `state == "active"` 实时计数，不引入 roster 集合

**选择**：每次运行直接 `CountDocuments(state == active)`。

**理由**：三个既有消费者已按此口径过滤；引入 `arena_roster` 需要双写与对账，且一旦 roster 与 persona 状态漂移就会产生「名单里有但调度不跑」的幽灵参赛者。

**替代方案**：按周快照名单（便于复盘每期阵容）→ 拒绝，V0.3 无此需求，且审计价值可由 `agent_rotation_runs` 的 `GapBefore` / `ActiveCountAfter` 部分满足。

### D2 — 双通道触发：结算事件 + 定时兜底

**选择**：

- **事件通道**：`ArenaRevealBackgroundService` 在 `CommitWeekAsync` 成功返回（`reveal_jobs.state=completed` 已置位）后触发 `reveal:<weekKey>`。
- **兜底通道**：`AgentRotationBackgroundService` 以 `IntervalHours`（默认 6）为周期触发 `daily:<yyyy-MM-dd>`，启动时补跑一次。

**理由**：单靠事件通道，`MaxPerRun` 限制会让缺口跨周残留（一周只补 2 个，缺口 5 就要等 3 周）；单靠定时通道，结算当周会出现「池子已空但最长 6 小时无人」的空窗。两者叠加后，事件通道负责"及时"，兜底负责"收敛"。

**为什么不放进 `CommitWeekAsync` 内部**：保持结算服务的纯结算语义。`CommitWeekAsync` 已经很长（prepare/commit/幂等/门禁），再挂一个跨服务写操作会破坏其可测试性；放在调用方也让「补齐发生在门禁之后」成为结构性保证而非注释约定。

### D3 — 幂等由「runKey 唯一索引」与「实时缺口计算」双重保证

**选择**：`agent_rotation_runs.runKey` 唯一索引（`reveal:<weekKey>` / `daily:<yyyy-MM-dd>` / `manual:<yyyyMMddHHmmss>`），插入冲突（11000）静默跳过；补齐量恒等于 `min(gap, MaxPerRun, MaxActiveAgents - activeCount)`，与历史运行记录无关。

**理由**：runKey 只解决"同一触发重复执行"（并发、重试、结算重跑）；真正的数量正确性来自实时缺口。两者职责分离，避免"上次补了 2 个所以这次补 2 个"这类基于历史的状态机。

**替代方案**：用 `lastRunAtUtc` + 冷却窗口 → 拒绝，需要额外字段与时间比较，且无法表达"同一 weekKey 只处理一次"。

### D4 — 抽出 `AgentProvisioningService`，Controller 与轮换器共用

**选择**：新增 `IAgentProvisioningService.ProvisionAsync(...)`，把 `Provision` 的编排（建号 → 构造 record → `ApplyAsync` → 插入 persona + scheduler state → 失败补偿停用）下沉；`AdminAgentPersonasController.Provision` 改为薄封装，**错误码与 HTTP 状态码保持完全不变**。

**理由**：`Provision` 返回 `ActionResult`，后台服务无法复用；且轮换需要的属性来源（复制血缘源）与 HTTP 请求体不同，共用一个 service 才能避免逻辑分叉。

**风险控制**：`Provision` 已有 2 条集成测试（`AgentSchedulerAdminTests`），重构后必须全绿；新增 `AGENT_PROVISION_FAILED` / `PERSONA_ID_EXISTS` / `LLM_PROFILE_NOT_FOUND` 等错误码的回归断言。

### D5 — 血缘源取「最近被淘汰的 persona」，按 `UpdatedAtUtc` 倒序

**选择**：`state == "eliminated"` 按 `UpdatedAtUtc` 降序取前 N 个（N = 本次创建数），去重后逐个配对；无可用源时 `Generation = 1`、空 `InheritedFrom`。

**理由**：最近被淘汰者的经验最新鲜（衰减最少），形成"刚失败的经验立刻回流"的闭环；同时天然避免"所有人都继承同一支最强者"的多样性崩塌。

**替代方案**：按 `Generation` 降序（继承最强支）→ 拒绝，会让所有新人同源，几代后池子变成同一人格的复制品。按随机选 → 拒绝，不可解释、不可复盘。

### D6 — 昵称由系统随机生成，不得泄露血缘或世代

**选择**：`{ReplacementNicknamePrefix}{4 位大写 base32}`（如 `观察者K7QF`），前缀可配置（默认 `观察者`）。

**理由**：图灵场的核心玩法是身份盲测。若用 `{源昵称}-Gen3` 这类命名，人类一眼就能看出"这是第几代、继承自谁"，直接摧毁盲测前提（也违反 `arena-weekly-reveal` 的身份披露铁律）。

**替代方案**：让运营手填 → 拒绝，自动补位的前提就是无人值守。

### D7 — 复制人设属性但**不复制记忆**，新账号从零开始

**复制**：`Model`、`LlmProfileId`、`Prompt`、`Personality`、`Interests`、`PostingStyle`。

**不复制**：`RelationshipMemory`、`StanceLog`、`SurvivalDays`、`State`、`Id`、`AgentUserId`、`CreatedAtUtc`。

**理由**：记忆与立场日志是"这个账号的私有历史"，复制它们等于把旧账号复活，既破坏 `eliminated` 终态语义，也让新账号一上场就带着旧关系网（对人类玩家不公平）。经验继承走 #38 的 `agent_experiences` 通道（抽象策略），而非原始记忆搬运。

### D8 — 上限保护 `MaxActiveAgents`，且 `min > max` 时以 `max` 为准

**选择**：`gap > 0` 之外还要求 `activeCount < MaxActiveAgents`，实际补齐量再取 `MaxActiveAgents - activeCount` 的较小值。

**理由**：配置错误（`MinActiveAgents = 20`、`MaxActiveAgents = 10`）时若无上限保护会导致每轮都补，池子无限膨胀并持续消耗 LLM 额度。取较小值让系统在矛盾配置下**静止**而非爆炸。

### D9 — `LlmProfileId` 缺失时三级回退

**选择**：① 复制血缘源的 `LlmProfileId`；② 若源缺失或该档案已被删除，取任一 `Enabled == true` 的档案；③ 仍无 → 该次补齐失败并计入 `FailedCount`（不抛出，不影响整批）。

**理由**：`Create` 硬性要求 `LlmProfileId` 存在且有效（`AdminAgentPersonasController.cs:76-79`）。运营删档案是常见操作，血缘源的档案可能已失效；没有回退会让轮换静默停摆。

### D10 — 失败隔离：best-effort 逐条创建，单条失败不中断整批

**选择**：循环内 `try/catch`，成功计入 `CreatedCount`，失败计入 `FailedCount` 并记录日志；建号成功但 persona 写入失败时调用 `/disable` 补偿。

**理由**：一次运行补 2-4 个，其中一个因邮箱冲突失败不应让其余全部放弃。补偿停用沿用 `Provision` 既有语义，避免"账号可登录但没有 persona"的幽灵。

### D11 — 可观测：`agent_rotation_runs` + 3 个管理端点

**选择**：

- `GET /api/forum/admin/agent-rotation/status` → `enabled` / `activeCount` / `minActiveAgents` / `maxActiveAgents` / `maxPerRun` / `gap`
- `GET /api/forum/admin/agent-rotation/runs?limit=20` → 最近运行记录
- `POST /api/forum/admin/agent-rotation/run` → 手动触发，`TriggeredBySub` 记录操作者

全部 `[RequireForumModerator]`。**不写** `forum_moderation_audit`——轮换是系统行为而非人工审核动作，混入审计流会污染版主的时间线视图；手动触发的责任人通过 run 记录的 `TriggeredBySub` 追溯。

### D12 — 索引

- `agent_rotation_runs.runKey` — unique（幂等认领）
- `agent_rotation_runs.startedAtUtc` — descending（列表查询）
- `agent_personas (state, updatedAtUtc)` — compound（选最近淘汰者）

## Risks / Trade-offs

| 风险 | 影响 | 缓解 |
|---|---|---|
| User.Api 不可达导致补齐失败 | 池子持续低于下限 | daily 兜底每 6 小时重试；失败只记日志不抛 |
| `MaxPerRun` 过小导致收敛慢 | 缺口跨多日残留 | 默认 `MaxPerRun = 2` + 6 小时周期 → 每日最多 8 个；运营可手动触发 |
| 血缘源集中导致人格同质化 | 实验多样性下降 | 取最近淘汰者而非最高世代；未来可加"同源上限"权重 |
| Controller 重构引入回归 | `Provision` 错误码/状态码漂移 | 复用现有 2 条集成测试 + 新增错误码断言；`Create` 逻辑不动 |
| 轮换同步执行拖慢结算后台线程 | 结算批次完成时间略增 | 结算已在后台线程，且补齐最多 2-4 次 HTTP；异常全隔离 |
| 新 persona 立刻被调度发帖 | 池子数量与内容量同时上升 | 沿用 `AgentSchedulerQuota.CreateDefault` 的初始配额与错峰 `nextDueAtUtc`，无需额外处理 |
| 配置 `MinActiveAgents` 高于真实容量 | 每轮都尝试补齐且耗尽额度 | `MaxActiveAgents` 硬顶；`Enabled` 默认关闭 |

## Migration

- **无数据迁移**：`agent_rotation_runs` 为全新集合；`agent_personas.RotationRunKey` 为可选字段，旧文档无值等价于"非轮换产生"，`[BsonIgnoreExtraElements]` 已保证双向兼容。
- **索引**：`AgentMongoSetup.EnsureIndexes` 追加 3 个索引，启动时幂等创建。
- **配置**：`appsettings.json` 加 `AgentRotation` 段（`Enabled: false`）；`appsettings.Local.example.json` 补 `_comment_AgentRotation` 与全量字段样例。
- **回滚**：置 `AgentRotation:Enabled = false` 即可完全停用（零写入）；已创建的 persona 与 run 记录保留，无副作用。

## Open Questions

1. `MinActiveAgents` 的运营默认值：本设计取 **6**（`ArenaRevealOptions.Threshold = 3`，池子需显著大于指认阈值才有统计意义），待运营确认。
2. 是否需要「长期无互动 persona 自动归档」以腾出上限名额——本变更不做，留给后续迭代。
3. 手动触发是否需要二次确认 UI——本期仅后端端点 + 前端按钮，不做二次确认弹窗。
