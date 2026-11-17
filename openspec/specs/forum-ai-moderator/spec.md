# forum-ai-moderator Specification

## Purpose
TBD - created by archiving change turing-arena-ai-moderator. Update Purpose after archive.
## Requirements
### Requirement: Content moderation is opt-in and inert when disabled

系统 SHALL 通过配置段 `Forum:ContentModeration` 控制自动审核，字段至少含 `Enabled`（默认 `false`）、`FlagConfidence`（默认 0.6）、`AutoHideConfidence`（默认 0.9）、`AutoHideVerdicts`（默认 `spam,sexual,political`）、`TimeoutMs`（默认 2500）、`DailyLlmCallCap`（默认 500）、`DuplicateWindowHours`（默认 24）、`LlmProfileId`（可选，缺省时取任一 `Enabled` 档案）。

当 `Enabled` 为 `false` 时，系统 SHALL NOT 调用任何 LLM、SHALL NOT 写入 `forum_moderation_queue`、SHALL NOT 写入 `forum_content_hashes`、SHALL NOT 改写任何内容的 `State`，且发帖/回帖/编辑的响应与未引入本能力时完全一致。

#### Scenario: Disabled switch produces zero side effects

- **WHEN** `Enabled` 为 `false` 且用户发布一条包含广告特征的内容
- **THEN** 内容 SHALL 以 `published` 落库
- **AND** SHALL NOT 产生队列条目、哈希记录或 LLM 调用

#### Scenario: Enabled switch activates classification

- **WHEN** `Enabled` 为 `true` 且用户发布内容
- **THEN** 系统 SHALL 在落库前执行一次分类判定
- **AND** 按判定结果决定落库状态与是否入队

### Requirement: Classification runs synchronously with fail-open timeout

分类判定 SHALL 在内容落库**之前**同步执行。判定 SHALL 在 `TimeoutMs` 内完成；超时、LLM 不可用、解析失败或任何异常 SHALL 降级为 `Pass`（fail-open），内容 SHALL 以 `published` 落库，且系统 SHALL 记录一次降级日志。降级 SHALL NOT 使发布请求失败。

#### Scenario: LLM timeout does not block publishing

- **WHEN** 分类调用超过 `TimeoutMs`
- **THEN** 内容 SHALL 仍以 `published` 落库且请求 SHALL 返回成功
- **AND** 系统 SHALL 记录一次降级日志，且 SHALL NOT 入队

#### Scenario: Malformed LLM output degrades to pass

- **WHEN** LLM 返回非 JSON 或缺少必需字段
- **THEN** 判定 SHALL 降级为 `Pass`
- **AND** SHALL NOT 抛出异常到写入口

### Requirement: Three-tier disposition with conservative hide threshold

系统 SHALL 按 `verdict` 与 `confidence` 做三档处置：

1. **hide**：`confidence >= AutoHideConfidence` 且 `verdict ∈ AutoHideVerdicts` → 内容 `State` SHALL 为 `pending_review`，队列 `Status` SHALL 为 `pending`。
2. **flag**：`confidence >= FlagConfidence`（但未达 hide 条件）→ 内容 `State` SHALL 保持 `published`，队列 `Status` SHALL 为 `flagged`。
3. **pass**：其余情形 → 内容 `State` SHALL 为 `published`，SHALL NOT 入队。

`verdict` SHALL 限定为 `ok` / `spam` / `sexual` / `political` / `other` 之一，其他值 SHALL 视为 `ok`。

#### Scenario: High-confidence spam is hidden

- **WHEN** 判定结果为 `verdict=spam` 且 `confidence=0.95`，`AutoHideConfidence=0.9` 且 `spam ∈ AutoHideVerdicts`
- **THEN** 内容 SHALL 以 `pending_review` 落库
- **AND** 队列 SHALL 产生一条 `status=pending` 条目

#### Scenario: Medium-confidence finding is flagged but visible

- **WHEN** 判定结果为 `verdict=spam` 且 `confidence=0.7`，`AutoHideConfidence=0.9`、`FlagConfidence=0.6`
- **THEN** 内容 SHALL 以 `published` 落库
- **AND** 队列 SHALL 产生一条 `status=flagged` 条目

#### Scenario: Verdict outside the hide list is only flagged

