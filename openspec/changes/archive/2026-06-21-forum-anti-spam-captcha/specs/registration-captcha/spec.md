## ADDED Requirements

### Requirement: 注册请求必须携带有效的 CAPTCHA token

`POST /api/auth/register` 接受请求体字段 `captchaToken`（字符串）。当 `CaptchaSettings.Enabled` 为 `true` 且请求方不具备 agent 豁免条件时，系统 SHALL 在执行任何业务逻辑前验证该 token 的有效性。

#### Scenario: token 缺失

- **WHEN** 请求体中 `captchaToken` 为 `null` 或空字符串，且 CAPTCHA 开关已启用
- **THEN** 返回 `400 Bad Request`，响应码为 `CAPTCHA_REQUIRED`

#### Scenario: token 无效

- **WHEN** 请求体中携带 `captchaToken`，但 Turnstile siteverify API 返回 `success: false`，且 CAPTCHA 开关已启用
- **THEN** 返回 `400 Bad Request`，响应码为 `CAPTCHA_INVALID`

#### Scenario: token 有效，注册正常进行

- **WHEN** 请求体中携带有效 `captchaToken`，Turnstile siteverify 返回 `success: true`，CAPTCHA 开关已启用
- **THEN** CAPTCHA 验证通过，后续注册业务逻辑正常执行（现有行为不变）

---

### Requirement: 开发环境旁路开关

当 `CaptchaSettings.Enabled` 为 `false` 时，系统 SHALL 完全跳过 CAPTCHA 验证，不调用外部 siteverify API，不要求请求体携带 `captchaToken`。

#### Scenario: 开关关闭时无需 token

- **WHEN** `CaptchaSettings.Enabled = false`，且请求体中不含 `captchaToken`
- **THEN** CAPTCHA 校验步骤被跳过，注册流程按原有逻辑执行

---

### Requirement: AI 智能体账号豁免 CAPTCHA（桩实现）

当请求携带的 JWT 中包含 claim `accountType = "agent"` 时，系统 SHALL 跳过 CAPTCHA 验证，不要求 `captchaToken`。当前生产环境从未签发此 claim，该分支在 #30（AI 智能体账号协议）落地前为无效分支；#30 实现后无需修改本文件。

#### Scenario: agent claim 存在时豁免（桩）

- **WHEN** 请求 JWT 包含 `accountType: agent` claim（#30 落地后生效）
- **THEN** CAPTCHA 校验步骤被跳过，注册流程按原有逻辑执行

---

### Requirement: Turnstile 验证超时策略

`ICaptchaVerifier.VerifyAsync` 调用外部 siteverify API 时 SHALL 设置最大等待时间（默认 5 秒）。超时或网络异常时，系统采用 fail-closed 策略：记录 `Warning` 日志并返回与验证失败相同的 `400 CAPTCHA_INVALID`。

#### Scenario: siteverify 超时

- **WHEN** 调用 Cloudflare siteverify API 超过 5 秒无响应
- **THEN** 返回 `400 Bad Request`，响应码为 `CAPTCHA_INVALID`；后台产生 `Warning` 级别日志

---

### Requirement: 前端 Turnstile widget 集成

注册表单 (`AuthView.vue`) SHALL 在提交按钮之前渲染 Turnstile widget。`captchaToken` 由 widget 回调填充；在 `captchaToken` 为空时提交按钮 SHALL 保持禁用状态。

#### Scenario: widget 验证完成前无法提交

- **WHEN** 用户填写完注册表单，但 Turnstile widget 尚未完成验证（`captchaToken` 为空）
- **THEN** 注册提交按钮保持禁用状态，无法触发 API 请求

#### Scenario: 验证失败后显示可辨识错误提示

- **WHEN** 服务端返回 `CAPTCHA_REQUIRED` 或 `CAPTCHA_INVALID`
- **THEN** 前端在注册表单下方显示具体的中文错误提示，且 Turnstile widget 自动重置以供用户再次尝试

---

### Requirement: 密钥通过环境配置传入，不硬编码

`TURNSTILE_SITE_KEY`（前端读取）和 `TURNSTILE_SECRET_KEY`（User.Api 后端读取）SHALL 通过 `.env` / `appsettings.Local.json` 注入；两者均 SHALL NOT 提交至版本控制。`.env.example` 和 `appsettings.Local.example.json` 须包含对应占位符。

#### Scenario: 密钥缺失时服务启动警告

- **WHEN** `CaptchaSettings.Enabled = true` 且 `SecretKey` 为空字符串
- **THEN** User.Api 启动时记录 `Warning` 级别日志提示密钥未配置；实际注册请求以 `CAPTCHA_INVALID` 拒绝
