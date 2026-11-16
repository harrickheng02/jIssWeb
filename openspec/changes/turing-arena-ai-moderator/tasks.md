# 任务清单 · 图灵场 AI 版主阶段一（V0.3）

> 状态：propose 完成，待 apply。
> 遵循 spec-driven Construction SOP：每个后端 task 先写失败测试（`dotnet test` 验证 FAIL）→ 最小实现 → 验证 PASS；前端 task 实现后 `npx vitest run`。
> 总开关 `Forum:ContentModeration:Enabled` 默认关闭；关闭时零 LLM 调用、零哈希写入、零队列写入、零状态改写。
> 三条铁律：① 屏蔽词表与命中结果 SHALL NOT 进任何 LLM prompt；② 只有运营者能最终处置，AI 只产出「待审/标记」；③ 判定失败一律 fail-open（放行），绝不阻断发布。
> 环境说明：Model.Api 全量测试有 15 条**既有** JWT Unauthorized 失败（ForumUserSanctions / ModerationAuditFeed / ModerationDelete），与本 change 无关，以「本 change 新增测试 + Report 侧 + 前端」为验收基线。

## Phase 0 · 配置、数据模型与 LLM 客户端扩展

- [ ] 0.1 新增 `ForumContentModerationOptions`（`SectionName = "Forum:ContentModeration"`；`Enabled=false`、`FlagConfidence=0.6`、`AutoHideConfidence=0.9`、`AutoHideVerdicts=[spam,sexual,political]`、`TimeoutMs=2500`、`DailyLlmCallCap=500`、`DuplicateWindowHours=24`、`LlmProfileId=null`）；`Program.cs` 绑定 + `PostConfigure` 兜底列表；`appsettings.json` 加默认段；`appsettings.Local.example.json` 补 `_comment_ContentModeration`。
- [ ] 0.2 `ForumContentStates` 增 `PendingReview = "pending_review"`；`ForumPostRecord` / `ForumReplyRecord` 增 `ModerationFlag`（`string?`，记录命中 verdict）与 `ModerationConfidence`（`double?`）。
- [ ] 0.3 新增 `ForumModerationQueueRecord`（`[BsonIgnoreExtraElements]`、`[BsonId][BsonRepresentation(ObjectId)]`）+ `ForumModerationQueueStatuses` / `ForumModerationSources` / `ForumModerationVerdicts` 常量；新增 `ForumContentHashRecord`。
- [ ] 0.4 `ForumMongoSetup` 补两个集合常量与索引：`forum_moderation_queue` 的 `(TargetType,TargetId,Source)` unique + `status_created` + `author`；`forum_content_hashes` 的 `(AuthorSubId,Hash,CreatedAtUtc)` + `createdAt`。确认不破坏既有索引。
- [ ] 0.5 【测试先行】新增 `LlmChatRequestOptionsTests`：`Temperature=null` 时请求体温度仍为 0.8（既有行为不变）、非 null 时按传入值写入、`MaxTokens` 同理；断言 FAIL。
- [ ] 0.6 `LlmChatRequest` 增可选 `double? Temperature` / `int? MaxTokens`；`DeepSeekChatClient` 仅在非 null 时写入请求体；运行 0.5 至 PASS。

## Phase 1 · 分类器与内容门禁

- [ ] 1.1 【测试先行】新增 `LlmContentModeratorTests`：合法 JSON 解析出 `verdict/confidence/reason`；`verdict` 越界回落 `ok`；缺字段回落 `ok`；非 JSON 回落 `ok`；空响应回落 `ok`；断言 FAIL。
- [ ] 1.2 新建 `IContentModerator` / `LlmContentModerator`：复用 `ILlmChatClient` + `IAgentLlmConfigResolver`，`Temperature = 0`，prompt 内 JSON 契约 + 首尾大括号截取 + verdict 白名单 + 全 catch（照抄 `LlmExperienceExtractor.Parse` 范式）。**prompt 只含标题/正文/固定指令**。运行 1.1 至 PASS。
- [ ] 1.3 【测试先行】新增 `ContentModerationGateTests`：`Enabled=false` 零调用；hide 条件 → `pending_review` + 队列 `pending`；flag 条件 → `published` + 队列 `flagged`；pass → 不入队；超时 → 放行且不入队；日上限耗尽 → 放行且零调用；断言 FAIL。
- [ ] 1.4 新建 `IContentModerationGate` / `ContentModerationGate`：编排「开关 → 日上限 → 分类 → 三档判定 → 状态改写 + 入队」，返回 `ModerationDecision(State, Enqueued)` 供写入口使用；入队遇 11000 静默跳过。运行 1.3 至 PASS。
- [ ] 1.5 日调用计数：基于 `forum_moderation_queue` 按 `CreatedAtUtc` 当日聚合（含 `Source=llm`），避免额外集合；补一条「跨重启仍生效」的测试。

## Phase 2 · 写入口接入

- [ ] 2.1 【测试先行】扩展 `ForumPostCreateTests` / `ForumReplyCreateTests`：`Enabled=true` 且判定 hide 时，发帖/回帖落库为 `pending_review`；`Enabled=false` 时行为与基线一致；断言 FAIL。
- [ ] 2.2 `ForumPostsController.Create` / `CreateReply` 在屏蔽词与限流之后、`InsertOneAsync` 之前调用 gate；按返回值决定 `State`；运行 2.1 至 PASS。
- [ ] 2.3 【测试先行】扩展编辑与草稿发布测试：`UpdatePost` / `UpdateReply` / `PublishDraft` 重新过 gate，命中 hide 时转 `pending_review`；断言 FAIL。
- [ ] 2.4 三处编辑路径接入 gate；运行 2.3 至 PASS。
- [ ] 2.5 【测试先行】新增 `ArenaContentModerationTests`：`ArenaSkillEffectsService.PublicInquiryAsync` 与 `ArenaRevealService.ResolveSafeConfessionBodyAsync` 均调用 gate，且 gate 抛异常时不阻断业务流程；断言 FAIL。
- [ ] 2.6 两个直写点接入 gate（用 try/catch 包裹，异常只记日志）；运行 2.5 至 PASS。
- [ ] 2.7 【测试先行】新增一条「AI 智能体账号内容也过 gate」的测试（`accountType=agent` 的 JWT）；断言 FAIL 后确认 2.2 的实现已覆盖（AI 走同一 Controller），无需额外代码，保留测试作回归。

