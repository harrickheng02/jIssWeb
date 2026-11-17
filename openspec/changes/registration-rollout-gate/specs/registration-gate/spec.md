## ADDED Requirements

> 注册护栏（图灵场 V0.3「小范围开放注册」）。写路径宿主 `JIssWeb.User.Api`；只读投影宿主 `JIssWeb.Report.Api`，不得引用 `JIssWeb.User.Api` 程序集，也不得引入服务间 HTTP 调用。新增配置段 `Registration`（默认关闭，关闭时注册端点一律拒绝）。既有 `EmailVerification:RegisterPerMinuteLimit` 的每分钟限流语义保持不变，本能力只在其之上叠加日级约束。

### Requirement: Registration master switch is closed by default

系统 SHALL 通过配置段 `Registration` 控制注册准入，字段至少含 `Enabled`（默认 `false`）、`DailyCap`、`PerIpDailyCap`、`TimeZoneId`。当 `Enabled` 为 `false` 时，`POST /api/auth/register` SHALL 返回 403 与错误码 `REGISTRATION_CLOSED`，SHALL NOT 创建账号、SHALL NOT 写入任何日计数、SHALL NOT 发送验证邮件。

总闸 SHALL NOT 拦截 `POST /api/auth/resend-verification`——该端点是既有未验证用户的恢复路径，保留自身的冷却与日上限约束。

#### Scenario: Closed switch rejects new registration

- **WHEN** `Registration:Enabled` 为 `false` 且请求 `POST /api/auth/register` 携带合法邮箱与强密码
- **THEN** 响应 SHALL 为 403 且错误码为 `REGISTRATION_CLOSED`
- **AND** `users` 集合 SHALL 不新增文档
- **AND** SHALL NOT 调用验证邮件发送器

#### Scenario: Invalid payload still reports precise validation error

- **WHEN** `Registration:Enabled` 为 `false` 且请求密码不满足强度要求
- **THEN** 响应 SHALL 为 400 且错误码为 `WEAK_PASSWORD`
- **AND** SHALL NOT 返回 `REGISTRATION_CLOSED`

#### Scenario: Resend path for an existing unverified account is also closed

- **WHEN** `Registration:Enabled` 为 `false` 且请求邮箱对应一个已存在但未验证的账号
- **THEN** 响应 SHALL 为 403 且错误码为 `REGISTRATION_CLOSED`
- **AND** SHALL NOT 发送验证邮件

### Requirement: Daily caps bound total and per-IP registrations

系统 SHALL 在总闸开启时施加两级日上限：全局 `DailyCap` 与单 IP `PerIpDailyCap`。上限 SHALL 按 `TimeZoneId` 的本地日期分桶，并在跨过本地零点后自动重置。任一上限达到时，`POST /api/auth/register` SHALL 返回 429 与错误码 `REGISTRATION_DAILY_CAP_REACHED`，并 SHALL 在 `Retry-After` 响应头返回距下一个本地零点的秒数。

`DailyCap` 或 `PerIpDailyCap` 为 `0` SHALL 表示该维度不限制。配置值为负数 SHALL 被规整为 `0`。`TimeZoneId` 无法解析时 SHALL 回落到 UTC，且 SHALL NOT 使注册端点失败。

#### Scenario: Global daily cap reached

- **WHEN** 总闸开启且今日全局成功注册数已达到 `DailyCap`
- **THEN** 后续注册请求 SHALL 返回 429 与错误码 `REGISTRATION_DAILY_CAP_REACHED`
- **AND** `Retry-After` SHALL 为正整数秒

#### Scenario: Per-IP daily cap reached

- **WHEN** 总闸开启且同一 IP 今日成功注册数已达到 `PerIpDailyCap`，但全局配额仍有剩余
- **THEN** 该 IP 的注册请求 SHALL 返回 429 与错误码 `REGISTRATION_DAILY_CAP_REACHED`
- **AND** 其他 IP 的注册请求 SHALL 不受影响

#### Scenario: Zero disables the corresponding cap

