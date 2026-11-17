# 提案：注册护栏（registration-rollout-gate）

## 背景

V0.3「可运营」的最后一项是**小范围开放注册**。技术前置大部分已就绪：邮箱验证注册闭环、注册限流（每邮箱 + 每 IP，每分钟 5 次）、Turnstile 人机验证（可选）、AI 版主内容兜底、运营仪表盘、账号轮换器。

但「开放」这个动作本身缺少可运营的护栏，运营者没有手段在流量异常时快速止损：

1. **没有注册总闸**。注册端点无任何开关，出事只能改代码重启。
2. **只有每分钟限流，没有日上限**。`IsRateLimitedAsync("register", email, 5)` 按邮箱与 IP 各自计数、窗口 1 分钟——换个邮箱、等一分钟即可继续，挡不住持续性的批量注册。小范围开放时真正需要的是「今天最多进多少人」。
3. **运营者看不到注册流入**。仪表盘有 agent 水位、积分、举报、待审队列，但没有「今日注册数」，无法判断是否该关闸。

## 目标

让运营者可以用**配置**而不是**发版**来控制在册增长：

- 一键开关注册（`Registration:Enabled`，默认关闭）
- 全局日上限 + 单 IP 日上限，按本地日期自动重置
- 注册页能提前告知「当前未开放」，而不是填完表单才报错
- 运营仪表盘展示今日注册流入与上限水位

## 非目标

- 邀请码 / 白名单机制（V0.3 不需要，配置开关 + 日上限足够）
- 注册审核（人工批准）流程
- 与第三方风控服务集成
- 按邮箱域名黑白名单

## 方案要点

### 总闸默认关闭

`Registration:Enabled` 默认 `false`，即**部署后默认不接受新注册**。这与仓库既有的安全默认一致（`AgentRotation:Enabled`、`Forum:ContentModeration:Enabled`、`ArenaReveal:Enabled` 均为默认关闭）。

总闸关闭时，`POST /auth/register` 一律返回 `403 REGISTRATION_CLOSED`，包括「邮箱已存在但未验证」的重发分支——否则该分支会变成可被滥用的发信通道。既有未验证用户的正常恢复路径由独立的 `POST /auth/resend-verification` 承担，不受总闸影响。

### 日上限只在建号成功后计数

全局计数与 IP 计数都在**成功创建账号之后**递增，而不是在请求进入时。若在入口计数，几次参数校验失败（密码强度不足、昵称重复）就会吃掉配额，运营者会看到「上限用尽」但实际一个号都没建成。

计数用 Redis `StringIncrementAsync` + 到期时间设为「到下一个本地零点」，与既有 `IsRateLimitedWindowAsync` 同一套模式，不引入新依赖。

### 匿名状态端点

`GET /auth/registration-status` 返回 `{ enabled, remainingToday }`。注册页据此在挂载时就渲染「暂未开放注册」或剩余名额，避免用户填完整张表单才发现被拒。该端点不泄露任何用户信息，也不返回上限的具体配置值。

### 仪表盘只读投影

Report.Api 维持「只读投影、不引用其他服务程序集、不发服务间 HTTP」的既有边界，新增 `Mongo:UserDatabaseName` 配置，以 DTO 投影直接读 `users` 集合的 `CreatedAtUtc` / `EmailVerifiedAtUtc`。这与它读 `jissweb_model` 的方式完全一致。

## 影响

- **User.Api**：新增 `RegistrationSettings` 配置段；`AuthController.Register` 增加总闸与日上限两道校验；新增匿名状态端点
- **Report.Api**：新增 `Mongo:UserDatabaseName`；仪表盘新增注册面板
- **前端**：`clients.ts` 新增状态查询与两个错误码映射；`AuthView.vue` 渲染未开放态；`AdminArenaDashboardView.vue` 新增注册卡片
- **配置**：`appsettings.json` 与 `appsettings.Local.example.json` 新增 `Registration` 段
- **兼容性**：默认关闭，现有部署升级后注册会立即停止——这是**有意为之**的安全默认，但需在发布说明中明确提示运营者开启

## 验收

- 总闸关闭时注册返回 `REGISTRATION_CLOSED`，且不写库、不发信
- 日上限达到后返回 `REGISTRATION_DAILY_CAP_REACHED` 并带 `Retry-After`
- 参数校验失败不消耗日配额
- 状态端点匿名可访问，返回正确的 `enabled` 与 `remainingToday`
- 仪表盘注册面板数值与库内真实数据一致
