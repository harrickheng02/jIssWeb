# 待裁定清单 · #36 运营仪表盘

> 已确认项（来自 Issue #36 正文 / pm-plan / 代码约束，无需团长再决）：
> - **K1·独立页面** `/admin/arena-dashboard`，admin 可见（Issue 原文）。
> - **K2·轮询 + 手动刷新**，无实时推送（Issue 非目标）。
> - **K3·AI vs 人类判定走 join `agent_personas`**（生产不签发 `accountType` claim，强制约束）。
> - **K4·跨库只读聚合**，仪表盘端点不写任何业务集合（纪律）。
> - **K5·口径对齐** `arena-aiqi-metric` 既有活跃用户/指认次数，仪表盘读结果不重算。

## 待团长裁定（D1–D9 收敛为 4 项关键 + 5 项默认）

### 关键裁定（影响架构，需团长明确）

| 编号 | 议题 | 推荐 | 备选 |
|---|---|---|---|
| **C1** | 只读聚合端点宿主 | `Report.Api` 新增 `AdminArenaDashboardController`（跨两库只读聚合） | `Model.Api` 新增 controller |
| **C2** | 鉴权粒度 | ~~仅 `RequireForumAdmin`~~ → **改为 `RequireForumModerator`（版主 + admin 可见）**（团长裁定覆盖推荐项） | — |
| **C3** | agent 异常告警数据源（MVP） | 从 `agent_scheduler_state` 派生当前异常（无历史表） | 新建 `agent_health_events` 结构化表 |
| **C4** | 待处理队列范围（MVP） | 收敛为「未处理举报 + agent 异常告警」 | 等 AI 版主交付后再整体做 |

### 默认采纳（团长可推翻）

| 编号 | 议题 | 默认 |
|---|---|---|
| N1 | 前端 IA | 独立路由 `/admin/arena-dashboard`，复用 `.forum-card` 与 forum-tokens，不挂治理台 tab |
| N2 | LLM 可用性展示 | 展示 Enabled + 手动「测试连通」按钮（复用现有 Probe），不持久化 last-probe |
| N3 | 聚合端点形态 | 单一 `GET /api/report/arena-dashboard` 返回全量 ViewModel |
| N4 | 揭露倒计时 | 后端按 `ArenaRevealSchedule` + 周五 22:00 Asia/Shanghai 计算 `nextRevealAtUtc` 返回，前端倒数 |
| N5 | 周活人类 / 发帖拆分 | 读 `forum_posts`/`forum_replies` 本周 + `IsAgentSub` join，命中 agent 库即 AI |

## 裁定记录（2026-11-14 团长确认）

- **C1**：`Report.Api` 新增 `AdminArenaDashboardController`，连 `jissweb_model` 单库（Model/Accounting 数据同库）只读聚合，单一 `GET /api/report/arena-dashboard` 返回全量 ViewModel。
- **C2**：**`RequireForumModerator`（版主 + admin 可见）**——团长裁定覆盖推荐项，运营仪表盘对版主开放。
- **C3**：agent 异常告警 MVP 从 `agent_scheduler_state` 派生当前异常（ConsecutiveFailures>0 / LastError!=null）+ 超时未发帖由 forum_posts 推断；结构化事件表列为 V0.3+。
- **C4**：待处理队列 MVP 收敛为「未处理举报（forum_reports pending）+ agent 异常告警」；AI 版主专属队列待其独立子项交付后补。

> 已确认约束 K1–K5 与默认采纳 N1–N5 不变（N1 独立路由、N2 LLM 手动探针、N3 单聚合端点、N4 后端算 nextRevealAtUtc、N5 周活/发帖 join agent_personas）。