- **WHEN** `DailyCap` 为 `0`
- **THEN** 系统 SHALL NOT 因全局日上限拒绝注册

#### Scenario: Counters reset at local midnight

- **WHEN** 本地日期从 `D` 跨到 `D+1`
- **THEN** 全局与各 IP 的当日计数 SHALL 归零

### Requirement: Daily quota is consumed only by successful account creation

日计数 SHALL 仅在账号成功创建之后递增。请求在参数校验、总闸检查、每分钟限流、日上限预检、CAPTCHA 校验、昵称查重任一环节失败时，SHALL NOT 消耗全局或 IP 日配额。

#### Scenario: Validation failure does not consume quota

- **WHEN** 总闸开启且 `DailyCap` 尚有剩余，请求因密码强度不足被拒绝
- **THEN** 全局与 IP 的当日计数 SHALL 保持不变

#### Scenario: Duplicate nickname does not consume quota

- **WHEN** 总闸开启且请求因昵称已被占用被拒绝
- **THEN** 全局与 IP 的当日计数 SHALL 保持不变

#### Scenario: Successful creation consumes both counters

- **WHEN** 账号创建成功
- **THEN** 全局当日计数 SHALL 加 1
- **AND** 该请求来源 IP 的当日计数 SHALL 加 1

### Requirement: Anonymous registration status endpoint

系统 SHALL 提供匿名可访问的 `GET /api/auth/registration-status`，返回 `{ enabled: boolean, remainingToday: number }`。当总闸关闭时 `remainingToday` SHALL 为 `0`。响应 SHALL NOT 包含 `DailyCap`、`PerIpDailyCap` 等配置原值，SHALL NOT 包含任何用户或 IP 维度信息。当 `DailyCap` 为 `0`（不限制）时 `remainingToday` SHALL 为 `-1`，避免与「名额用尽」的 `0` 混淆。

#### Scenario: Status reflects closed switch

- **WHEN** `Registration:Enabled` 为 `false` 且未携带任何凭据
- **THEN** 响应 SHALL 为 200，`enabled` 为 `false`，`remainingToday` 为 `0`

#### Scenario: Status reflects remaining quota

- **WHEN** `Registration:Enabled` 为 `true` 且今日已成功注册 `k` 个账号
- **THEN** `remainingToday` SHALL 为 `max(DailyCap - k, 0)`

#### Scenario: Unlimited cap is distinguishable from exhausted quota

- **WHEN** `Registration:Enabled` 为 `true` 且 `DailyCap` 为 `0`
- **THEN** `remainingToday` SHALL 为 `-1`
- **AND** 前端 SHALL 将 `-1` 展示为「不限」而非「0 个名额」

#### Scenario: Status does not leak configuration

- **WHEN** 调用 `GET /api/auth/registration-status`
- **THEN** 响应体 SHALL NOT 出现 `DailyCap` 或 `PerIpDailyCap` 字段

### Requirement: Dashboard projects registration inflow read-only

运营仪表盘 SHALL 展示注册流入面板，至少含 `todayRegistered`（本地今日新建账号数）、`todayVerified`（其中已完成邮箱验证的数量）、`pendingVerification`（全库未验证账号数）、`dailyCap` 与 `remainingToday`。

该面板 SHALL 由 `JIssWeb.Report.Api` 以 Mongo 只读投影实现，SHALL NOT 引用 `JIssWeb.User.Api` 程序集，SHALL NOT 发起服务间 HTTP 调用。用户库名 SHALL 由 `Mongo:UserDatabaseName` 配置提供，默认值与 `JIssWeb.User.Api` 的 `Mongo:DatabaseName` 对齐。

#### Scenario: Dashboard reports today's inflow

- **WHEN** 用户库中存在 3 个 `CreatedAtUtc` 落在本地今日的账号，其中 2 个已验证
- **THEN** `todayRegistered` SHALL 为 3，`todayVerified` SHALL 为 2

#### Scenario: Dashboard does not require User.Api availability

- **WHEN** `JIssWeb.User.Api` 不可达
- **THEN** 仪表盘注册面板 SHALL 仍能返回数据
