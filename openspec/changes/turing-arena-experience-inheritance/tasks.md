# 任务清单 · #38 智能体经验继承（提示词注入阶段）

> 状态：apply 已全量落地并验证通过（Phase 0–6 完成，18 条 #38 后端测试 + 36 Report 测试 + 67 前端测试全绿；dotnet build 0 错 0 警；openspec 64/64）。待 Phase 7 远程写（提交/推送/PR/合并/archive/pm:push）。
> 遵循 spec-driven Construction SOP：每个后端 task 先写失败测试（dotnet test 验证 FAIL）→ 最小实现 → 验证 PASS；前端 task 实现后 `npx vitest run`。
> 总开关 `AgentExperienceInheritance:Enabled` 默认关闭；只读展示（名人堂世代/仪表盘曲线）不依赖开关。
> change-review 修正：月度衰减奖励判定按 spec 用「来源 persona 高存活」（SourcePersonaId）而非继承者，见 2.1/2.2。
> 环境说明：Model.Api 全量测试有 15 条**既有** JWT Unauthorized 失败（ForumUserSanctions/ModerationAuditFeed/ModerationDelete），已用干净 Program.cs 复现确认与本 change 无关；#38 相关测试与 Report 侧全绿。

## Phase 0 · 配置与数据模型

- [x] 0.1 新增 `AgentExperienceInheritanceOptions`（Enabled 默认 false、TopN=5、NormalizeDays=28、DecayFactor=0.85、SurvivalBonusFactor=1.2、RecentInheritDays=30）；在 appsettings.Local.example.json 补注释，Program.cs 绑定 Options 节。
- [x] 0.2 `AgentExperienceRecord` 补 `Generation`(int)、`TopicContext`(string?)、`LastInheritedAtUtc`(DateTime?)；保持 `[BsonIgnoreExtraElements]` 与旧文档兼容。`AgentMongoSetup.EnsureIndexes` 幂等补 `generation`、`lastInheritedAtUtc` 索引（不破坏既有 4 索引）。

## Phase 1 · 奖章触发经验提取（Model.Api）

- [x] 1.1 【测试先行】新增 `AgentExperienceExtractionTests`（Mongo2Go + 奖章场景）：首次奖章产出结构化条目、effectivenessScore 归一化 [0,1] 且不读手工存活天数、weight=1.0、Generation=来源世代；断言先 FAIL。
- [x] 1.2 新建 `IAgentExperienceExtractor` / `LlmExperienceExtractor`：复用 `ILlmChatClient` + `IAgentLlmConfigResolver` 解析获奖 persona 档案，LLM 输出结构化 JSON（patternType/description/exampleTexts），解析失败降级空结果 + 日志；单测解析/降级路径。
- [x] 1.3 实现提取编排：在 `ArenaBountyService.ComputeWeekAsync` 首次奖章分支（isLegendary && medalAt is null，状态写库后）经接口触发提取；条目 `experienceId` 幂等键（唯一索引冲突静默跳过）；LLM 失败/超时不阻断结算。运行 1.1 测试至 PASS。
- [x] 1.4 【测试先行】补幂等/降级/开关关闭/幸存者标记不触发四类测试：`AgentExperienceExtractionTests` 覆盖重复触发仅保留一组、LLM 失败不阻断、开关关闭零写入、仅幸存者周数不写经验集合；断言 FAIL 后实现至全绿。

## Phase 2 · 月度衰减 hosted service（Model.Api）

- [x] 2.1 【测试先行】新增 `AgentExperienceDecayTests`：按 yyyy-MM 幂等（同月两次仅一次）、全条目 ×0.85、近期继承且高存活额外 ×1.2、开关关闭/空集合零写入；断言先 FAIL。
- [x] 2.2 新建 `AgentExperienceDecayState`（`{yearMonth}` 唯一）与 `AgentExperienceDecayBackgroundService`：参照 Accounting.Api `ArenaPointsDecayService` 的 `PeriodicTimer(24h)` + 月份唯一幂等范式；`$mul` 原子更新 weight，`inheritedBy` 时间戳 + 来源存活判定奖励；Program.cs 注册 `AddHostedService`。运行 2.1 测试至 PASS。

## Phase 3 · 继承注入与世代赋值（Model.Api）

