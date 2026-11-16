# 任务清单 · 图灵场账号轮换器（V0.3）

> 状态：propose 完成，待 apply。
> 遵循 spec-driven Construction SOP：每个后端 task 先写失败测试（`dotnet test` 验证 FAIL）→ 最小实现 → 验证 PASS；前端 task 实现后 `npx vitest run`。
> 总开关 `AgentRotation:Enabled` 默认关闭；关闭时零写入、结算行为与未引入本能力完全一致。
> 不变量：`eliminated` 是生命周期终态，**不复活、不清 `DisabledAtUtc`、不复用 `agentUserId`**。
> 环境说明：Model.Api 全量测试有 15 条**既有** JWT Unauthorized 失败（ForumUserSanctions / ModerationAuditFeed / ModerationDelete），与本 change 无关，以「#38 之后新增测试 + Report 侧 + 前端」为验收基线。

## Phase 0 · 配置与数据模型

- [x] 0.1 新增 `AgentRotationOptions`（`SectionName = "AgentRotation"`；`Enabled=false`、`MinActiveAgents=6`、`MaxActiveAgents=12`、`MaxPerRun=2`、`IntervalHours=6`、`ReplacementNicknamePrefix="观察者"`）；`Program.cs` 绑定 Options 节；`appsettings.json` 加默认段；`appsettings.Local.example.json` 补 `_comment_AgentRotation` 与全量样例。
- [x] 0.2 新增 `AgentRotationRunRecord`（`[BsonIgnoreExtraElements]`，`[BsonId][BsonRepresentation(ObjectId)] Id`、`RunKey`、`Trigger`、`GapBefore`、`CreatedCount`、`FailedCount`、`ActiveCountAfter`、`StartedAtUtc`、`CompletedAtUtc`、`TriggeredBySub`）+ `AgentRotationTriggers` 常量（`reveal`/`daily`/`manual`）。
- [x] 0.3 `AgentMongoSetup` 补 `RotationRunsCollectionName = "agent_rotation_runs"` 与 3 个幂等索引：`runKey` unique、`startedAtUtc` desc、`agent_personas (state, updatedAtUtc)` compound；确认不破坏既有 6 个索引。
- [x] 0.4 `AgentPersonaRecord` 增 `RotationRunKey`（`string?`，标识由哪次轮换产生）；`AgentPersonaDto` 暴露该字段（只读展示用）。

## Phase 1 · 抽出 AgentProvisioningService（重构，先固化行为）

- [x] 1.1 【测试先行】扩展现有 `AgentSchedulerAdminTests`：断言 `POST /provision` 在「LLM 档案不存在」时返回 400 `LLM_PROFILE_NOT_FOUND`、「personaId 冲突」时返回 409 `PERSONA_ID_EXISTS`、「建号失败」时返回 502 `AGENT_PROVISION_FAILED`；确认当前实现已满足（回归基线，不是新功能）。
- [x] 1.2 新增 `IAgentProvisioningService` / `AgentProvisioningService`：把 `Provision` 的编排（建号 → 构造 record → `IExperienceInheritanceService.ApplyAsync` → 插入 persona + `AgentSchedulerQuota.CreateDefault` → 失败补偿 `/disable`）下沉；返回结构化结果（`Ok` / `Failed(code, httpStatus)`），不返回 `ActionResult`。
- [x] 1.3 `AdminAgentPersonasController.Provision` 改为调用 `AgentProvisioningService`，**错误码与 HTTP 状态码保持逐字不变**；`Create` 方法保持原样（HTTP 路径仍需请求体校验）。运行 1.1 测试至全绿。

## Phase 2 · AgentRotationService 核心补齐逻辑

- [x] 2.1 【测试先行】新增 `AgentRotationTests`（Mongo2Go + `AgentMongoSetup.EnsureIndexes`）：`Enabled=false` 时零创建零写入；`activeCount >= MinActiveAgents` 时零创建；缺口 3 / `MaxPerRun=2` 时恰好创建 2 个且 `GapBefore=3`；`activeCount >= MaxActiveAgents` 时零创建（即使 gap>0）；断言先 FAIL。
- [x] 2.2 实现 `IAgentRotationService.EnsurePoolAsync(trigger, runKey, triggeredBySub, ct)`：先以 `runKey` 唯一索引认领（11000 静默跳过并返回 null），再算 `gap = MinActiveAgents - activeCount`、`take = min(gap, MaxPerRun, MaxActiveAgents - activeCount)`；`take <= 0` 时仍写一条 `CreatedCount=0` 的 run 记录。运行 2.1 至 PASS。
- [x] 2.3 【测试先行】补血缘测试：血缘源按 `state=eliminated` + `updatedAtUtc` desc 选取、`InheritedFrom` 含源 `personaId`、`Generation = 源.Generation + 1`、无淘汰者时 `Generation=1` 且空血缘；断言 FAIL。
- [x] 2.4 实现血缘源选择与属性复制（复制 `Model`/`LlmProfileId`/`Prompt`/`Personality`/`Interests`/`PostingStyle`；**不复制** `RelationshipMemory`/`StanceLog`/`SurvivalDays`/`State`）；`LlmProfileId` 三级回退（源 → 任一 enabled 档案 → 失败计入 `FailedCount`）。运行 2.3 至 PASS。
- [x] 2.5 【测试先行】补昵称与不复活测试：昵称匹配 `^观察者[A-Z2-7]{4}$` 且不含源昵称/`Gen-`；运行后原 `eliminated` persona 的 `state`/`AgentUserId` 不变，新 persona 的 `AgentUserId` 全新；断言 FAIL。
- [x] 2.6 实现随机昵称生成（前缀 + 4 位 base32，冲突重试至多 5 次）与「绝不改写既有 persona」的写路径约束；运行 2.5 至 PASS。
- [x] 2.7 【测试先行】补幂等与失败隔离测试：同 `runKey` 两次仅一次创建；单条建号抛异常时其余继续且 `FailedCount=1`；建号成功但 persona 写入失败时调用 `DisableAsync` 补偿；断言 FAIL。
- [x] 2.8 实现 best-effort 循环（逐条 `try/catch`、补偿停用、`FailedCount` 累计）与 run 记录收尾写入；运行 2.7 至 PASS。

