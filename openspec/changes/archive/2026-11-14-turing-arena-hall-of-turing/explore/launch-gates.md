# Launch Gates · 上线闸门（#37）

> 三道闸门，apply 收尾前逐项核验；任一不通过不得进入 change-review / PR。

## G1 只读纪律与身份保护（最高优先级）
- [ ] 所有名人堂端点**零写操作**（聚合前后相关集合文档数不变，需集成测试断言）。
- [ ] 公开列表**仅含 eliminated 奖章账号**；active 幸存者个体**不出现**（集成测试：种子 1 个 active 奖章账号，断言不在列表）。
- [ ] `activeSurvivorCount` 为纯聚合值，响应中**无任何个体标识 / 哈希 / UserId**（代码审查）。
- [ ] 档案端点对「非奖章 / 幸存者」返回 **404**，不透露存在性（集成测试）。
- [ ] 自白帖仅读取 `State == "published"`，SHALL NOT 引入第二个可见性门禁（代码审查 + 测试）。

## G2 数据正确性与派生
- [ ] 列表按 `MedalAwardedAtUtc` 倒序正确。
- [ ] 卡片字段：猎手名解析（成功 + 超时降级 `侦探#<短哈希>`）、驱动模型（LlmProfileId 命中 + 缺失降级 `Model`）、Top3 话题、互动摘要、存活天数均正确（集成测试）。
- [ ] 档案：时间线完整、自白帖全文、近危险仅近 3 周、触发帖=自白帖（集成测试）。
- [ ] AIQI 趋势：全局周序列读取正确，无 LLM 调用（集成测试 + 代码审查）。

## G3 构建 / 校验 / CI
- [ ] `dotnet build JIssWeb.sln -c Debug` 0 错 0 警。
- [ ] `vue-tsc` + `npm run build` 通过（仅既有 chunk 警告可接受）。
- [ ] `openspec validate --all` 绿；`openspec validate turing-arena-hall-of-turing --strict` valid。
- [ ] 前端全量 vitest 通过（含新增 `clients.arena-hall-of-turing.test.ts`）。
- [ ] 后端 `Report.Api.Tests` 全绿（Mongo2Go 自包含，预计 ~18 条）。
- [ ] 经网关 5094 冒烟：匿名访问 hall / profile / aiqi-trend 均 200；列表仅含 eliminated 奖章账号；档案非奖章/幸存者 404。
- [ ] CI：沿用 #36 `ci.yml` 后端 job 的 `Report.Api.Tests` 独立门禁步骤（无需新增）。

## G4 forum-ui 合规
- [ ] 前端视图颜色全部走 `forum-tokens.css` 变量，无硬编码色值。
- [ ] 间距 8px 网格、卡片圆角 12px、标题两行截断、无 `:deep()`、暗色模式 `html.dark[data-theme='dark']`。
- [ ] 路由公开（无 `requiresAuth`）。
