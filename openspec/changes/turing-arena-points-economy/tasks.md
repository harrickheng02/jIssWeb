# 任务清单（#35 积分经济全量）

## 1. 部署拓扑：Accounting.Api 拉起

- [x] 1.1 `docker-compose.yml` 新增 `accounting-api` 服务（端口 5100，挂载 Mongo/Redis）；`appsettings.Local.example.json` 补全 Mongo/Redis/内部服务段
- [x] 1.2 Gateway `appsettings.json` 新增 `accounting-cluster` + `accounting-route`（`/api/accounting/{**catch-all}` → accounting-service）；BFF `DownstreamServicesOptions` 新增 `AccountingApiBaseUrl` 并代理只读端点

## 2. 积分账本核心（消费 + 余额 + 流水）

- [x] 2.1 `ArenaMongoSetup` 扩展：注册 `user_points_ledger` / `user_points_transactions` / `arena_points_consumer_state` 集合与索引（ledger.userId 唯一、transactions(userId,occurredAt)、consumer_state 单例）
- [x] 2.2 实现 `ArenaPointsConsumerService`（BackgroundService，PeriodicTimer + 启动补跑）：读事件源、按 eventId 幂等、写 ledger/transaction；含消费者集成 fixture 测试（重放不重复计、hit/miss 正确）
- [x] 2.3 负余额下限 0：消费钳 `max(0, …)`、transaction 记真实 delta；测试（余额不足 miss 钳 0、记真实扣减）
- [x] 2.4 实现 `ArenaPointsDecayService`（每月 1 日 + 启动补跑缺失月）：全用户 -10%、记 decay 流水、按 yyy-MM 幂等；测试（同月不重复、补跑缺失月、不预跑未来月）
- [x] 2.5 只读 API：`GET /api/accounting/points/balance`、`GET /api/accounting/points/history`（响应零 eventId/targetUserId）；鉴权（仅自身/版主）；集成测试（隐私红线、越权拒绝）

## 3. 侦探技能商店

- [x] 3.1 技能目录常量 + `GET /api/accounting/skills/catalog`；`POST /api/accounting/skills/purchase {skillId}` 扣费 + 库存（`user_skill_inventory`）；余额不足拒绝；集成测试（购买扣费、余额不足拒绝）
- [x] 3.2 Model.Api `GET .../arena/skills/behavior-scan?targetUserId=`：读 `user_skill_inventory` 校验已购、聚合目标近 7 日词云；集成测试（未购拒绝、返回词云、零隐私泄露）
- [x] 3.3 Model.Api `POST .../arena/skills/public-inquiry {targetUserId,question}`：生成公开提问帖 + 24h 回应标记 + agent 调度感知；集成测试（未购拒绝、生成标记帖）
- [x] 3.4 免死护盾 waive：消费者处理用户 miss 事件时若持有未核销 death_shield 则 waive -5 并核销；不修改 #33 结算代码；集成测试（护盾吸收误判、核销一次）

## 4. AIQI 指标与净流量周报

- [x] 4.1 `ArenaAiqiBatchService`（每周日 + 启动补跑）：检测成功率/hit+miss、AIQI 公式、样本不足标注、写 `arena_aiqi_results`、按 weekKey 幂等；集成测试（公式边界、样本不足、幂等）
- [x] 4.2 净流量周报：本周 Σdelta；>0 通胀预警；< -15% 存量向低余额用户发热身包（transaction + 幂等）；集成测试（通胀预警、热身包发放、幂等）

## 5. 前端

- [x] 5.1 个人积分面板组件（余额 + 历史明细），遵循 forum-tokens.css、8px 网格、标题 2 行截断；vitest
- [x] 5.2 技能商店页（三技能卡片 + 购买按钮 + 余额不足禁用）+ 用户页使用入口（行为扫描/公开质询）；BFF 代理只读端点；vitest

## 6. 验证与交付

- [x] 6.1 全量 `dotnet test`（Arena 套件 + 新增账本/技能/AIQI 测试），隔离 42 条环境性失败，零新增回归
- [x] 6.2 `npx vitest run` + `vue-tsc` 0 错误 + 生产构建
- [ ] 6.3 change-review 逐条核对 SHALL/MUST → 中文提交 → PR → 合并 → `/opsx:archive` → 改 `pm-plan.yaml` #35 closed → `npm run pm:push`
