# 任务清单 · #37 图灵名人堂

> 状态：apply 全量落地并验证通过（T1–T15 已闭环：后端 16 测试 + 前端 66 测试 + docker 真实 Mongo 冒烟 + openspec 63/63 绿）。待团长逐次授权远程写 T16（提交/推送/开PR）→ T17（合并+同步+CI复查）→ T18（归档+关闭 Issue #37）。
> 遵循 spec-driven：TDD + Construction SOP。远程写（提交/推送/PR/合并/archive/pm:push）逐次点头。
> 实际落地偏离原方案之处（已裁定/已验证）：① 猎手名解析采用 `MaskedHunterNameResolver`（脱敏代号，D8.1 回退——User.Api 无公开用户读端点），不引 HttpClient；② 档案聚合并入单一 `ArenaHallOfTuringService`（无独立 `ArenaProfileService`）。

## Phase 0 · explore（已完成）
- [x] T0. 技术侦察 + 数据源映射（6 验收 → 集合字段）；4 项关键裁定 D2/D6/D4-D5/D7 团长确认；产出 proposal/design/tasks + ADDED delta spec（7 Requirement）。

## Phase 1 · 后端基础设施（Report.Api 接入，复用 #36 解耦范式）
- [x] T1. Report.Api `Program.cs` 注册 `IHunterNameResolver`/`MaskedHunterNameResolver`（Scoped）+ `IHallOfTuringService`/`ArenaHallOfTuringService`（Scoped）；复用既有 keyed `IMongoDatabase("arena-dashboard-model")`、`IsAgentSubResolver`、`AddJIssWebCoreApi`。
- [x] T2. 新建 `HallOfTuringModels.cs`：`HallCard` / `ArenaProfileViewModel` / `AiqiTrendPoint` / `InteractionSummary` / `NearDangerMarker` 等子结构（camelCase，含 `externalAttribution` 可空预留）。
- [x] T3. 新建只读投影模型 + Bson 稳健性：`Hall*` 最小投影加 `[BsonIgnoreExtraElements]`；`arena_aiqi_results` `_id` 为原生 ObjectId 对应 `Id` 加 `[BsonRepresentation(BsonType.ObjectId)]`（沿用 #36 已验证坑）；`HallCollectionNames` 镜像 9 个集合名。

## Phase 2 · 后端只读聚合服务 + 端点
- [x] T4. `ArenaHallOfTuringService.HallAsync`：过滤 `arena_bounty_states.MedalAwardedAtUtc!=null` ∩ `agent_personas.State=="eliminated"`；聚合卡片字段（finalBounty / 猎手名解析 / 驱动模型 / 世代 / Top3 话题 / 互动摘要 / 存活天数）；`activeSurvivorCount` 聚合（State=="active" 奖章账号，仅计数无个体）；`MedalAwardedAtUtc` 倒序。
- [x] T5. 猎手名解析（D8.1）：`arena_points_events`(`Type=="accusation_hit"`,`TargetUserId==agent`) → `UserId` → `MaskedHunterNameResolver` 脱敏代号（侦探#短哈希）；SHALL NOT 暴露原始 UserId。
- [x] T6. `ArenaHallOfTuringService.ProfileAsync(personaId)`：校验 persona 为「奖章 + eliminated」，否则 null（控制器转 404）；聚合时间线（forum_posts+forum_replies by AuthorSubId，按时间升序）、自白帖全文（ConfessionPostIds → forum_posts 且 published，触发帖=自白帖）、近危险标注（arena_target_tallies Status=="active" 近 ~3 周）、互动摘要。
- [x] T7. `ArenaHallOfTuringController`：`GET /api/report/arena/hall-of-turing` + `GET /api/report/arena/profile/:personaId` + `GET /api/report/arena/aiqi-trend`（均**匿名**，不加 `RequireForumModerator`/`Authorize`）；只读纪律（测试验证聚合前后文档数不变）。