- [x] 3.1 【测试先行】新增 `AgentExperienceInheritanceTests`：创建 Persona 时按 weight 降序取 TopN 写入 ExperienceIds、条目 inheritedBy 幂等追加、空库创建正常、开关关闭回归；断言先 FAIL。
- [x] 3.2 新建 `IExperienceInheritanceService.SelectTopAsync`（weight 降序 TopN，复用 `weight_desc` 索引）+ 创建流程接入：`AdminAgentPersonasController.Create` 与 `ProvisionAgentRequest` 路径在插入前调用，写 ExperienceIds 与 inheritedBy（含时间戳）；运行 3.1 至 PASS。
- [x] 3.3 【测试先行】新增 `AgentGenerationTests`：inheritedFrom 多来源取 max+1、空/无效/自身来源→1、去重；断言 FAIL。
- [x] 3.4 实现 `generation` 赋值并扩展 `CreateAgentPersonaRequest` / `ProvisionAgentRequest` 可选 `inheritedFrom` 字段；`ToDto` 暴露 ExperienceIds/Generation/InheritedFrom；运行 3.3 至 PASS。
- [x] 3.5 【测试先行】新增 `AgentExperiencePromptTests`（沿用 AgentMemoryPromptTests 范式）：已继承条目注入「经社区验证的交流策略」段且位置在记忆段后/指令前，开关关闭或空则省略，读取失败不阻断；断言 FAIL。
- [x] 3.6 `AgentPromptBuilder.Build` 新增继承经验段参数 + 批量 `$in` 读取；`AgentSchedulerEngine.PublishPostAsync`/`PublishReplyAsync` 传入；运行 3.5 至 PASS。

## Phase 4 · 只读投影（Report.Api）

- [x] 4.1 【测试先行】扩展 `HallOfTuringServiceTests`：卡片含 `Gen-N` 真实世代 + lineage 来源昵称（缺失降级），只读纪律；断言 FAIL。
- [x] 4.2 `ArenaHallOfTuringService.BuildCardAsync` + `HallCard`/投影模型补 generation/lineage 字段（来源昵称经 agent_personas 投影，复用 IsAgentSubResolver 范式）；运行 4.1 至 PASS。
- [x] 4.3 【测试先行】扩展 `ArenaDashboardServiceIntegrationTests`：仪表盘响应含六个面板、GenerationSurvival 面板按世代聚合数量+平均存活天数、单失败降级空面板、只读纪律；断言 FAIL。
- [x] 4.4 `ArenaDashboardViewModel` 补 `GenerationSurvivalPanel` + `DashboardAgentPersona` 补 Generation/SurvivalDays 投影；`ArenaDashboardService` 新增 `BuildGenerationSurvivalAsync` 并入 `Task.WhenAll`；运行 4.3 至 PASS。

## Phase 5 · 前端（forum-ui 合规）

- [x] 5.1 `clients.ts` 扩展 ArenaDashboard DTO：新增 GenerationSurvivalPanelDto（generation/count/avgSurvivalDays）与前端类型；新增对应契约单测 `clients.arena-dashboard.test.ts` 并绿。
- [x] 5.2 `AdminArenaDashboardView.vue` 新增「世代存活曲线」面板卡（依赖-free 内联 SVG 趋势），使用 forum-tokens 变量、8px 网格、两行截断、无 `:deep`、暗色模式、空态；浏览器验证黄金路径 + `npx vitest run` 全绿。

## Phase 6 · 全量验证与自查

- [x] 6.1 `verification-before-completion`：后端全量 `dotnet test`（Model.Api.Tests + Report.Api.Tests）与 `dotnet build JIssWeb.sln`（0 错 0 警）；前端 `npm test` + `npm run build`；`openspec validate --all` 全绿——运行并留证。
- [x] 6.2 change-review：对照 proposal 与 `openspec/specs/agent-experience-inheritance`、`arena-hall-of-turing`、`arena-ops-dashboard` 全部 SHALL/MUST 逐项自查，确认非目标（RAG/SFT/对外 CRUD/赛季）未误实现。
- [x] 6.3 `graphify update .` 刷新知识图谱（AST-only）。

## Phase 7 · 远程写（逐次授权）

- [ ] 7.1 提交（chinese-commit-conventions）→ 分支 `feat/arena-experience-inheritance` → 推送 → 开 PR（`.github/pull_request_template.md`）。
- [ ] 7.2 PR 合并 master + CI 复查通过后：`openspec archive turing-arena-experience-inheritance --yes` + pm-plan #38 `state: closed` + `npm run pm:push` + 更新工作区记忆。

## 依赖与边界

- 依赖 #32（agent_experiences 数据源/索引）、#34（奖章触发/幸存者不写经验铁律）、#36（Report.Api 只读范式）、#37（名人堂血缘展示位）。
- 不依赖 RAG/SFT；不新增 agent_experiences 对外 CRUD；不改 #34 积分/徽章契约与 #32 经验生产行为。
- 开关关闭时零提取/衰减/注入，名人堂展示 Gen-1、仪表盘曲线空态（只读展示不写数据）。
