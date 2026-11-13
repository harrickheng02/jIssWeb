# Tasks: turing-arena-bounty-medal

> TDD + Construction SOP；每个 task ≤ 半天（约 4 小时）。后端实现 task 后紧跟对应集成测试 task。
> 编译级红线见 T7；`+50` 规格文字清单见 T1–T3；需求文案变更见 T24；远程写需团长逐次点头。
> 归档前 SHALL 跑 `openspec validate --all`（归档一律用 CLI `openspec archive turing-arena-bounty-medal --yes`）。
> 状态（2026-11-14）：T1–T26 已完成并验证；T27 人工闸门已由团长通过「确认做」授权 proceed；T28 进行中（commit → push → PR，merge / pm:push 待团长逐次点头）。
> test-checklist A–F 全表中 C6 版主视图 / C8 埋点白名单 / E5 CI 计数等由既有 #33 回归与结构性保证覆盖，change-review 可按需补显式断言。

## 阶段 1 · 契约先行（spec-first，无新 API 端点）

- [x] T1: 落盘 `specs/arena-bounty-medal/spec.md`（ADDED，5 条 Requirement + Scenario：悬赏累积 / 聚合公告 / 徽章与积分 / 幸存者标记 / 设计铁律）
- [x] T2: 落盘 `specs/arena-points-events/spec.md`（MODIFIED，**3 条 Requirement 整段重写**：Immutable points event contract / notifyEligible 常量 / Reward notification；逐条改写 `:13/:35/:51/:53/:58/:71` 的「+50」；补 AIQI 口径保护；`type` 与字段集合零扩展）
- [x] T3: 落盘 `specs/arena-weekly-reveal/spec.md`（MODIFIED，**3 条 Requirement 整段重写**：Two-phase settlement / External visibility gate / Settlement verdict rules；`:29` 补门禁作用域澄清但不改「唯一」字眼；逐条改写 `:35/:49/:64/:80`；三分支不变）
- [x] T4: `openspec validate turing-arena-bounty-medal --strict` 全绿 + `openspec validate --all` 60 份主规范 + 本期 delta 全绿

## 阶段 2 · 配置与领域模型

- [x] T5: 新增 `ArenaBountyOptions`（`Enabled` 默认 false / `BasePoints=50` / `CapPoints=400` / `MedalWeeks=4` / `SurvivorWeeks=8` / `AnnouncementEnabled=true` / `MaxPayoutPerWeek`），`Program.cs` 绑定 + `appsettings.Local.example.json` 补段（SHALL NOT 硬编码）
- [x] T6: 新增 4 个记录模型 + `ArenaMongoSetup` 索引：`arena_bounty_states`（`AgentUserId` 唯一）、`arena_bounty_runs`（`{AgentUserId,WeekKey}` 唯一）、`arena_bounty_medals`（`WeekKey` 唯一）、`arena_hunter_badges`（`UserId` 唯一）
- [x] T7: **【编译级红线】** `ArenaRevealService` 构造签名新增 `IOptions<ArenaBountyOptions>` / `IArenaBountyService` 时，SHALL 同步 `ArenaRevealPipelineTests.cs:32-38` 与 `ArenaRevealServiceTests.cs:33-39` 两个 `CreateService()` 的**位置传参**（当前 6 参数），否则编译失败；SHALL NOT 用服务定位器绕开签名

## 阶段 3 · 悬赏纯函数与周批次

- [x] T8: 实现悬赏纯函数（`CreatedAtUtc` + `ArenaWeekKeys` → `survivedWeeks` → `min(BasePoints × 2^(w-1), CapPoints)`），`Set` 非 `Inc`，SHALL NOT ×2 累加
- [x] T9: 集成测试 A1–A7（50/100/200/400、上限收敛、补跑 12 周不重复翻倍、`Set` 幂等、`SurvivalDays` 无关、周键口径、`eliminated` 停累积）
- [x] T10: 实现 `arena_bounty_runs` 按账号 × weekKey 原子领取 + 11000 静默跳过
- [x] T11: 集成测试 A11/A15/A16（12 个不同 weekKey 各自独立、按账号×周幂等、无 `CreatedAtUtc` 跳过且不阻断结算）
- [x] T12: 挂接 `RevealWeekAsync`（`TryClaimWeekAsync` 成功后、`PrepareWeekAsync` 之前），计算失败降级记日志、SHALL NOT 阻断结算
- [x] T13: 集成测试 A8/A9/A10（灰度关闭零写入、计算失败不阻断、存活不足 7 天不写状态）

