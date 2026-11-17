# 任务：注册护栏

## 1. 配置与选项

- [x] 1.1 新增 `Options/RegistrationSettings.cs`：`SectionName = "Registration"`、`Enabled = false`、`DailyCap = 50`、`PerIpDailyCap = 3`、`TimeZoneId = "Asia/Shanghai"`
- [x] 1.2 `User.Api/Program.cs` 绑定配置段，并加 `PostConfigure` 兜底（负值归零、空时区回落 `Asia/Shanghai`）
- [x] 1.3 `appsettings.json` 新增 `Registration` 段（默认关闭）
- [x] 1.4 `appsettings.Local.example.json` 新增 `Registration` 段并注明与 `Mongo:UserDatabaseName` 的对应关系

## 2. 日计数与时间边界

- [x] 2.1 新增 `Services/RegistrationDailyCounter.cs`：`GetLocalDate(TimeZoneId)`、`SecondsUntilNextLocalMidnight(TimeZoneId)`、`ReadAsync(scope, key)`、`IncrementAsync(scope, key)`
- [x] 2.2 时区非法时回落 UTC 并记 warning，不抛异常
- [x] 2.3 单元测试：跨零点秒数计算、时区非法回落、零上限语义

## 3. 注册端点接入

- [x] 3.1 `AuthController.Register` 在参数校验后插入总闸检查（403 `REGISTRATION_CLOSED`），覆盖「已存在未验证账号」重发分支
- [x] 3.2 在每分钟限流之后插入日上限预检（全局 + IP），超限返回 429 `REGISTRATION_DAILY_CAP_REACHED` 并带 `Retry-After`
- [x] 3.3 建号成功后递增全局与 IP 日计数（失败路径一律不递增）
- [x] 3.4 `GET /api/auth/registration-status`（匿名）返回 `{ enabled, remainingToday }`
- [x] 3.5 测试：总闸关闭拒绝 / 精确校验错误不被掩盖 / 重发分支被拦 / 全局上限 / IP 上限隔离 / 零上限 / 校验失败不扣配额 / 成功建号双计数 / 状态端点三场景

## 4. Report.Api 注册面板

- [x] 4.1 `appsettings.json` 新增 `Mongo:UserDatabaseName`（默认 `jissweb_user`）
- [x] 4.2 新增用户库投影 DTO 与 `DashboardCollectionNames.Users`
- [x] 4.3 `ArenaDashboardService` 新增注册面板聚合（`todayRegistered` / `todayVerified` / `pendingVerification` / `dailyCap` / `remainingToday`）
- [x] 4.4 时区与 `Registration:TimeZoneId` 对齐（Report.Api 侧独立配置，默认同值）
- [x] 4.5 测试：今日流入计数、未验证统计、User.Api 不可达不影响（仅 Mongo 依赖）

## 5. 前端

- [x] 5.1 `clients.ts` 新增 `RegistrationStatusDto` 与 `getRegistrationStatus()`
- [x] 5.2 `clients.ts` 错误码映射补充 `REGISTRATION_CLOSED`、`REGISTRATION_DAILY_CAP_REACHED`
- [x] 5.3 `AuthView.vue` 挂载时查询状态：关闭态禁用注册表单并展示说明文案；剩余名额为 0 时提示
- [x] 5.4 `AdminArenaDashboardView.vue` 新增注册流入卡片（今日注册/已验证/待验证 + 上限水位条）
- [x] 5.5 前端测试：状态查询与错误码映射

## 6. 全量验证

- [x] 6.1 `dotnet build backend/src/JIssWeb.sln` 0 错 0 警
- [x] 6.2 新增测试全绿；`User.Api.Tests` 与 `Report.Api.Tests` 全量通过
- [x] 6.3 前端 `vitest` + `vue-tsc -b` + `vite build` 通过
- [x] 6.4 `openspec validate registration-rollout-gate --strict`
- [x] 6.5 change-review：逐条比对 spec 与实现，确认无遗漏 Requirement / Scenario

## 7. 提交与收尾

- [x] 7.1 提交（Conventional Commits 中文）、推送分支、开 PR、等 CI 双 job 通过、合并 master
- [x] 7.2 `openspec archive registration-rollout-gate --yes`；`pm-plan.yaml` 同步 V0.3 里程碑勾选与 OpenSpec 备注；推送归档提交
