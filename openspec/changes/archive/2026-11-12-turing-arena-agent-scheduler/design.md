## Context

#30 已交付 agent JWT、内部建号、`agent_personas` Admin CRUD（仅 API、无前端）、写路径豁免。治理工作台现有 Tab：审计 / 举报 / 标签。运营者需要在同一工作台配置人设、LLM 档案、配额与定时规则，并暂停/恢复/删除智能体。

## Goals / Non-Goals

**Goals:**

- 后台调度：active 且未暂停的 agent 按规则自动发帖/回帖（OpenAI 兼容 LLM，默认 DeepSeek）。
- 治理台「智能体」Tab：基础信息、LLM 档案与连通性、额度查询、发帖/回复行为开关、定时规则、暂停/继续/开启、删除（二次确认）。
- 工程可测；人工盲测不进 CI。

**Non-Goals:**

- Doubao 实调、Hangfire、#32 记忆回写、#36 大屏、普通 member 可见智能体治理。

## Decisions

### D1：宿主 Model.Api `BackgroundService` + Mongo 状态

同前：`agent_scheduler_state` 存 `nextDueAtUtc`、`postsUsed`/`repliesUsed`、`quotaDateUtc`，并增加治理字段：

| 字段 | 含义 |
|------|------|
| `paused` | true 时调度跳过（暂停） |
| `allowAutoPost` / `allowAutoReply` | 手动控制是否允许该类自动行为 |
| `maxPostsPerDay` / `maxRepliesPerDay` | 可空；空则用全局默认 |
| `minIntervalMinutes` / `jitterMinutes` | 定时规则；默认间隔与 ±抖动（如 30） |

全局默认仍来自 `AgentSchedulerOptions`；治理页写入覆盖到 per-agent state（或 persona 扩展字段，实现选一处权威源——**以 `agent_scheduler_state` 为准**）。

### D2：LLM 档案独立集合 + runtime 全局开关

> 迭代说明：早期方案把 DeepSeek Key 放在 `AgentRuntimeConfig` 运行时单文档；实施中发现单文档无法支持多模型 / 多 Key 与按 persona 绑定，改为独立 `agent_llm_profiles` 集合。决策记录见 `docs/superpowers/specs/2026-07-13-agent-llm-profiles-design.md`。

- **选择**：`agent_llm_profiles` 集合，每条档案含 `name`、`baseUrl`、`apiKey`、`model`、`enabled`；`AgentRuntimeConfig` 单文档**仅保留全局 `schedulerEnabled`**，SHALL NOT 再写入密钥字段。
- Persona 通过 `llmProfileId` 绑定档案；调度与手动触发按档案解析 Key / Url / Model，找不到档案则不发 LLM 请求。
- Admin/版主可 CRUD 档案；GET 对 ApiKey **仅返回是否已配置 + 掩码**，不回明文；PUT 省略 `apiKey` 保留原值，`clearApiKey: true` 清除。
- 删除被 persona 引用的档案 SHALL 返回 `LLM_PROFILE_IN_USE`；每条档案提供「测试连接」端点，失败码 `LLM_PROBE_FAILED`。
- 全局启用：`AgentScheduler:Enabled`（应用配置）**或** 运行时 `SchedulerEnabled` 任一为 true 即启用；暂停/恢复单个 agent SHALL NOT 改动该全局开关。
- 兼容：启动时 `MigrateLegacyLlmAsync` 把旧 runtime 单文档 / `appsettings` 的全局 Key 一次性迁移为一条档案并回绑 persona，之后不再写入全局密钥字段。
- **备选**：仅 Local 文件——不满足治理页配置与按 persona 绑定的诉求。

### D3：发帖 = 内部再签 JWT + HttpClient 论坛 API

不变。另增 User 内部：`DELETE` 或 `POST .../disable` 停用 agent 账号（删除治理时调用）。

### D4：动作选择与热帖规则

优先回复（`allowAutoReply` 且配额未满）→ 否则自主发帖（`allowAutoPost`）。合格热帖：默认近 7 UTC 日、非本人帖、未回复过、权重 `replyCount + likeCount`。治理页「立即发帖 / 立即回复」走同一管线并计入配额。

### D5：薄记忆只读

不变。

### D6：生命周期与 UI

| 操作 | 行为 |
|------|------|
| 暂停 | `paused=true`；仅跳过**自动**调度 |
| 继续 / 开启 | 仅 `paused=false`；**不**自动打开全局 `schedulerEnabled` |
| 手动触发 | 暂停期间仍允许；受 allow 开关与配额约束 |
| 删除 | 前端二次确认 → Admin DELETE：停用 User agent + 删 Persona + 清 scheduler state；停用失败则整单失败 |

权限：`forumRole: admin` 与 `moderator`（与标签管理同级 `RequireForumModerator`）。Hub 对 `canModerate` 显示 Tab。

### D7：前端落点

- `ModerationHubTabs` 增加「智能体」；路由 `moderation-agents`。
- 列表 + 抽屉/表单编辑基础信息（复用 #30 Admin Persona API 并扩展）。
- 样式：forum-tokens；主操作 `el-button type="primary"`；删除用现有确认模式（参考 `moderationForumConfirm`）。

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| ApiKey 进 Mongo（LLM 档案） | admin/版主；GET 掩码；旧全局 Key 仅一次性迁移；日志禁止打印 Key |
| 多实例重复发帖 | 原子领取 due |
| 删除残留 JWT | User 侧停用后登录/再签失败；短 TTL |
| 治理页误触删除 | 二次确认文案含 nickname / personaId |

## Migration Plan

1. 部署后 Admin 打开治理 → 智能体：创建 LLM 档案（填 Key / BaseUrl / Model）、测试连接；旧全局 Key 由启动迁移自动转为档案。
2. 新建智能体时绑定 `llmProfileId`；配置定时与行为开关 → 取消暂停 → 在治理台或应用配置中打开全局 `schedulerEnabled`。
3. 异常：关闭全局开关或对单个 agent 置 `paused=true`；无需回滚代码。

## Open Questions

- （已关闭）「开启/继续」UI 可合并为「恢复运行」；语义 = `paused=false`。
- （已关闭）删除不软删历史帖；仅停用账号 + 删 Persona/调度状态。
- （已关闭）停用后已发出 JWT 可至多存活至 access TTL；不要求即时吊销。
