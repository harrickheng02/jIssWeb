# 测试清单 · #36 运营仪表盘

> 后端单测 / 集成测试 + 前端单测。覆盖聚合正确性、鉴权、AI vs 人类判定、倒计时、空态。

## 后端（Report.Api / 聚合服务）

### B1 · 鉴权
- `AdminArenaDashboardController`：admin → 200；moderator → 403；member → 403；无 token → 401。
- `IsAgentSub(sub)`：agent sub → true；human sub → false；未知 → false（不读 claim）。

### B2 · 聚合正确性（Mongo 集成测试，见 G6 备注）
- 存活/总 agent：`agent_personas` 注入 3 active + 1 eliminated → 存活 3 / 总 4。
- 待揭露队列：`arena_reveal_jobs` 注入 completed=1 + settling=2 → 队列数 2。
- AIQI/净流量：注入 `arena_aiqi_results` / `arena_netflow_results` 一条本周记录 → 端点返回与记录一致（不重算）。
- 本周指认次数：`arena_accusations` 注入本周 5 条 + 上周 3 条 → 返回 5。
- 传说级猎物：`arena_bounty_states` 注入 IsLegendaryPrey=true×2 + false×1 → 列表 2。
- Top3 侦探：`arena_accusations` 注入多 user 多周 → 本周分组计数取前 3 正确。
- 周活人类 / 发帖 AI vs 人类：`forum_posts` 本周若干 + `agent_personas` 已知 agent sub → 拆分计数正确（AI=命中 agent 库，人类=其余去重）。
- 未处理举报：`forum_reports` 注入 pending×2 + resolved×1 → 计数 2。

### B3 · agent 异常派生（D5）
- `agent_scheduler_state` 注入 ConsecutiveFailures>0 ×1 + LastError!=null ×1 + 正常 ×2 → 异常列表 2，含错误摘要。
- 超时未发帖：某 agent 的 forum_posts 最近发帖时间 > 期望节奏阈值 → 标记为「超时未发帖」异常。

### B4 · 揭露倒计时（D4/N4）
- `ComputeNextRevealUtc()`：给定任意时间 → 返回下一个周五 22:00 Asia/Shanghai；跨周/跨月边界正确；返回 UTC 且带时区说明。

### B5 · LLM 可用性（D6）
- 仪表盘 LLM 卡片返回各 profile 的 Enabled 状态；「测试连通」按钮调用现有 Probe 返回 ok / failed 透传。

### B6 · 只读纪律（G2）
- 聚合路径不触发任何写：集成测试后断言相关集合文档数 / 字段未变。

## 前端（Vitest + 点验）

### F1 · 路由守卫
- `/admin/arena-dashboard`：admin → 可进；member → 重定向 home（无数据请求泄露）。

### F2 · ViewModel 渲染
- 6 面板在空数据 / 有数据 / 加载中 / 错误态均正确渲染；`.forum-card` 指标卡复用 forum-tokens（无硬编码色）。
- 经济健康三色：净流量 >0 预警色、<-15% 危险色、其余健康色。
- AIQI 置信度：`SampleSufficient=false` 显示「样本不足」徽标。
- 揭露倒计时：从 `nextRevealAtUtc` 实时递减显示。

### F3 · 手动刷新
- 刷新按钮触发重新 GET；轮询定时器（可选）按 N 秒触发，卸载清理。

## 验收（非代码）
- 巡检 ≤15 分钟自测记录：admin 账号在 docker 全套下完成一遍 6 面板巡检并计时。