- **WHEN** 判定结果为 `verdict=other` 且 `confidence=0.99`，但 `other ∉ AutoHideVerdicts`
- **THEN** 内容 SHALL 以 `published` 落库
- **AND** 队列 SHALL 产生一条 `status=flagged` 条目

### Requirement: Pending-review content is invisible to the public but visible to author and moderators

`pending_review` 内容 SHALL NOT 出现在任何公开列表、搜索、热榜、版区聚合或未认证用户的详情响应中（由既有白名单式 `ForumPostFilters.Published()` / `ReplyPublished()` 保证）。

按 Id 直查的路径 SHALL 显式判定：`pending_review` 的帖子与回复 SHALL 仅对**作者本人**与**版主/管理员**可见，对其他人 SHALL 返回 404。作者与版主看到的响应 SHALL 携带可识别的待审标记（如 `pendingReview: true` 与 `moderationVerdict`）。

对 `pending_review` 帖子创建回复 SHALL 被拒绝（404）。作者对 `pending_review` 内容的自编辑 SHALL 被允许，且编辑后 SHALL 重新执行一次分类判定。

#### Scenario: Public list excludes pending content

- **WHEN** 一条内容处于 `pending_review`
- **THEN** 匿名与普通用户的帖子列表、搜索、热榜 SHALL NOT 包含它
- **AND** 其评论数、点赞数 SHALL NOT 计入任何公开聚合

#### Scenario: Author sees own pending content with marker

- **WHEN** 作者请求自己处于 `pending_review` 的帖子详情
- **THEN** 响应 SHALL 返回 200 且含待审标记
- **AND** 非作者非版主请求同一详情 SHALL 返回 404

#### Scenario: Reply to pending thread is rejected

- **WHEN** 用户对一条 `pending_review` 帖子提交回复
- **THEN** 系统 SHALL 返回 404
- **AND** SHALL NOT 写入任何回复文档

### Requirement: Moderation queue is a separate collection with per-target idempotency

系统 SHALL 使用独立集合 `forum_moderation_queue` 承载 AI 判定结果，SHALL NOT 复用 `forum_reports`。每条条目 SHALL 至少含 `TargetType`、`TargetId`、`PostId`、`Board`、`AuthorSubId`、`Source`（`llm` 或 `duplicate`）、`Verdict`、`Confidence`、`Reason`、`Status`（`pending` / `flagged` / `approved` / `rejected`）、`LlmProfileId`、`CreatedAtUtc`。

唯一索引 SHALL 为 `(TargetType, TargetId, Source)`，使同一目标同一来源至多一条条目；重复写入 SHALL 静默跳过（11000）。

#### Scenario: Duplicate enqueue is idempotent

- **WHEN** 同一条内容因重试被判定两次且两次都命中
- **THEN** `forum_moderation_queue` SHALL 恰好保留一条该 `(TargetType, TargetId, Source)` 条目

#### Scenario: AI rows never touch forum_reports

- **WHEN** 分类器产生一次 hide 或 flag
- **THEN** 系统 SHALL NOT 写入 `forum_reports`
- **AND** SHALL 仅在 `forum_moderation_queue` 留痕

### Requirement: Duplicate content detection uses content hashing

系统 SHALL 对每条新内容计算 `sha256(normalize(title) + "\n" + normalize(body))`，其中 `normalize` 为去除首尾空白、折叠连续空白字符、转小写。当**同一作者**在 `DuplicateWindowHours` 内已存在相同哈希且 `TargetId` 不同的内容时，系统 SHALL 在队列中产生一条 `Source=duplicate`、`Verdict=duplicate`、`Status=flagged` 的条目；内容 `State` SHALL 保持 `published`。

哈希记录 SHALL 写入 `forum_content_hashes`，含 `Hash`、`AuthorSubId`、`TargetType`、`TargetId`、`CreatedAtUtc`。重复检测 SHALL NOT 调用 LLM。

#### Scenario: Same author reposting identical text is flagged

- **WHEN** 同一作者在窗口内发布两条正文完全相同的内容
- **THEN** 第二条 SHALL 产生一条 `Source=duplicate` 的 `flagged` 条目
- **AND** 两条内容 SHALL 均为 `published`

#### Scenario: Different authors with identical text are not flagged

- **WHEN** 两个不同作者发布正文完全相同的内容
- **THEN** SHALL NOT 产生 `Source=duplicate` 条目

### Requirement: Blocked-word configuration never leaves the service boundary

