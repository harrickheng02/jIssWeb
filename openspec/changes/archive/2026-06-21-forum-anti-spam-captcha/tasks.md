## 1. 后端配置与服务层（User.Api）

- [x] 1.1 新增 `CaptchaSettings` options 类（字段：`Enabled: bool`、`SecretKey: string`、`TimeoutSeconds: int = 5`）；在 `Program.cs` 绑定 `"Captcha"` 配置节
- [x] 1.2 新增 `ICaptchaVerifier` 接口（方法：`Task<bool> VerifyAsync(string token, string? remoteIp)`）；实现 `TurnstileCaptchaVerifier`，调用 CF siteverify API，超时 fail-closed，超时 / 异常记录 Warning 日志
- [x] 1.3 在 `Program.cs` 注册 `IHttpClientFactory` 命名客户端 `"turnstile"`（baseAddress = `https://challenges.cloudflare.com`）并注册 `TurnstileCaptchaVerifier` 为 `ICaptchaVerifier` 单例
- [x] 1.4 更新 `appsettings.Local.example.json`（User.Api）新增 `"Captcha": { "Enabled": true, "SecretKey": "" }` 占位符

## 2. 后端注册端点改造（AuthController）

- [x] 2.1 `RegisterRequest` 新增 `CaptchaToken?: string` 字段
- [x] 2.2 在 `AuthController.Register()` 现有频率限流之后、业务逻辑之前插入 CAPTCHA 校验分支：`Enabled == false` → 跳过；`accountType claim == "agent"` → 跳过（桩）；`token` 空 → `400 CAPTCHA_REQUIRED`；验证失败 → `400 CAPTCHA_INVALID`
- [x] 2.3 `AuthController` 构造函数注入 `ICaptchaVerifier` 与 `IOptions<CaptchaSettings>`
- [x] 2.4 `CaptchaSettings.Enabled = true` 且 `SecretKey` 为空时，启动阶段写入 `Warning` 日志

## 3. 后端集成测试（backend/tests/JIssWeb.User.Api.Tests/）

- [x] 3.1 在 `JIssWeb.User.Api.Tests.csproj` 添加 `Moq` 包引用（版本与 Model.Api.Tests 一致）
- [x] 3.2 新增 `CaptchaRegistrationFixture`：继承 `UserSanctionsIntegrationFixture` 模式；在 `WithWebHostBuilder` 中调用 `services.RemoveAll(typeof(IConnectionMultiplexer)); services.AddSingleton(_ => Mock.Of<IConnectionMultiplexer>())` mock Redis；注册 `ICaptchaVerifier` 测试替换（可通过构造参数切换 pass/fail）
- [x] 3.3 测试用例：`captchaToken` 缺失 → 400 `CAPTCHA_REQUIRED`（`Captcha:Enabled=true`）
- [x] 3.4 测试用例：`captchaToken` 无效（verifier 返回 false）→ 400 `CAPTCHA_INVALID`
- [x] 3.5 测试用例：`captchaToken` 有效（verifier 返回 true）→ 200，注册流程正常推进
- [x] 3.6 测试用例：`Captcha:Enabled=false` 时无 token 也能通过（开发旁路）
- [x] 3.7 测试用例：请求 Header 携带伪造 `accountType=agent` JWT claim 时无 token 也能通过（桩路径，手动构造 JWT）

## 4. 配置文件与环境变量

- [x] 4.1 `.env.example` 末尾 VITE 区块新增 `VITE_TURNSTILE_SITE_KEY=`（前端 widget 用，可选，**不加入** `vite.config.ts` 的 `requireEnv` 检查）；非 VITE 区块新增 `TURNSTILE_SECRET_KEY=`（后端 siteverify 用，经 docker-compose 透传为 `Captcha__SecretKey` 环境变量）
- [x] 4.2 确认 `.env` 和 `appsettings.Local.json` 均在 `.gitignore` 中（不提交真实密钥）

## 5. 前端 Turnstile widget 集成（AuthView.vue）

- [x] 5.1 安装 `vue-turnstile`（原 tasks 中的包名 `@marsidev/vue-turnstile` 不存在，实际包名为 `vue-turnstile`）并更新 `frontend/package.json`
- [x] 5.2 `AuthView.vue` 注册 Tab 中引入 `<VueTurnstile>` widget，读取 `import.meta.env.VITE_TURNSTILE_SITE_KEY`，widget 回调将 token 写入 `registerForm.captchaToken`；widget 包裹容器的 margin/padding 使用 `--space-*` CSS 变量（禁止硬编码 px 值）
- [x] 5.3 更新 `canSubmitRegister` 计算属性，新增 `registerForm.captchaToken` 非空条件
- [x] 5.4 注册请求（`register()` 函数）body 新增 `captchaToken` 字段
- [x] 5.5 `getAuthErrorMessage()` 新增 `CAPTCHA_REQUIRED` / `CAPTCHA_INVALID` 中文映射；验证失败后调用 widget `.reset()` 方法

## 6. 验证完成

- [x] 6.1 运行 `cd backend && dotnet test tests/JIssWeb.User.Api.Tests` 确认所有后端测试通过（含新增 CAPTCHA 测试）
- [x] 6.2 运行 `cd frontend && npm test` 确认前端测试无回归
- [x] 6.3 启动本地服务（`CAPTCHA_ENABLED=false`），手动验证注册流程端到端正常
- [x] 6.4 对照 spec 逐条核查 SHALL/MUST 条款均已覆盖
