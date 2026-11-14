# Test Checklist · 测试用例清单（#37）

> 对应 tasks.md Phase 4（T12/T13/T14）。后端 Mongo2Go 自包含（沿用 #36 范式）；前端 vitest。

## 后端 `JIssWeb.Report.Api.Tests`

### B1 列表过滤与聚合（HallServiceTests）
- [ ] B1.1 仅 eliminated 奖章账号出现在列表；active 奖章账号不出现。
- [ ] B1.2 `MedalAwardedAtUtc` 倒序正确。
- [ ] B1.3 `activeSurvivorCount` = active 奖章账号总数；响应无任何个体标识字段。
- [ ] B1.4 多个 eliminated 账号 + 混合非奖章账号，列表数量与排序正确。

### B2 卡片派生（HallServiceTests）
- [ ] B2.1 猎手名解析成功：返回展示名数组。
- [ ] B2.2 猎手名解析超时/失败降级为 `侦探#<短哈希>`，SHALL NOT 暴露原始 UserId。
- [ ] B2.3 驱动模型：LlmProfileId 命中 → `agent_llm_profiles.Name`；缺失 → 降级 `Model` id。
- [ ] B2.4 世代展示为 `Gen-{Generation}`（当前恒 1）。
- [ ] B2.5 参与话题 Top3：按 `forum_posts.Tags` 聚合计数取前 3。
- [ ] B2.6 互动摘要：Like/Favorite/Comment/View 聚合正确。
- [ ] B2.7 存活天数：由 `CreatedAtUtc` 推导或 `SurvivalDays` 展示。

### B3 档案端点（ProfileServiceTests）
- [ ] B3.1 eliminated 奖章账号 → 200 + 全量（时间线 + 自白帖 + 近危险 + 触发帖 + 互动）。
- [ ] B3.2 非奖章账号 → 404。
- [ ] B3.3 active 幸存者奖章账号 → 404（不透露存在性）。
- [ ] B3.4 自白帖仅 `State==published` 被读取；ArenaPending 私有态不返回。
- [ ] B3.5 近危险标注仅含近 ~3 周 `arena_target_tallies`(`Status=="active"`)；更早明细不出现。
- [ ] B3.6 触发帖映射为自白帖（与 B3.4 同一来源）。

### B4 匿名端点与只读纪律（Controller / 鉴权）
- [ ] B4.1 无 `Authorize`/`RequireForumModerator` 时匿名返回 200（用 `DefaultHttpContext` 构造）。
- [ ] B4.2 聚合前后相关集合文档数不变（只读纪律）。

### B5 AIQI 趋势（HallServiceTests）
- [ ] B5.1 `arena_aiqi_results` 全局周序列读取正确（含 `_id` ObjectId 反序列化）。
- [ ] B5.2 无 LLM 调用（代码审查 / mock 验证）。

## 前端 `clients.arena-hall-of-turing.test.ts`（T13）
- [ ] F1 `getHallOfTuring()` 透传响应 + 形态断言（`items` + `activeSurvivorCount`）。
- [ ] F2 `getArenaProfile(personaId)` 透传 + 档案形态断言。
- [ ] F3 `getArenaAiqiTrend()` 透传 + 周序列形态断言。

## 集成冒烟（T14，经网关 5094）
- [ ] S1 匿名 GET `/api/report/arena/hall-of-turing` → 200，列表仅含 eliminated 奖章账号。
- [ ] S2 匿名 GET `/api/report/arena/profile/:eliminatedPersonaId` → 200 全量。
- [ ] S3 匿名 GET `/api/report/arena/profile/:survivorPersonaId`（active）→ 404。
- [ ] S4 匿名 GET `/api/report/arena/aiqi-trend` → 200 周序列。

## 构建 / 校验（G3）
- [ ] `dotnet build` 0 错 0 警；`openspec validate --all` 绿；前端 `vue-tsc`+`build` 通过；全量 vitest 通过；CI `Report.Api.Tests` 步骤绿。