## Phase 3 · 待审内容可见性（最高风险，逐条回归）

- [ ] 3.1 新建 `ForumVisibility` helper：`CanViewAsync(post, requesterId, isModerator)` 与回复版本，`pending_review` 仅作者/版主可见；补单元测试覆盖 5 种状态 × 4 种身份。
- [ ] 3.2 【测试先行】扩展 `ForumPostsController` 详情/回复列表/回帖测试：`pending_review` 对匿名与普通用户 404、对作者与版主 200 且带 `pendingReview` 标记、回帖被拒；断言 FAIL。
- [ ] 3.3 `GetById`、`GetReplies`、`CreateReply` 接入 `ForumVisibility`；运行 3.2 至 PASS。
- [ ] 3.4 【测试先行】扩展 `ForumMeController` 测试：作者「我的帖子/我的回复」可见 `pending_review` 且带状态徽标字段；断言 FAIL 后实现至 PASS。
- [ ] 3.5 【测试先行】扩展公开列表/搜索/热榜测试：`pending_review` 不出现在任何公开列表，评论数与点赞数不计入公开聚合；断言 FAIL 后确认白名单过滤器已天然满足，保留测试作回归。
- [ ] 3.6 【测试先行】扩展举报目标解析测试：`pending_review` 内容仍可被举报且入队去重不冲突；断言 FAIL 后实现至 PASS。

## Phase 4 · 重复内容检测

- [ ] 4.1 【测试先行】新增 `DuplicateContentDetectorTests`：同作者同哈希窗口内 → 入队 `Source=duplicate`、`Status=flagged`、内容仍 `published`；不同作者同哈希 → 不标记；窗口外 → 不标记；空白折叠与大小写归一化生效；断言 FAIL。
- [ ] 4.2 新建 `IDuplicateContentDetector` / `DuplicateContentDetector`（`sha256` + normalize + 窗口查询 + 写哈希）；接入 gate（在 LLM 判定之后执行，避免重复内容重复付费）；运行 4.1 至 PASS。

## Phase 5 · 队列端点与审计

- [ ] 5.1 【测试先行】新增 `ModReviewQueueControllerTests`：`GET` 按 `status` 过滤与分页、按版区隔离；`approve` 使内容 `published` 并写 `content.approve` 审计；`reject` 使内容 `deleted` 且 `DeletedBySub` 为系统标识并写 `content.reject` 审计（含原判定 Metadata）；匿名/非版主 401/403；断言 FAIL。
- [ ] 5.2 新建 `ModReviewQueueController`（`/api/mod/review-queue`），复用 `ForumModerationAccessService` 做版区隔离；`ModerationAuditActions.Known` 追加 `content.approve` / `content.reject`，`ModerationAuditPresentation.ActionLabel` 补中文；运行 5.1 至 PASS。
- [ ] 5.3 【测试先行】补一条「入队不写审计」测试：入队后 `forum_moderation_audit` 记录数为 0；断言 FAIL 后确认实现无审计写入，保留测试作回归。

## Phase 6 · 前端（forum-ui 合规）

- [ ] 6.1 `clients.ts` 新增 `ReviewQueueItemDto` / `listModerationReviewQueue` / `approveModerationReviewItem` / `rejectModerationReviewItem`；补契约单测 `clients.review-queue.test.ts` 并绿。
- [ ] 6.2 `ModerationHubTabs.vue` 增第 5 个 Tab「内容复审」（`canModerate` 可见）；`router/index.ts` 注册 `moderation/review-queue`。
- [ ] 6.3 新建 `ModerationReviewQueueView.vue`：复用举报队列的「列表 + 行内展开 + 单行动作」布局，展示 verdict/confidence/reason/来源，动作「通过」/「驳回」；仅用 forum-tokens 变量、8px 网格、无 `:deep`、暗色模式、空态；`npx vitest run` 全绿。
- [ ] 6.4 `ForumPostDetailView` 与「我的帖子」对待审内容显示「审核中」徽标（作者与版主可见）；`npx vitest run` 全绿。

## Phase 7 · 全量验证与 change-review

- [ ] 7.1 `dotnet build backend/src/JIssWeb.sln` 0 错 0 警；`dotnet test` 新增测试全绿（既有 15 条 JWT 失败单列基线）；Report.Api 36/36；`npx vitest run` 全绿。
- [ ] 7.2 `openspec validate --all` 全通过；逐条比对 spec 与实现，修正漂移。
- [ ] 7.3 真实链路演练：重建 model-api 容器 → 开启开关 → 发一条广告特征内容 → 确认 `pending_review` 与队列条目 → 运营者 approve → 确认公开可见；截图或 curl 留证。

## Phase 8 · PR 与收尾

- [ ] 8.1 提交（Conventional Commits 中文）、推送分支、开 PR、等 CI 双 job 通过、合并 master。
- [ ] 8.2 `openspec archive turing-arena-ai-moderator --yes`；`pm-plan.yaml` 同步 V0.3 里程碑勾选与 OpenSpec 备注；推送归档提交。
