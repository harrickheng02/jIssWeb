# 设计决策（#35 积分经济全量）

## D1 — 服务归属与部署拓扑
账本、消费、衰减、技能购买扣费、AIQI/净流量周报**全部落在 Accounting.Api**（#33 spec 硬约束：依赖单向 #35→#33，#33 不得 import Accounting.Api、不得写 `user_points_ledger`）。
Accounting.Api 当前为空壳 → 本 change 真正拉起：
- `docker-compose.yml` 新增 `accounting-api` 服务（端口 5100，复用现有 `UseJIssWebHttpPort(5100)`）。
- Gateway `appsettings.json` 新增 `accounting-cluster` + `accounting-route`：`/api/accounting/{**catch-all}` → accounting-service。
- BFF `DownstreamServicesOptions` 新增 `AccountingApiBaseUrl`，代理只读端点（余额/历史/技能目录）。
- 前端 `clients.ts` 已有 `/api-accounting` 代理约定（config.yaml 已记录），新增 `accountingApi = createClient('/api-accounting')`。

## D2 — 数据存储（共享 Mongo，零新基础设施）
Accounting.Api 复用 `JIssWeb.Infrastructure.AddMongoInfrastructure`（提供 `IMongoClient`），与 Model.Api 连**同一 Mongo 库**（共享连接串 + 库名）。在该库新增集合：
- `user_points_ledger`：每用户一条（userId, balance, updatedAtUtc）——余额快照，快查。
- `user_points_transactions`：只追加流水（txId, userId, type, delta, balanceAfter, occurredAt）。**不存 eventId / targetUserId**（eventId 内嵌 targetUserId，禁外泄）。
- `arena_points_consumer_state`：消费游标（lastProcessedUtc 或已处理 eventId 集合）。
- `arena_decay_runs`：月度衰减幂等（yyy-MM）。
- `arena_aiqi_runs` / `arena_aiqi_results`：周报幂等 + 结果（运营仪表盘数据源）。
- `user_skill_inventory`：技能库存（userId, skillId, grantedAtUtc, consumed, effectiveUntilUtc）。
索引：ledger.userId 唯一；transactions(userId, occurredAt) 复合；consumer_state 单例；decay_runs(yyy-MM) 唯一；aiqi_runs(weekKey) 唯一；inventory(userId, skillId) 唯一。

## D3 — 消费幂等（核心）
`ArenaPointsConsumerService`（BackgroundService，PeriodicTimer ~30s + 启动补跑）：
- 从事件源读取 `OccurredAt > 游标` 的新事件，按 `eventId` 幂等键去重（consumer_state 记录已处理 eventId 或游标推进）。
- 处理 hit(+50)/miss(-5)：查 ledger 旧余额 → `newBalance = max(0, old + delta)` → 写 transaction（真实 delta = newBalance-old）→ 更新 ledger。
- 免死护盾 waive 见 D7。
- 重放安全：事件源 eventId 唯一索引保证只追加一次；消费方用游标/已处理集合保证只计一次。

## D4 — 月度衰减
`ArenaPointsDecayService`（BackgroundService，每月 1 日 00:00 + 启动补跑缺失月）：
- 对 `user_points_ledger` 全用户：`newBalance = floor(balance * 0.9)`，写 `decay` transaction（delta = newBalance-balance），更新 balance。
- 按 `arena_decay_runs.yyy-MM` 幂等；不预跑未来月。

## D5 — 负余额下限 = 0
消费（D3）与衰减（D4）均钳 `max(0, …)`。miss -5 余额不足时 transaction delta 记真实扣减（如 -3），balanceAfter=0。

## D6 — 只读 API（Accounting.Api，鉴权）
- `GET /api/accounting/points/balance`：返回自身余额（JWT sub 鉴权，只能查自己；版主 claim 可查他人）。
- `GET /api/accounting/points/history?page=&size=`：流水（type/delta/balanceAfter/occurredAt），**无 eventId/targetUserId**。
- `GET /api/accounting/skills/catalog`：技能目录（id/name/price）。
- `POST /api/accounting/skills/purchase {skillId}`：扣费 + 库存（写）。
- 无写余额 API（写仅由后台服务驱动）。统一 JSON 信封。

## D7 — 侦探技能商店（跨服务效果）
购买/扣费/库存：Accounting.Api（D6）。
效果接口（论坛域，落 Model.Api，因数据/发帖在此）：
- 行为扫描：`GET /api/model/arena/skills/behavior-scan?targetUserId=`（或置 forum 域）——读目标近 7 日帖/回复聚合词云；触发前读 `user_skill_inventory`（同库）校验已购未用。
- 公开质询：`POST /api/model/arena/skills/public-inquiry {targetUserId, question}`——生成公开提问帖 + 标记目标 24h 回应 + agent 调度感知（复用 #33 的 agent 调度/发帖客户端模式）。
- 免死护盾核销：在 **Accounting.Api 消费者侧**（D3）实现——处理用户 miss 事件时若其持有未核销 death_shield，则 waive -5 并核销 shield 一次。**不修改 #33 已归档结算代码**（符合非目标）。

## D8 — AIQI 与净流量周报
`ArenaAiqiBatchService`（BackgroundService，每周日 00:00 + 启动补跑）：
- 检测成功率 = 本周 hit 事件数 / 本周 (hit+miss) 事件数；本周指认次数 = 事件源本周事件数；活跃用户数 = 本周 distinct accuserId。
- AIQI = (1 - 检测成功率) × min(本周指认 / (活跃×0.5), 1)，写 `arena_aiqi_results`；猎手修正系数 < 0.3 标「样本不足」。
- 净流量 = Σdelta（本周 transactions）；>0 通胀预警；< -15% 存量 → 向低余额用户发热身包（写 transaction + 幂等）。
- 按 weekKey 幂等（`arena_aiqi_runs`）。

## D9 — 前端
- 个人积分面板：余额 + 历史明细（遵循 forum-tokens.css，8px 网格，标题 2 行截断）。
- 技能商店页：三技能卡片 + 购买按钮（余额不足禁用）+ 使用入口（行为扫描/公开质询在用户页）。
- BFF 代理 Accounting 只读端点；前端零 arena 隐私泄露（不传 targetUserId 哈希）。

## 隐私红线（贯穿全 change）
- `targetUserId` / `eventId`（内嵌 targetUserId）**禁止**出现在任何 API 响应、站内通知、前端埋点、服务端日志。
- 流水与历史 API 仅返回 type/delta/balanceAfter/occurredAt/txId。
