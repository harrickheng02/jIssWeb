# Proposal: turing-arena-bounty-medal

## Problem

#34「悬赏累积与图灵奖章事件」代码与规范层零实现，且原验收标准与 #33 已归档契约冲突：点名式全站公告会主动暴露 AI 身份；全额悬赏与冻结的 `arena_points_events`（`type`/`delta`）不兼容；「赛季结束强制揭露」越界到 #37/#38；`SurvivalDays` 无自动递增、AIQI 为全站周级单值而非 per-agent。四份调研 + 两轮共 7 项团长裁定已收口，需落为可实施契约。

## Solution

新增 `arena-bounty-medal` 能力：

1. **悬赏累积**：以 `CreatedAtUtc` + #33 周键用纯函数推导 `min(BasePoints × 2^(w-1), CapPoints)`，每周批次幂等 `Set`，状态落独立集合，绝不 ×2 累加（否则启动补跑 12 周会放大 4096 倍）。
2. **积分经济**：不新增 `type`、不新增字段，只把 `delta` 从冻结 +50 放开为悬赏值；每位成功指认者各获全额 + 双层封顶；#35 消费者零改动自动入账，AIQI 分母不变。
3. **聚合公告与徽章**：公告只发聚合信息、零字段扩展、零门禁字段、写入即可见；新增图灵猎手徽章与个人中心只读视图。
4. **幸存者只打标记**：不做强制揭露/名人堂/经验继承；`AIQI > 0.8` 写为「禁止降质、禁止主动暴露」的约束声明。
5. **自愈**：一次性事件用「≥ `MedalWeeks` 且未发放」下界触发，被跳过的周补跑时恰好发放一次；零改动 #33 状态机。

灰度 `Enabled` 默认 `false`，关闭时零写入、积分回落恰好 +50。

## Non-goals

名人堂/档案/赛季纪念（#37）、经验提取器与继承（#38）、仪表盘传说级猎物列表（#36）、赛季制框架（V1.0 后）、强制揭露分支、点名公告、悬赏转让/技能永久解锁、新增事件 `type` 或字段、新增公告写端点或可见性门禁字段、修复 #33/#35 既有偏差（周键、字段命名、N+1、in-progress 续跑）。

## 影响范围

- **服务边界**：Model.Api 为唯一写入方（悬赏计算、事件 `delta`、公告、徽章）；Accounting.Api **零代码改动**（仅消费语义变化）；Frontend 新增个人中心只读视图；User.Api / BFF / Gateway 零改动。**不新增公告写入端点**，`GET /api/forum/announcements` 响应契约不变；新增一个当前用户徽章只读端点 `GET /api/arena/hunter-badge/me`（`[Authorize]`、按 JWT sub 隔离），作为徽章展示的唯一数据源。
- **前端 UI**：徽章入口/视图 SHALL 遵循 `forum-tokens.css`（CSS 变量、8px 网格、无硬编码色、标题 2 行截断、不用 `:deep` 覆盖 EP），见 `.cursor/rules/forum-ui.mdc`。
- **契约**：ADDED `arena-bounty-medal`；MODIFIED `arena-points-events`、`arena-weekly-reveal`（逐条同步 `+50` 措辞 + `:29` 门禁作用域澄清）；其余主规范零改动。
- **需求文案**：`pm-plan.yaml:328` 与 GitHub Issue #34 正文点名文案按 C1 改为聚合口径，归档时同步。
