# 设计：注册护栏

## 上下文

`AuthController.Register` 当前顺序：邮箱/密码校验 → 密码强度 → 每分钟限流（邮箱 + IP）→ CAPTCHA → 资料解析与昵称查重 → 已存在账号分支（重发验证邮件）→ 建号 → 发信。

限流实现 `IsRateLimitedAsync(bucket, email, limitPerMinute)` 对邮箱哈希与 IP 哈希各做一次 `IsRateLimitedWindowAsync`（Redis `INCR` + `KeyExpire`，窗口 1 分钟）。既有日级限流先例是 `GetResendDailyLimitKey(email)` + `ResendPerDayLimit`，窗口 `TimeSpan.FromDays(1)`。

## 决策

### D1 总闸默认关闭，且拦截重发分支

`Registration:Enabled` 默认 `false`。关闭时 `POST /auth/register` 在**参数校验之后、限流之前**返回 `403 REGISTRATION_CLOSED`。

放在参数校验之后是为了让非法请求仍然得到 `INVALID_INPUT` / `WEAK_PASSWORD` 这类精确错误，而不是被总闸掩盖——调试与前端提示都更准确。

**重发分支一并拦截**：若只拦新建、放行「已存在未验证账号」的重发，该分支就成为一条不需要总闸许可的发信通道，可被用于对既有邮箱做验证邮件轰炸。既有未验证用户的恢复路径是独立的 `POST /auth/resend-verification`（有自己的冷却与日上限），不受总闸影响。

### D2 日上限按本地日期分桶，时区显式配置

计数 key 形如 `register:day:global:{yyyy-MM-dd}` 与 `register:day:ip:{sha256(ip)}:{yyyy-MM-dd}`，TTL 设为「到下一个本地零点的秒数」。

**不用服务器本地时间**：容器默认 UTC，「今天」的分界会落在北京时间早上 8 点，运营者看到的「今日注册数」会在上午突然归零。改为 `Registration:TimeZoneId`（默认 `Asia/Shanghai`），与 `ArenaReveal:TimeZoneId` 同一套显式时区策略。时区 ID 非法时回落到 UTC 并记 warning，不让注册端点因为一个配置笔误而 500。

### D3 只在建号成功后计数，IP 计数同样延后

全局与 IP 计数都在账号**成功写入之后**递增。

若在入口计数，任何一次失败尝试（密码强度不足、昵称重复、CAPTCHA 失败）都会吃掉配额。小范围开放时日上限可能只有几十，几次失败就足以让运营者看到「上限用尽」却一个号都没建成——这是最容易被误判为故障的失效模式。

代价：`remainingToday` 的语义是「还能成功建几个」，而不是「还能尝试几次」。这正是运营者关心的口径。

### D4 预检与计数分离，接受有界竞态

流程拆成两步：入口处**读**计数做预检（超限即 429），建号成功后**写**计数。

并发请求可能同时通过预检，导致实际注册数略微超过 `DailyCap`，超出量不超过并发数。要做到严格不超限需要 Lua 脚本或分布式锁，为一个默认关闭、小范围开放的开关引入这类复杂度不值得。**明确接受该竞态并在此记录**，避免后续被当成 bug 反复调查。

### D5 状态端点匿名且只暴露必要信息

`GET /auth/registration-status` → `{ enabled: bool, remainingToday: int }`。

- 匿名可访问：注册页在未登录状态就要渲染该状态
- `remainingToday` 在总闸关闭时返回 0，不返回上限本身，避免把运营参数暴露给外部
- 不返回 IP 维度的剩余量（那会把 IP 计数变成可探测的侧信道）

### D6 Report.Api 通过 Mongo 投影读取用户库，不引服务间依赖

Report.Api 的既有边界是「只读投影、不引用其他服务程序集、无 HttpClient」。为注册指标引入 `IUserServiceClient` 会破坏这条边界，还会把仪表盘可用性绑到 User.Api 上。

改为新增 `Mongo:UserDatabaseName`（默认 `jissweb_user`），以 DTO 投影读 `users` 的 `CreatedAtUtc` / `EmailVerifiedAtUtc`。与它读 `jissweb_model` 完全同构。

指标口径：
- `todayRegistered`：`CreatedAtUtc` 落在本地今日的账号数
- `todayVerified`：其中 `EmailVerifiedAtUtc` 非空的数量
- `pendingVerification`：全库 `EmailVerifiedAtUtc` 为空的账号数（不限于今日，用于发现「注册了但从不验证」的异常）

`dailyCap` 与 `remainingToday` 一并返回，供前端画水位条。

### D7 配置集中在 `Registration` 段，不与 `EmailVerification` 混放

`EmailVerification:RegisterPerMinuteLimit` 已有，但语义是「发信频率」；`Registration` 段承载的是「是否允许进人」。两者失效时的处置动作不同（一个调限流，一个关总闸），混在一起会让运营者在紧急时找错开关。

```
Registration:
  Enabled: false
  DailyCap: 50
  PerIpDailyCap: 3
  TimeZoneId: Asia/Shanghai
```

`PostConfigure` 兜底：`DailyCap < 0` → 0（0 表示不限制，与 `DailyLlmCallCap` 语义一致）；`PerIpDailyCap < 0` → 0；空时区 → `Asia/Shanghai`。

## 数据流

```
POST /auth/register
  ├─ 参数校验（INVALID_INPUT / WEAK_PASSWORD）
  ├─ 总闸检查 ──关闭──> 403 REGISTRATION_CLOSED
  ├─ 每分钟限流（邮箱 + IP）──超限──> 429 RATE_LIMITED
  ├─ 日上限预检（全局 + IP）──超限──> 429 REGISTRATION_DAILY_CAP_REACHED + Retry-After
  ├─ CAPTCHA
  ├─ 昵称查重 / 已存在分支 ──已存在──> 409 EMAIL_EXISTS 或 VERIFICATION_EMAIL_SENT
  ├─ 建号 + 发信
  └─ 递增日计数（全局 + IP）
```

## 风险

| 风险 | 处置 |
| --- | --- |
| 升级后注册默认停止，运营者以为故障 | 发布说明明确提示；状态端点与前端未开放态给出清晰文案 |
| 并发导致轻微超限 | D4 已记录为可接受 |
| 时区配置错误导致日界漂移 | 非法 ID 回落 UTC + warning 日志 |
| 用户库名与 User.Api 不一致 | `Mongo:UserDatabaseName` 默认值与 User.Api `Mongo:DatabaseName` 对齐，并在 `appsettings.Local.example.json` 注明 |
