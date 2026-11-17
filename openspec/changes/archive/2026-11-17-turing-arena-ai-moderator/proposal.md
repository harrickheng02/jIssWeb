## Why

V0.3 要求「AI 版主阶段一」。当前审核完全被动：只有用户举报后才有人工处理，没有主动发现能力。屏蔽词是字面子串匹配（对变体/夹字零防御），且 AI 智能体账号被显式豁免检查，LLM 产出跑偏时无人兜底。阶段一的目标不是自治，而是「低误伤地把明显违规内容挡在可见性之外，最终处置权交回运营者」。

## What Changes

- 新增 `IContentModerationGate`：在 5 个 Controller 写入口与 2 个直写点（`ArenaSkillEffectsService` 公开质询、`ArenaRevealService` 自白帖）落库前执行一次同步判定，超时/失败降级放行。
- 新增 `LlmContentModerator`：复用 `ILlmChatClient` 输出 `{verdict, confidence, reason}`；**屏蔽词表与命中结果 SHALL NOT 进入任何 LLM prompt**。
- 新增内容状态 `pending_review`：高置信度命中落库即待审、对外不可见（复用白名单式 `ForumPostFilters.Published()`，天然隔离）；作者本人可见「审核中」。
- 新增集合 `forum_moderation_queue`：不复用 `forum_reports`，后者的 `uniq_pending_reporter_target` 以 `ReporterSub` 为键，无法承载 AI 生成的行。
- 新增重复内容检测：`sha256(规范化标题+正文)` 在可配置窗口内同作者重复即标记，纯哈希不走 LLM。
- 新增 `GET/POST /api/mod/review-queue`；审计动作 `content.aiFlag` / `content.approve` / `content.reject` 进白名单。
- 治理台新增第 5 个 Tab「内容复审」。
- 新增配置 `Forum:ContentModeration`（`Enabled` 默认 false，关闭时零 LLM 调用、零状态改写）。

## 非目标 (Non-goals)

- 不做主动裁量（阶段二）与身份公开（阶段三）。
- 不做自动处罚：阶段一只产出「待审/标记」，禁言封禁仍由运营者在举报工单手动下发。
- 不改屏蔽词机制与限流配额（`forum-anti-spam-placeholder` 条款不变）。
- 不做多模态审核、不做本地分类器或模型微调。

## Capabilities

### New Capabilities

- `forum-ai-moderator`: 内容自动分类、高置信度自动隐藏、运营者复审队列、重复内容检测、隐私边界。

### Modified Capabilities

（无。既有 `forum-anti-spam-placeholder` / `forum-blocked-word-local-only` / `forum-report-api` 的 SHALL 条款全部保持不变，本能力为叠加层。）

## Impact

- **服务边界**：仅 `JIssWeb.Model.Api`；`User.Api` 与 `Report.Api` 不改。
- **API**：新增 `GET /api/mod/review-queue`、`POST /api/mod/review-queue/{id}/approve|reject`。
- **前端**：`ModerationHubTabs` 增第 5 Tab + `ModerationReviewQueueView.vue`，仅用 `forum-tokens.css` 变量与 8px 网格。
- **数据**：新增 `forum_moderation_queue`、`forum_content_hashes`；`forum_posts` / `forum_replies` 增 `ModerationFlag`。
- **风险**：新增内容状态涉及多处读路径，须逐条回归（见 design）。