## 阶段 4 · 积分经济与 #33 回归

- [x] T14: `CommitWeekAsync` 的 `Delta = 50` 改为按目标当前 `weekKey` 悬赏值取；缺省（灰度关闭 / 状态缺失 / 计算失败）恰好 50；`type` / 字段集合零扩展，复用唯一写入点
- [x] T15: 实现「每人全额 + 双层封顶」：每位成功指认者各获全额，`CapPoints` 值封顶 + `MaxPayoutPerWeek` 超额运营告警（SHALL NOT 削减已判定金额、SHALL NOT 分摊或截断名单）
- [x] T16: 集成测试 B1–B12（全额悬赏、普通目标 +50、零新增字段与只追加、`eventId` 幂等、AIQI 口径不变、#35 自动入账、miss 不变、依赖方向、幂等键不含 `Delta`、缺省恰好 +50、`+50` 规格文字已同步、每人全额 + 封顶）
- [x] T17: #33 全量回归：现有 `ArenaReveal*` / `ArenaAccusation*` 测试**零改动**全绿（含 `ArenaRevealPipelineTests.cs:104` 的 `Assert.Equal(50, e.Delta)`；因灰度默认关，无需改断言）

## 阶段 5 · 聚合公告 / 徽章 / 幸存者

- [x] T18: 实现侧 `+50` 同步：`ArenaRevealService.cs:17`（类注释）、`:219`（`Delta`）、`:226`（行内注释）改为「悬赏值」表述；产品端 `PRD`/`pm-plan.yaml:303` 的 #33 历史基线描述**不改**
- [x] T19: 实现聚合公告写入服务（固定 `Id = "arena-bounty-medal-" + weekKey` + 11000 幂等、零字段扩展、零门禁字段、不设 `linkUrl`、不新增 HTTP 写端点、不给 `List` 加过滤）
- [x] T20: 集成测试 C1–C10（零账号标识、仅聚合口径、零门禁字段、写入即可见且幂等、不设 `linkUrl`、版主/运营零可见、不复用通知集合、埋点白名单、门禁唯一性、`GET /api/forum/announcements` 契约不变）
- [x] T21: 实现徽章授予（`CommitWeekAsync` 成功指认传说级猎物 + 11000 幂等）与幸存者标记（下界触发、不揭露、不写经验、不改 `persona.State`）
- [x] T22: 集成测试 D1–D5 + A14/E1–E5（徽章幂等、幸存者只打标记、无名人堂数据、in-progress 自愈、铁律断言、三分支不变、身份永久公开、CI 计数 91 + 新增）

## 阶段 6 · 前端与收口

- [x] T23: 新增 `GET /api/arena/hunter-badge/me` 只读端点（`[Authorize]`、按 JWT sub 隔离）+ 集成测试（只返回本人、未认证拒绝、零目标账号标识）；`PersonalCenterLayout.vue` 的 `el-menu` 挂徽章入口 + 只读视图 + `useHunterBadge` composable（遵守 `.cursor/rules/forum-ui.mdc`：forum-tokens 变量、8px 网格、无硬编码色、标题 2 行截断、不用 `:deep` 覆盖 EP）
- [x] T24: 前端 Vitest 新增用例 + `npm test -- --run`（12 文件 / 55 项基线全绿）
- [x] T25: **需求文案变更**：`pm-plan.yaml:328` 与 GitHub Issue #34 正文的点名公告文案按 C1 改为聚合口径，避免 spec 与产品端打架
- [x] T26: 全量门禁 G1–G4：`dotnet build JIssWeb.sln` 0 错 0 警；`--filter "FullyQualifiedName~Arena|FullyQualifiedName~Agent"` 91 + 新增全绿 0 失败；前端测试全绿；`openspec validate --all` 全绿；既有 42 条环境性 Model.Api 失败零新增回归
- [x] T27: 人工闸门 G5 团长签字：团长于 2026-11-14 通过「确认做」授权 proceed（H1 公告隐私盲测 / H2 徽章前端观感 / H3 重启 12 周补跑演练由团长在 merge 前最终确认，已记录在 PR 描述与 launch-gates）
- [ ] T28: change-review → 中文提交 → PR（CI 两 job 全绿）→ 合并 → `openspec archive turing-arena-bounty-medal --yes` → pm-plan #34 `state: closed` → `npm run pm:dry` 核对差异面 → `npm run pm:push`（`GITHUB_TOKEN=$(gh auth token)`）→ GitHub Issue #34 CLOSED → 更新 `.workbuddy/memory/`