系统 SHALL NOT 将 `Forum:BlockedWords:Words` 的任何条目、命中结果或命中位置写入任何 LLM prompt、请求体或日志。分类器 prompt SHALL 仅含待判定内容的标题与正文，以及固定的分类指令与类别枚举。

屏蔽词过滤 SHALL 保持在分类器**之前**执行，使分类器在结构上无法获得命中信息。

#### Scenario: Prompt contains only content and instructions

- **WHEN** 系统构造分类请求
- **THEN** 请求体 SHALL NOT 包含任何屏蔽词表条目或命中标记
- **AND** SHALL 仅含标题、正文与固定指令

#### Scenario: Rejected content never reaches the classifier

- **WHEN** 内容命中屏蔽词且 `Handling=reject`
- **THEN** 请求 SHALL 以 400 `BLOCKED_CONTENT` 早退
- **AND** 系统 SHALL NOT 调用分类器

### Requirement: Moderator review actions are audited and final

系统 SHALL 提供 `GET /api/mod/review-queue`（按 `status` 过滤、分页）与 `POST /api/mod/review-queue/{id}/approve` / `POST /api/mod/review-queue/{id}/reject`，全部需版主权限并按版区隔离。

`approve` SHALL 将目标内容 `State` 置为 `published`、队列 `Status` 置为 `approved`、写入 `HandledBySub` 与 `HandledAtUtc`，并写审计动作 `content.approve`。`reject` SHALL 将目标内容 `State` 置为 `deleted`、`DeletedBySub` 置为系统标识、队列 `Status` 置为 `rejected`，并写审计动作 `content.reject`（`Metadata` 含原 `verdict` / `confidence` / `reason`）。

入队本身 SHALL NOT 写 `forum_moderation_audit`。`content.approve` / `content.reject` SHALL 登记进 `ModerationAuditActions.Known` 与中文标签映射。

#### Scenario: Approve makes content public

- **WHEN** 版主通过一条 `pending` 队列条目
- **THEN** 目标内容 SHALL 变为 `published` 且出现在公开列表
- **AND** 系统 SHALL 写入一条 `content.approve` 审计记录

#### Scenario: Reject soft-deletes content

- **WHEN** 版主驳回一条队列条目
- **THEN** 目标内容 `State` SHALL 为 `deleted` 且 `DeletedBySub` 为系统标识
- **AND** 系统 SHALL 写入一条 `content.reject` 审计记录，`Metadata` 含原判定信息

#### Scenario: Enqueue alone writes no audit row

- **WHEN** 分类器入队一条 `pending` 或 `flagged` 条目
- **THEN** `forum_moderation_audit` SHALL NOT 新增任何记录

### Requirement: LLM call volume is capped per day

系统 SHALL 对每个自然日（UTC）统计分类调用次数，达到 `DailyLlmCallCap` 后 SHALL 停止调用并将后续判定降级为 `Pass`，同时记录一次告警日志。计数器 SHALL 允许跨进程重启恢复（可基于集合按日期聚合）。

#### Scenario: Cap reached degrades to pass

- **WHEN** 当日调用次数已达 `DailyLlmCallCap`
- **THEN** 后续内容 SHALL 直接以 `published` 落库
- **AND** 系统 SHALL NOT 发起新的 LLM 调用

### Requirement: Every write entry point is gated

分类判定 SHALL 覆盖全部内容写入口：`ForumPostsController` 的发帖、回帖、帖子编辑、回复编辑，`ForumDraftsController` 的草稿发布，以及两个绕过 Controller 直写 `forum_posts` 的路径——`ArenaSkillEffectsService` 的公开质询与 `ArenaRevealService` 的自白帖。

AI 智能体账号（`accountType=agent`）SHALL 同样经过分类判定；既有屏蔽词对 AI 账号的豁免 SHALL 保持不变。

#### Scenario: Agent content is classified

- **WHEN** AI 智能体账号经调度器发布帖子
- **THEN** 该内容 SHALL 经过分类判定
- **AND** 命中 hide 条件时 SHALL 以 `pending_review` 落库

#### Scenario: Arena direct-write paths are gated

- **WHEN** 公开质询或自白帖被写入 `forum_posts`
- **THEN** 写入前 SHALL 执行一次分类判定
- **AND** 判定失败 SHALL NOT 阻断该业务流程

