## Why

注册频率限流（change-B）在流量层拦截高速机器人，但对低速分布式 botnet 无效；现有注册链路缺乏"发起方是否为人类"的身份门槛，导致批量机器账号可低成本渗透。接入 Cloudflare Turnstile 无感验证作为注册的身份层防御，从源头拦截机器人，避免大量无效未验证账号积压并消耗邮件资源。

## What Changes

- **User.Api**：`RegisterRequest` 新增 `CaptchaToken` 字段；`AuthController.Register()` 在现有频率限流之后、业务逻辑之前插入 CAPTCHA 校验步骤；新增 `ICaptchaVerifier` 接口与 `TurnstileCaptchaVerifier` 实现；新增 `CaptchaSettings` 选项类（含 `Enabled` 开关，`false` 时跳过验证，用于本地开发）。
- **前端**：`AuthView.vue` 注册 Tab 嵌入 Turnstile widget；`captchaToken` 非空才允许提交；验证失败（`CAPTCHA_REQUIRED` / `CAPTCHA_INVALID`）给出可辨识错误提示。
- **Agent 豁免桩**：`Register()` 在 CAPTCHA 校验前读取 JWT claim `accountType`，值为 `agent` 时跳过验证；此分支当前为死代码（生产环境从不签发该 claim），待 #30 落地后自动激活，**无需再次修改此文件**。
- **配置**：`.env` 新增 `TURNSTILE_SITE_KEY` / `TURNSTILE_SECRET_KEY`；`appsettings.Local.json`（User.Api）新增 `Captcha` 节。

## Capabilities

### New Capabilities

- `registration-captcha`：用户注册端点的 CAPTCHA 验证行为契约——何时必须提供 token、错误码语义、agent 豁免条件、开发环境旁路规则。

### Modified Capabilities

（无现有 spec 的行为要求变更）

## Impact

- **服务边界**：User.Api（注册端点 + 新服务层）；前端 SPA（AuthView.vue）；无 Model.Api 改动（首次发帖 CAPTCHA 不做）。
- **外部依赖**：Cloudflare Turnstile verify API（`https://challenges.cloudflare.com/turnstile/v0/siteverify`）；前端新增 `vue-turnstile`（`@marsidev/vue-turnstile` 不存在 Vue 版本，实际使用 `vue-turnstile` by ruigomes）。
- **非目标**：首次发帖 CAPTCHA；短信 / 邮件验证；设备指纹 SDK；agent 账号创建协议（属于 #30）；登录环节 CAPTCHA。
