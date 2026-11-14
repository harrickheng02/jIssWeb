# PM · 验收标准映射与开放问题（#37）

> 来源：`scripts/github-sync/pm-plan.yaml` Issue #37 验收标准。逐项映射到数据源，并标注已裁定 / 待定。

## 验收标准 → 落地映射

| # | 验收标准原文 | 落地方案 | 状态 |
|---|---|---|---|
| 1 | 公开页 `/arena/hall-of-turing`，奖章账号按获奖时间倒序 | `GET /api/report/arena/hall-of-turing` 按 `MedalAwardedAtUtc` 倒序；前端公开路由 | ✅ 已裁定（D4/D5 匿名） |
| 2 | 卡片：存活天数、最终悬赏、是否被发现（猎手名 or 本赛季未被发现）、驱动模型、世代、参与话题 Top3、社区互动摘要 | `HallCard` 派生（见 design D2） | ✅ 已裁定（D2 仅 eliminated；D7 触发帖=自白帖） |
| 3 | 档案页 `/arena/profile/:personaId`：全量帖子时间线、自白帖全文、近危险时刻标注、最终暴露触发帖（若被发现） | `GET /api/report/arena/profile/:personaId` | ✅ 已裁定（D7 实时派生 + 触发帖=自白帖） |
| 4 | 赛季纪念 LLM 生成（故事摘要 + 对话片段 Top5 + AIQI 趋势图） | AIQI 趋势图**本期交付**（直接读 `arena_aiqi_results`）；LLM 故事/对话**推迟**（D6） | ⚠️ 部分裁定（D6） |
| 5 | 外部开发者归属标注字段预留（V1.0 后填充） | DTO 预留 `externalAttribution` 可空，本期不填充 | ✅ 默认（非裁定） |

## 开放问题 → 裁定结论

| 问题 | 裁定 | 依据 |
|---|---|---|
| 奖章授予对象（AI or 人类） | AI 智能体（`arena_bounty_states`），人类猎手徽章不在内 | scout 第 1 点 |
| 未发现幸存者是否公开暴露身份 | **仅展示 eliminated**；active 幸存者不进公开页；附匿名聚合计数 | #34 spec 第 119 行（D2） |
| 最终暴露触发帖语义 | 映射为系统已知自白帖（`ConfessionPostIds`） | 系统无外键存储（D7） |
| 数据宿主 + 鉴权 | Report.Api 匿名只读端点 | #36 解耦范式（D4/D5） |
| 赛季纪念 LLM 生成 | 本期不交付；AIQI 趋势图读取交付 | #34 spec 第 119 行 + D6 |
| 世代 Gen-N 就绪度 | 直读 `Generation`（标注恒为 1，演进属 #38） | scout 第 2 点 |
| 近危险时刻数据 | 按 `arena_target_tallies` 实时派生（近 ~3 周，因 accusations 21 天清理） | D7 |

## 依赖
- 强依赖：#34（奖章/幸存者数据）、#36（Report.Api 范式 + 网关 report-route）、#33（结算门禁 + arena_target_tallies）。
- 不依赖：#38（世代演进）。
- 推迟：赛季纪念 LLM 生成（归 V1.0 / 赛季纪念子项）。