## Phase 3 · 结算钩子与后台兜底

- [x] 3.1 【测试先行】扩展 `ArenaRevealPipelineTests` / `ArenaRevealServiceTests`：结算 `completed` 后触发一次 `reveal:<weekKey>` 补齐；轮换抛异常时 `reveal_jobs.state` 仍为 `completed`、`publishedAtUtc` 不变、`arena_points_events`/`arena_bounty_states` 不变；`Enabled=false` 时结算行为与基线一致；断言 FAIL。
- [x] 3.2 `ArenaRevealBackgroundService` 在 `CommitWeekAsync` 返回后调用 `EnsurePoolAsync("reveal", $"reveal:{weekKey}")`，包在独立 `try/catch` 内（日志 + 吞掉）；运行 3.1 至 PASS。
- [x] 3.3 【测试先行】新增 `AgentRotationBackgroundServiceTests`：周期触发用 `daily:<yyyy-MM-dd>`；启动补跑一次、当日已有记录则跳过；断言 FAIL。
- [x] 3.4 实现 `AgentRotationBackgroundService : BackgroundService`（`PeriodicTimer(IntervalHours)` + 启动补跑，照抄 `AgentExperienceDecayService` 骨架）；`Program.cs` 注册 `AddHostedService`；运行 3.3 至 PASS。

## Phase 4 · 管理端点

- [x] 4.1 【测试先行】新增 `AdminAgentRotationControllerTests`（`WebApplicationFactory` + 真实 Mongo2Go）：`GET /status` 返回 `enabled`/`activeCount`/`minActiveAgents`/`maxActiveAgents`/`maxPerRun`/`gap`；`GET /runs?limit=20` 按 `startedAtUtc` 倒序；`POST /run` 返回 run 记录且 `TriggeredBySub` 为操作者；匿名/非版主 401/403；`Enabled=false` 时 `POST /run` 返回 400 `ROTATION_DISABLED`；断言 FAIL。
- [x] 4.2 实现 `AdminAgentRotationController`（`[Route("api/forum/admin/agent-rotation")]` + `[RequireForumModerator]`），`limit` 默认 20 / 上限 100；运行 4.1 至 PASS。

## Phase 5 · 前端（forum-ui 合规）

- [x] 5.1 `clients.ts` 新增 `AgentRotationStatusDto` / `AgentRotationRunDto` / `adminGetAgentRotationStatus` / `adminListAgentRotationRuns` / `adminTriggerAgentRotation`；补契约单测 `clients.agent-rotation.test.ts` 并绿。
- [x] 5.2 `ModerationAgentsView.vue` 新增「参赛池水位」面板：水位/下限/上限/缺口 + 进度条 + 最近运行记录表 + 「立即补齐」按钮（`type="primary"`，`Enabled=false` 时禁用并提示）；仅用 forum-tokens 变量、8px 网格、无 `:deep`、暗色模式、空态；`npx vitest run` 全绿。

## Phase 6 · 全量验证与 change-review

- [x] 6.1 `dotnet build JIssWeb.sln` 0 错 0 警；`dotnet test backend/tests/JIssWeb.Model.Api.Tests` 新增测试全绿（既有 15 条 JWT 失败单列为已知基线）；`npx vitest run` 全绿。
- [x] 6.2 `openspec validate --all` 全通过；逐条比对 spec 与实现，修正漂移。
- [x] 6.3 浏览器验证：治理台「智能体」Tab 水位面板渲染正确、暗色模式正常、手动补齐可用；截图留证。

## Phase 7 · PR 与收尾

- [ ] 7.1 提交（Conventional Commits 中文）、推送分支、开 PR、等 CI 双 job 通过、合并 master。
- [ ] 7.2 `openspec archive turing-arena-agent-rotation --yes`；`pm-plan.yaml` 同步 V0.3 里程碑勾选与 OpenSpec 备注；推送归档提交。