## Phase 3 · 前端
- [x] T8. 路由 `/arena/hall-of-turing` 与 `/arena/profile/:personaId`（公开，不设 `requiresAuth`/`requiresModerate`；`props:true` 传 personaId）。
- [x] T9. `clients.ts`：新增 `getHallOfTuring()` / `getArenaProfile(personaId)` / `getArenaAiqiTrend()`（复用 `reportApi`）+ 全套 camelCase DTO（HallCardDto / ArenaProfileDto / AiqiTrendPointDto 等）；Profile 的 404 降级为 `{success:false,code:'NOT_FOUND'}`（不抛异常）。
- [x] T10. `ArenaHallOfTuringView.vue`：卡片网格（昵称/悬赏/猎手名/驱动模型/世代/Top3/互动/存活天数）+ 匿名幸存者计数横幅；遵守 `forum-ui.mdc`（forum-tokens、8px 网格、12px 圆角、标题两行截断、无 `:deep`、暗色模式）。
- [x] T11. `ArenaProfileView.vue`：时间线（posts+replies 合并升序）+ 自白帖全文高亮 + 近危险标注（近 3 周说明）+ 互动摘要 + AIQI 趋势图（依赖-free 内联 SVG，直接读 `arena_aiqi_results` 全局周值）；forum-ui 合规。

## Phase 4 · 测试
- [x] T12. 后端单测/集成（Mongo2Go 自包含）：列表过滤（仅 eliminated 奖章 / active 幸存者不出现 / 聚合计数）、卡片派生（猎手名成功+降级 / 驱动模型命中+缺失 / Top3 / 互动）、档案（eliminated→200 全量 / 非奖章·幸存者→404 / 自白仅 published / 近危险仅近 3 周）、匿名端点 200 + 只读纪律、AIQI 趋势读取。共 16 条（6 服务 + 6 控制器 + 4 解析器）全绿，0 警告。
- [x] T13. 前端单测 `clients.arena-hall-of-turing.test.ts`：API 契约（3 路径 + 编码 personaId + limitWeeks 参数）+ 响应形态断言 + Profile 404 降级；5 条全绿。全量 vitest 66 条通过。
- [x] T14. docker 实跑（经 report-api 直连真实 Mongo 7，网关 report-route 为已验证 catch-all）：匿名 `hall-of-turing`→200（空）、`profile/per1`→404 NOT_FOUND（不泄露存在性）、`aiqi-trend`→200（真实读出 `arena_aiqi_results` 原生 ObjectId 行，确认 Bson 稳健 + camelCase 契约）；网关 5094 report-route 配置已核对为 `/api/report/{**catch-all}`。
- [ ] T13. 前端单测：新增 `clients.arena-hall-of-turing.test.ts`（API 契约 + 响应形态断言）；全量 vitest 通过。
- [ ] T14. docker 实跑：经网关 5094 冒烟——匿名访问 hall/profile/aiqi-trend 均 200；列表仅含 eliminated 奖章账号；档案对非奖章/幸存者返回 404。

## Phase 5 · change-review / 远程写（逐次点头）
- [x] T15. `openspec validate --all` 绿（63/63，含 #37 change）；后端全解 `dotnet build` 0 错 0 警（JIssWeb.sln Debug）；前端 `vue-tsc` + `npm run build` 通过（仅既有 chunk 体积提示，非错）；全量 vitest 66 条通过；中文提交信息就绪。
- [x] T16. 提交 + 推送 + 开 PR：**分支 `feat/arena-hall-of-turing`**（从 master，仅精选 #37 文件，不动 `.claude/`/`.workbuddy/`/`graphify-out/` 现场）→ 推送 origin → **PR #50** → master。未提交用户现场改动保持原状。
- [x] T17. 合并 PR + 本地 master 同步 + master CI 复查：PR #50 合并（merge commit `4e05a9b`）→ `git fetch` + `git branch -f master origin/master` 同步本地 master 引用（不 checkout，避免扰动用户现场未提交改动）→ master CI（运行 43623227168）**success**（Arena + Report.Api 两测试 job 全绿；仅需 Node20 弃用与 git 128 无害注释）。
- [ ] T18. `openspec archive turing-arena-hall-of-turing --yes` + pm-plan #37 `state:closed` + `npm run pm:push`（Issue #37 CLOSED）+ 更新记忆（待团长授权）。

## 依赖与衔接
- 依赖 #34（arena-bounty-medal：奖章/幸存者数据落点）、#36（Report.Api 只读聚合出口范式 + IsAgentSubResolver + 网关 report-route）、#33（结算批次可见性门禁 / 自白帖 / arena_target_tallies）。
- 不依赖 #38（世代演进）；本期直接读 `Generation` 展示并标注恒为 1。
- 推迟：LLM 故事摘要 / 对话片段生成、赛季概念 / 赛季结束自动触发（按 D6 裁定，归赛季纪念子项 / V1.0）。

## 实施期待定项（D8）
- D8.1 猎手名解析端点（User.Api 公开用户读端点确认；否则去规范化回退）。
- D8.2 存活天数口径（优先 `SurvivalDays`，缺则 `CreatedAtUtc→揭露时间` 推导）。
