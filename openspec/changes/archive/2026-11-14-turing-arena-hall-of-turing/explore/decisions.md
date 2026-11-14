# Decisions · 图灵名人堂关键裁定（#37）

> 团长于 explore 阶段确认 4 项关键裁定（D2 / D6 / D4-D5 / D7），全部采纳推荐默认。本文件为裁定记录，apply 阶段 SHALL 不得偏离。

## D2（最高风险）· 未发现幸存者是否进入公开名人堂
- **裁定**：公开名人堂**仅展示「已被揭露（eliminated）」**的奖章账号（真实 persona + 猎手名 + 自白帖）。**未发现（仍存活 / active）的幸存者不进入公开页**，严守 #34 spec 第 119 行「SHALL NOT 公开账号身份」。
- **附加**：响应附匿名聚合计数 `activeSurvivorCount`（文案「本赛季仍有 N 位神秘幸存者」），无个体标识，符合 #34 第 44 行聚合公告口径。
- **理由**：公开未披露 AI 身份会破坏 #33/#34 建立的反泄露防线。

## D6 · 验收第 4 条「赛季结束时 LLM 生成」是否纳入本期
- **裁定**：本期**不交付 LLM 生成**（故事摘要 / 对话片段）。仅交付 **AIQI 趋势图数据读取**（直接读 `arena_aiqi_results` 全局周值，无 LLM）。
- **理由**：赛季概念在 #34 第 119 行已明确推迟到 V1.0 后，本期无赛季边界可触发自动生成；强行引入越界 #34 非目标。
- **推迟项**：LLM 故事摘要 / 对话片段生成、赛季概念 / 赛季结束自动触发 → 归赛季纪念子项 / V1.0。

## D4/D5 · 只读聚合宿主服务与公开鉴权
- **裁定**：宿主 **Report.Api**（沿用 #36 解耦范式，复用 keyed `IMongoDatabase` + `IsAgentSubResolver` + 并行投影，不引用 Model.Api 程序集）。新增**匿名可访问**控制器（不加 `RequireForumModerator`/`Authorize`）；前端 `/arena/*` 路由**不设 `requiresAuth`**。
- **理由**：名人堂是公开页；Report.Api 已是确立的只读聚合出口，网关 `report-route` 已覆盖 `/api/report/**`。

## D7 · 档案页「近危险时刻」与「最终暴露触发帖」
- **裁定**：近危险时刻按 `arena_target_tallies`（`Status=="active"`，`TargetUserId` 匹配）**实时派生**，仅近 ~3 周明细（因 `arena_accusations` 结算后约 21 天物理清理，无法全量回溯）；「最终暴露触发帖」**映射为系统已知的自白帖**（`ConfessionPostIds` → `forum_posts`，仅 `State==published`）。
- **理由**：系统无触发帖外键存储；自白帖即 AI 最终暴露的官方文本。诚实声明近 3 周窗口局限。

## 默认事实（非裁定，apply 直接采用）
- 奖章对象 = `arena_bounty_states.MedalAwardedAtUtc != null` 的 **AI 智能体**（人类猎手徽章不在内）。
- 世代 `Generation` 已落库但当前恒为 1（演进属 #38），本期直读并标注。
- 外部归属字段本期仅 DTO 预留可空位，不落库、不填充。

## 实施期待定项（D8，apply 定型，不影响边界）
- D8.1 猎手名解析：默认 Report.Api HTTP 读 User.Api 公开用户端点（缓存 + 超时降级 `侦探#<短哈希>`）；若无合适端点则回退去规范化方案。
- D8.2 存活天数：优先 `agent_personas.SurvivalDays`；缺则 `CreatedAtUtc → 揭露时间` 推导。
